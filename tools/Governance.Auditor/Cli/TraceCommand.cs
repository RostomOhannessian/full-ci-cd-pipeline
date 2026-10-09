using System.CommandLine;
using Governance.Auditor.Common;
using Governance.Auditor.Status;
using Governance.Auditor.Trace;

namespace Governance.Auditor.Cli;

internal static class TraceCommand
{
    public static Command Create(CliContext context)
    {
        var check = new Option<bool>("--check") { Description = "Do not write. Fail when the report differs from what the tests and evidence give." };
        var output = new Option<string>("--output") { Description = "The report to write, relative to the repository root.", DefaultValueFactory = _ => TraceFiles.ReportPath };
        var all = new Option<bool>("--all") { Description = "Treat every active requirement as in scope, to see what is still missing." };
        var phase = new Option<string?>("--phase") { Description = "Treat every requirement that this phase and the earlier ones deliver as in scope. Use it at a phase exit gate. Values: 0 to 4, or launch." };
        var summary = StatusCommands.SummaryOption();

        var command = new Command("trace", "Map requirements to the tests and evidence that prove them, and fail when an in-scope requirement has none.")
        {
            check, output, all, phase, summary,
        };

        command.SetAction(parseResult =>
        {
            var files = context.Files(parseResult);
            List<Finding> findings = [];

            var status = StatusFile.Load(files, findings);
            var requirements = TraceFiles.LoadRequirements(files, findings);
            var evidence = TraceFiles.LoadEvidence(files, findings);

            if (status is null || requirements is null || evidence is null)
            {
                return context.Report("trace", findings, parseResult.GetValue(summary));
            }

            var options = new TraceOptions(parseResult.GetValue(all), ParsePhase(parseResult.GetValue(phase)));
            var result = TraceAnalyzer.Analyze(requirements, status, ProofScanner.Scan(files), evidence, files, options);
            findings.AddRange(result.Findings);

            var rendered = TraceRenderer.Render(requirements, status, result.Coverage);
            var path = parseResult.GetValue(output)!;

            if (parseResult.GetValue(check))
            {
                var current = files.FileExists(path) ? files.ReadAllText(path).ReplaceLineEndings("\n") : null;

                if (!string.Equals(current, rendered, StringComparison.Ordinal))
                {
                    findings.Add(Finding.Error("trace-drift", "The report is not what the requirements, tests, and evidence give. Run 'governance trace' and commit the result.", path));
                }
            }
            else
            {
                files.WriteAllText(path, rendered);
                context.Output.WriteLine($"trace: wrote {path}.");
            }

            return context.Report("trace", findings, parseResult.GetValue(summary));
        });

        return command;
    }

    private static PhaseId? ParsePhase(string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (string.Equals(value, "launch", StringComparison.Ordinal) || (int.TryParse(value, out var number) && number is >= 0 and <= 4))
        {
            return new PhaseId(value);
        }

        throw new GovernanceException($"--phase must be 0 to 4 or launch, and was '{value}'.");
    }
}
