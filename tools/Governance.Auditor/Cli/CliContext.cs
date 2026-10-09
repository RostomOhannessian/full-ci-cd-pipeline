using System.CommandLine;
using Governance.Auditor.Common;

namespace Governance.Auditor.Cli;

/// <summary>Everything a command needs from the outside world. Tests replace the writers and the process runner.</summary>
internal sealed class CliContext
{
    public CliContext(TextWriter output, TextWriter error, IProcessRunner processes, Func<string, string?> environment)
    {
        Output = output;
        Error = error;
        Processes = processes;
        Environment = environment;
        RootOption = new Option<string?>("--root")
        {
            Description = "The repository root. Defaults to the Git repository that contains the current directory.",
            Recursive = true,
        };
    }

    public TextWriter Output { get; }

    public TextWriter Error { get; }

    public IProcessRunner Processes { get; }

    public Func<string, string?> Environment { get; }

    public Option<string?> RootOption { get; }

    public RepositoryFiles Files(ParseResult parseResult)
    {
        var root = parseResult.GetValue(RootOption);
        return root is null ? RepositoryFiles.Locate(System.Environment.CurrentDirectory) : new RepositoryFiles(root);
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
        var passed = errors == 0;

        Output.WriteLine(passed
            ? $"{title}: passed ({warnings} warnings)."
            : $"{title}: failed ({errors} errors, {warnings} warnings).");

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

        return $"{heading}{errors} errors and {warnings} warnings.\n\n{table}{more}\n";
    }
}
