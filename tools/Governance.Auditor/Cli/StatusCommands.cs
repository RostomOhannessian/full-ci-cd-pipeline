using System.CommandLine;
using Governance.Auditor.Common;
using Governance.Auditor.Status;

namespace Governance.Auditor.Cli;

internal static class StatusCommands
{
    public static Command Create(CliContext context)
    {
        var command = new Command("status", "Validate docs/project/status.yaml and render docs/project/STATUS.md from it.");
        command.Subcommands.Add(CreateValidate(context));
        command.Subcommands.Add(CreateRender(context));
        return command;
    }

    private static Command CreateValidate(CliContext context)
    {
        var summary = SummaryOption();
        var command = new Command("validate", "Check status.yaml against its schema and the rules a schema cannot express.") { summary };

        command.SetAction(parseResult =>
        {
            var files = context.Files(parseResult);
            List<Finding> findings = [];

            if (StatusFile.Load(files, findings) is { } status)
            {
                findings.AddRange(StatusValidator.Validate(status, files));
            }

            return context.Report("status validate", findings, parseResult.GetValue(summary));
        });

        return command;
    }

    private static Command CreateRender(CliContext context)
    {
        var check = new Option<bool>("--check") { Description = "Do not write. Fail when STATUS.md differs from what status.yaml renders." };
        var output = new Option<string>("--output") { Description = "The page to write, relative to the repository root.", DefaultValueFactory = _ => StatusFile.PagePath };
        var summary = SummaryOption();
        var command = new Command("render", "Render STATUS.md from status.yaml. Fails if status.yaml is not valid.") { check, output, summary };

        command.SetAction(parseResult =>
        {
            var files = context.Files(parseResult);
            List<Finding> findings = [];
            var status = StatusFile.Load(files, findings);

            if (status is not null)
            {
                findings.AddRange(StatusValidator.Validate(status, files));
            }

            if (status is null || findings.HasErrors())
            {
                return context.Report("status render", findings, parseResult.GetValue(summary));
            }

            var rendered = StatusRenderer.Render(status);
            var path = parseResult.GetValue(output)!;

            if (parseResult.GetValue(check))
            {
                var current = files.FileExists(path) ? files.ReadAllText(path).ReplaceLineEndings("\n") : null;

                if (!string.Equals(current, rendered, StringComparison.Ordinal))
                {
                    findings.Add(Finding.Error(
                        "status-drift",
                        "The page is not what status.yaml renders. Run 'governance status render' and commit the result.",
                        path,
                        current is null ? null : FirstDifferentLine(current, rendered)));
                }

                return context.Report("status render --check", findings, parseResult.GetValue(summary));
            }

            files.WriteAllText(path, rendered);
            context.Output.WriteLine($"status render: wrote {path}.");
            return 0;
        });

        return command;
    }

    internal static Option<string?> SummaryOption() => new("--summary")
    {
        Description = "Append a Markdown report to this file. In GitHub Actions, pass the job summary file.",
    };

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
