using System.CommandLine;
using Governance.Auditor.Common;

namespace Documentation.Auditor.Cli;

/// <summary>Everything a command needs from the outside world. Tests replace the writers and the clock.</summary>
internal sealed class CliContext
{
    public CliContext(TextWriter output, TextWriter error, TimeProvider time, string? workingDirectory = null)
    {
        Output = output;
        Error = error;
        Time = time;
        WorkingDirectory = workingDirectory ?? System.Environment.CurrentDirectory;
        RootOption = new Option<string?>("--root")
        {
            Description = "The repository root. Defaults to the Git repository that contains the current directory.",
            Recursive = true,
        };
    }

    public TextWriter Output { get; }

    public TextWriter Error { get; }

    /// <summary>The clock behind the freshness rule. A test fixes it, so a result does not depend on the day the test runs.</summary>
    public TimeProvider Time { get; }

    /// <summary>Where the search for the repository root starts when no <c>--root</c> is given.</summary>
    public string WorkingDirectory { get; }

    public Option<string?> RootOption { get; }

    public RepositoryFiles Files(ParseResult parseResult)
    {
        var root = parseResult.GetValue(RootOption);
        return root is null ? RepositoryFiles.Locate(WorkingDirectory) : new RepositoryFiles(root);
    }

    /// <summary>Prints the findings and returns the exit code: 0 when nothing is an error, and 1 otherwise.</summary>
    public int Report(string title, IReadOnlyCollection<Finding> findings, string? summaryPath = null)
    {
        foreach (var finding in findings.OrderByDescending(finding => finding.Severity))
        {
            Output.WriteLine(finding);
        }

        var errors = findings.Count(Severity.Error);
        var warnings = findings.Count(Severity.Warning);
        var notes = findings.Count(Severity.Note);
        var passed = errors == 0;

        Output.WriteLine(passed
            ? $"{title}: passed ({warnings} warnings, {notes} notes)."
            : $"{title}: failed ({errors} errors, {warnings} warnings, {notes} notes).");

        if (!string.IsNullOrEmpty(summaryPath))
        {
            MarkdownText.Append(summaryPath, FindingsSummary(title, findings));
        }

        return passed ? 0 : 1;
    }

    public static string FindingsSummary(string title, IReadOnlyCollection<Finding> findings)
    {
        var errors = findings.Count(Severity.Error);
        var warnings = findings.Count(Severity.Warning);
        var notes = findings.Count(Severity.Note);
        var heading = $"### {MarkdownText.Cell(title)}: {(errors == 0 ? "passed" : "failed")}\n\n";

        if (findings.Count == 0)
        {
            return heading + "No findings.\n\n";
        }

        var rows = findings
            .OrderByDescending(finding => finding.Severity)
            .Take(100)
            .Select<Finding, IReadOnlyList<string>>(finding =>
                [finding.Severity.ToString(), MarkdownText.Code(finding.Rule), MarkdownText.Cell(finding.Location), MarkdownText.Cell(finding.Message)]);

        var table = MarkdownText.Table(["Severity", "Rule", "Location", "Message"], rows);
        var more = findings.Count > 100 ? $"\nThe first 100 of {findings.Count} findings are shown.\n" : string.Empty;

        return $"{heading}{errors} errors, {warnings} warnings, and {notes} notes.\n\n{table}{more}\n";
    }
}
