using Documentation.Auditor.Cli;

namespace Documentation.Auditor;

internal static class Program
{
    public static Task<int> Main(string[] args)
    {
        var context = new CliContext(Console.Out, Console.Error, TimeProvider.System);
        return DocumentationCli.RunAsync(args, context, CancellationToken.None);
    }
}
