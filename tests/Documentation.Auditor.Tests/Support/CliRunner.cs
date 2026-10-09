using Documentation.Auditor.Cli;
using Xunit;

namespace Documentation.Auditor.Tests.Support;

internal sealed record CliResult(int ExitCode, string Output, string Error);

/// <summary>A clock that always says the same time, so the freshness rule gives the same answer on every day.</summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

internal static class CliRunner
{
    /// <summary>The date the tests treat as today. The pages in the sample repository were verified a week earlier.</summary>
    public static DateTimeOffset Today { get; } = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    public static async Task<CliResult> RunAsync(TestRepository repository, params string[] arguments) =>
        await RunAsync(repository.Root, arguments);

    /// <summary>Runs the command line against the real repository, which is how the self-check tests prove the repository is clean.</summary>
    public static async Task<CliResult> RunOnRealRepositoryAsync(params string[] arguments) =>
        await RunAsync(RealRepository.Root, arguments);

    private static async Task<CliResult> RunAsync(string root, string[] arguments)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var context = new CliContext(output, error, new FixedTimeProvider(Today));

        var exitCode = await DocumentationCli.RunAsync([.. arguments, "--root", root], context, TestContext.Current.CancellationToken);
        return new CliResult(exitCode, output.ToString().ReplaceLineEndings("\n"), error.ToString().ReplaceLineEndings("\n"));
    }
}
