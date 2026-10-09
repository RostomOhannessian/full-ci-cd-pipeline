using System.CommandLine;
using Governance.Auditor.Security;

namespace Governance.Auditor.Cli;

internal static class SecurityCommand
{
    public static Command Create(CliContext context)
    {
        var baseReference = new Option<string?>("--base") { Description = "The commit the pull request branches from. With it, every commit up to --head is checked for a noreply identity." };
        var head = new Option<string?>("--head") { Description = "The last commit of the range. Defaults to HEAD.", DefaultValueFactory = _ => "HEAD" };
        var rules = new Option<string[]>("--rule") { Description = "Run only this rule. Repeat the option to run several. By default every rule runs." };
        var summary = StatusCommands.SummaryOption();

        var command = new Command("security", "Run the security auditor: secret patterns, workflow permissions, action pinning, secrets usage, license policy, and commit identity.")
        {
            baseReference, head, rules, summary,
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var files = context.Files(parseResult);
            var policy = SecurityPolicy.Load(files);
            var baseValue = parseResult.GetValue(baseReference);
            var range = string.IsNullOrEmpty(baseValue) ? null : new GitRange(baseValue, parseResult.GetValue(head) ?? "HEAD");

            var findings = await SecurityAuditor.RunAsync(new SecurityContext(files, policy, context.Processes, range), parseResult.GetValue(rules) ?? [], cancellationToken);
            return context.Report("security", [.. findings], parseResult.GetValue(summary));
        });

        return command;
    }
}
