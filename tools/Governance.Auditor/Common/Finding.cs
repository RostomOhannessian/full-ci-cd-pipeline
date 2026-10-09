namespace Governance.Auditor.Common;

/// <summary>How serious a finding is. Only errors fail a check.</summary>
internal enum Severity
{
    Note = 0,
    Warning = 1,
    Error = 2,
}

/// <summary>
/// One thing a check found. A message never repeats a secret value or a personal email address, because findings reach logs and job
/// summaries.
/// </summary>
/// <param name="Rule">The stable rule ID, such as <c>action-pinning</c>.</param>
/// <param name="Severity">How serious it is.</param>
/// <param name="Message">What is wrong and how to fix it.</param>
/// <param name="Path">The repository-relative path with forward slashes, when the finding belongs to a file.</param>
/// <param name="Line">The one-based line, when it is known.</param>
internal sealed record Finding(string Rule, Severity Severity, string Message, string? Path = null, int? Line = null)
{
    public static Finding Error(string rule, string message, string? path = null, int? line = null) => new(rule, Severity.Error, message, path, line);

    public static Finding Warning(string rule, string message, string? path = null, int? line = null) => new(rule, Severity.Warning, message, path, line);

    public static Finding Note(string rule, string message, string? path = null, int? line = null) => new(rule, Severity.Note, message, path, line);

    public string Location => Path is null ? string.Empty : Line is null ? Path : $"{Path}:{Line}";

    public override string ToString() => Location.Length == 0
        ? $"{Severity.ToString().ToLowerInvariant()} [{Rule}] {Message}"
        : $"{Severity.ToString().ToLowerInvariant()} [{Rule}] {Location}: {Message}";
}

internal static class FindingExtensions
{
    public static bool HasErrors(this IEnumerable<Finding> findings) => findings.Any(finding => finding.Severity == Severity.Error);

    public static int Count(this IEnumerable<Finding> findings, Severity severity) => findings.Count(finding => finding.Severity == severity);
}
