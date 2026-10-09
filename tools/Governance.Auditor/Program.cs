using Governance.Auditor.Cli;
using Governance.Auditor.Common;

namespace Governance.Auditor;

internal static class Program
{
    public static Task<int> Main(string[] args)
    {
        var context = new CliContext(Console.Out, Console.Error, new SystemProcessRunner(), Environment.GetEnvironmentVariable);
        return GovernanceCli.RunAsync(args, context, CancellationToken.None);
    }
}
