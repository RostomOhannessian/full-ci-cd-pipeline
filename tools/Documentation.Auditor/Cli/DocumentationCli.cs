using System.CommandLine;
using Governance.Auditor.Common;

namespace Documentation.Auditor.Cli;

/// <summary>
/// The <c>documentation</c> command line. Exit codes: 0 means every check passed, 1 means a check failed or the arguments were wrong, and 2
/// means the tool could not do its work, for example because a file is missing. In every case a nonzero code means "do not merge".
/// </summary>
internal static class DocumentationCli
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
        var root = new RootCommand("Deterministic documentation checks for this repository: the tool inventory, the tool pages, and every Markdown page.");
        root.Options.Add(context.RootOption);

        foreach (var command in AuditCommands.Create(context))
        {
            root.Subcommands.Add(command);
        }

        return root;
    }
}
