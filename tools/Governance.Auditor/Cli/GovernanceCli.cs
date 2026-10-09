using System.CommandLine;
using Governance.Auditor.Common;

namespace Governance.Auditor.Cli;

/// <summary>
/// The <c>governance</c> command line. Exit codes: 0 means every check passed, 1 means a check failed or the arguments were wrong, and 2
/// means the tool could not do its work, for example because a file is missing. In every case a nonzero code means "do not merge".
/// </summary>
internal static class GovernanceCli
{
    public static async Task<int> RunAsync(string[] args, CliContext context, CancellationToken cancellationToken)
    {
        var root = Build(context);

        try
        {
            var parseResult = root.Parse(args);
            var configuration = new InvocationConfiguration { Output = context.Output, Error = context.Error, EnableDefaultExceptionHandler = false };
            return await parseResult.InvokeAsync(configuration, cancellationToken);
        }
        catch (GovernanceException exception)
        {
            context.Error.WriteLine($"error: {exception.Message}");
            return 2;
        }
    }

    public static RootCommand Build(CliContext context)
    {
        var root = new RootCommand("Deterministic governance checks for this repository: status, traceability, security, blast radius, and GitHub sync.");
        root.Options.Add(context.RootOption);
        root.Subcommands.Add(StatusCommands.Create(context));
        root.Subcommands.Add(TraceCommand.Create(context));
        root.Subcommands.Add(TestSummaryCommand.Create(context));
        root.Subcommands.Add(SecurityCommand.Create(context));
        root.Subcommands.Add(BlastRadiusCommand.Create(context));
        root.Subcommands.Add(GitHubSyncCommand.Create(context));
        return root;
    }
}
