using System.CommandLine;
using Governance.Auditor.Common;
using Governance.Auditor.TestSummary;

namespace Governance.Auditor.Cli;

internal static class TestSummaryCommand
{
    public static Command Create(CliContext context)
    {
        var results = new Option<string>("--results")
        {
            Description = "A TRX file, or a directory searched for *.trx files, relative to the repository root.",
            DefaultValueFactory = _ => "TestResults",
        };
        var summary = StatusCommands.SummaryOption();
        var command = new Command("test-summary", "Summarize TRX test results as Markdown, and fail when a test failed or no results exist.") { results, summary };

        command.SetAction(parseResult =>
        {
            var files = context.Files(parseResult);
            var path = parseResult.GetValue(results)!;
            var trxFiles = path.EndsWith(".trx", StringComparison.OrdinalIgnoreCase) ? [path] : files.ListFiles(path).Where(file => file.EndsWith(".trx", StringComparison.OrdinalIgnoreCase)).ToList();

            if (trxFiles.Count == 0)
            {
                // No results must never read as a pass, so a missing run fails the check.
                return context.Report("test-summary", [Finding.Error("test-summary-empty", $"No TRX files were found under '{path}'. Run the tests with --report-trx first.", path)], parseResult.GetValue(summary));
            }

            var summaries = trxFiles.Select(file => TrxParser.Parse(Path.GetFileName(file), files.ReadAllText(file))).ToList();
            var markdown = TrxParser.Render(summaries);
            context.Output.WriteLine($"test-summary: {summaries.Sum(item => item.Total)} results in {summaries.Count} files, {summaries.Sum(item => item.Failed)} failed.");

            if (parseResult.GetValue(summary) is { Length: > 0 } summaryPath)
            {
                MarkdownText.Append(summaryPath, markdown);
            }

            List<Finding> findings = [.. summaries.SelectMany(item => item.Failures.Select(failure => Finding.Error("test-failed", $"{failure.Test}: {failure.Message}", item.File)))];
            return context.Report("test-summary", findings);
        });

        return command;
    }
}
