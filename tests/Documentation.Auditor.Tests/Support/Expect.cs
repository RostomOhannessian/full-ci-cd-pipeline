using Xunit;

namespace Documentation.Auditor.Tests.Support;

/// <summary>Assertions on the output of a command, which names every finding as <c>severity [rule] location: message</c>.</summary>
internal static class Expect
{
    public static void Passes(CliResult result) => Assert.True(result.ExitCode == 0, $"Expected the audit to pass.\n{result.Output}{result.Error}");

    /// <summary>The audit fails, and every error it reports names the given rule, so the test proves that one cause fails and nothing else does.</summary>
    public static void FailsOnlyWith(CliResult result, string rule)
    {
        Assert.True(result.ExitCode == 1, $"Expected the audit to fail with {rule}.\n{result.Output}{result.Error}");
        var errors = Errors(result);
        Assert.NotEmpty(errors);
        Assert.All(errors, line => Assert.Contains($"[{rule}]", line, StringComparison.Ordinal));
    }

    public static IReadOnlyList<string> Errors(CliResult result) =>
        [.. result.Output.Split('\n').Where(line => line.StartsWith("error [", StringComparison.Ordinal))];

    public static IReadOnlyList<string> Warnings(CliResult result) =>
        [.. result.Output.Split('\n').Where(line => line.StartsWith("warning [", StringComparison.Ordinal))];

    public static IReadOnlyList<string> Notes(CliResult result) =>
        [.. result.Output.Split('\n').Where(line => line.StartsWith("note [", StringComparison.Ordinal))];
}
