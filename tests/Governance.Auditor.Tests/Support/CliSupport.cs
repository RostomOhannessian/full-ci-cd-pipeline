using Governance.Auditor.Cli;
using Governance.Auditor.Common;
using Xunit;

namespace Governance.Auditor.Tests.Support;

/// <summary>A process runner that answers from a list instead of starting anything. An unexpected request fails the test.</summary>
internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly List<(Func<ProcessRequest, bool> Matches, ProcessResult Result)> _responses = [];

    public List<ProcessRequest> Requests { get; } = [];

    public FakeProcessRunner On(Func<ProcessRequest, bool> matches, string standardOutput, int exitCode = 0, string standardError = "")
    {
        _responses.Add((matches, new ProcessResult(exitCode, standardOutput, standardError)));
        return this;
    }

    public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);

        foreach (var (matches, result) in _responses)
        {
            if (matches(request))
            {
                return Task.FromResult(result);
            }
        }

        throw new InvalidOperationException($"Unexpected process request: {request.FileName} {string.Join(' ', request.Arguments)}");
    }
}

internal sealed record CliResult(int ExitCode, string Output, string Error);

internal static class CliRunner
{
    public static async Task<CliResult> RunAsync(TestRepository repository, params string[] arguments) =>
        await RunAsync(repository, new FakeProcessRunner(), new Dictionary<string, string>(), arguments);

    public static async Task<CliResult> RunAsync(TestRepository repository, IProcessRunner processes, params string[] arguments) =>
        await RunAsync(repository, processes, new Dictionary<string, string>(), arguments);

    public static async Task<CliResult> RunAsync(TestRepository repository, IProcessRunner processes, IReadOnlyDictionary<string, string> environment, params string[] arguments)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var context = new CliContext(output, error, processes, name => environment.GetValueOrDefault(name));

        var exitCode = await GovernanceCli.RunAsync([.. arguments, "--root", repository.Root], context, TestContext.Current.CancellationToken);
        return new CliResult(exitCode, output.ToString().ReplaceLineEndings("\n"), error.ToString().ReplaceLineEndings("\n"));
    }

    /// <summary>Runs the command line against the real repository, which is how the self-check tests prove the repository is clean.</summary>
    public static async Task<CliResult> RunOnRealRepositoryAsync(params string[] arguments)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var context = new CliContext(output, error, new FakeProcessRunner(), _ => null);

        var exitCode = await GovernanceCli.RunAsync([.. arguments, "--root", RealRepository.Root], context, TestContext.Current.CancellationToken);
        return new CliResult(exitCode, output.ToString().ReplaceLineEndings("\n"), error.ToString().ReplaceLineEndings("\n"));
    }
}
