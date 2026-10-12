using System.CommandLine;
using Governance.Auditor.Common;
using Governance.Auditor.Status;
using Governance.Auditor.Usage;

namespace Governance.Auditor.Cli;

internal static class UsageCommand
{
    public static Command Create(CliContext context)
    {
        var command = new Command(
            "usage",
            "Validate docs/project/ai-usage.yaml, render docs/project/AI-USAGE.md from it, and estimate the AI units of the work packages that remain. Reads repository files only: the /ai-usage-recorder skill reads the session store.");
        command.Subcommands.Add(CreateValidate(context));
        command.Subcommands.Add(CreateRender(context));
        command.Subcommands.Add(CreateEstimate(context));
        return command;
    }

    private static Command CreateValidate(CliContext context)
    {
        var summary = StatusCommands.SummaryOption();
        var command = new Command("validate", "Check the ledger against its schema and the rules a schema cannot express.") { summary };

        command.SetAction(parseResult =>
        {
            var findings = Load(context.Files(parseResult), out var ledger, out var status);

            if (ledger is not null && status is not null)
            {
                findings.AddRange(UsageValidator.Validate(ledger, status));
            }

            return context.Report("usage validate", findings, parseResult.GetValue(summary));
        });

        return command;
    }

    private static Command CreateRender(CliContext context)
    {
        var check = new Option<bool>("--check") { Description = "Do not write. Fail when AI-USAGE.md differs from what the ledger renders." };
        var output = new Option<string>("--output") { Description = "The page to write, relative to the repository root.", DefaultValueFactory = _ => UsageFile.PagePath };
        var summary = StatusCommands.SummaryOption();
        var command = new Command("render", "Render AI-USAGE.md from the ledger. Fails if the ledger is not valid.") { check, output, summary };

        command.SetAction(parseResult =>
        {
            var files = context.Files(parseResult);
            var findings = Load(files, out var ledger, out var status);

            if (ledger is not null && status is not null)
            {
                findings.AddRange(UsageValidator.Validate(ledger, status));
            }

            if (ledger is null || status is null || findings.HasErrors())
            {
                return context.Report("usage render", findings, parseResult.GetValue(summary));
            }

            var rendered = UsageRenderer.Render(ledger, status);
            var path = parseResult.GetValue(output)!;

            if (parseResult.GetValue(check))
            {
                var current = files.FileExists(path) ? files.ReadAllText(path).ReplaceLineEndings("\n") : null;

                if (!string.Equals(current, rendered, StringComparison.Ordinal))
                {
                    findings.Add(Finding.Error(
                        "usage-drift",
                        "The page is not what the ledger renders. Run 'governance usage render' and commit the result.",
                        path,
                        current is null ? null : FirstDifferentLine(current, rendered)));
                }

                return context.Report("usage render --check", findings, parseResult.GetValue(summary));
            }

            files.WriteAllText(path, rendered);
            context.Output.WriteLine($"usage render: wrote {path}.");
            return 0;
        });

        return command;
    }

    private static Command CreateEstimate(CliContext context)
    {
        var phase = new Option<string?>("--phase") { Description = "Only the work packages of this phase: 0 to 4, or launch. The default is every phase." };
        var json = new Option<bool>("--json") { Description = "Print the result as one line of JSON instead of text." };
        var command = new Command(
            "estimate",
            "Estimate a range of AIU for each work package that remains, from the completed ones and by size, and print the method and its caveats. It informs planning and never blocks a merge.")
        {
            phase, json,
        };

        command.SetAction(parseResult =>
        {
            var findings = Load(context.Files(parseResult), out var ledger, out var status);

            if (ledger is not null && status is not null)
            {
                findings.AddRange(UsageValidator.Validate(ledger, status));
            }

            if (ledger is null || status is null || findings.HasErrors())
            {
                return context.Report("usage estimate", findings);
            }

            PhaseId? only = null;

            if (parseResult.GetValue(phase) is { } requested)
            {
                only = new PhaseId(requested);

                if (status.FindPhase(only.Value) is null)
                {
                    context.Error.WriteLine($"error: status.yaml has no phase '{requested}'. Use 0 to 4, or launch.");
                    return 1;
                }
            }

            var estimate = UsageEstimator.Estimate(ledger, status, only);
            context.Output.WriteLine(parseResult.GetValue(json) ? UsageEstimator.ToJson(estimate) : UsageEstimator.RenderText(estimate));
            return 0;
        });

        return command;
    }

    /// <summary>Loads the status file and the ledger. Either is null when it does not match its schema, and the reasons are in the findings.</summary>
    private static List<Finding> Load(RepositoryFiles files, out UsageLedger? ledger, out StatusDocument? status)
    {
        List<Finding> findings = [];
        status = StatusFile.Load(files, findings);
        ledger = UsageFile.Load(files, findings);
        return findings;
    }

    private static int FirstDifferentLine(string current, string expected)
    {
        var left = current.Split('\n');
        var right = expected.Split('\n');
        var count = Math.Min(left.Length, right.Length);

        for (var index = 0; index < count; index++)
        {
            if (!string.Equals(left[index], right[index], StringComparison.Ordinal))
            {
                return index + 1;
            }
        }

        return count + 1;
    }
}
