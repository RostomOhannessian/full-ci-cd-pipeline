using System.CommandLine;
using System.Globalization;
using Documentation.Auditor.Discovery;
using Documentation.Auditor.Inventory;
using Documentation.Auditor.Pages;
using Documentation.Auditor.Policy;
using Governance.Auditor.Common;

namespace Documentation.Auditor.Cli;

/// <summary>The three commands. <c>inventory</c> and <c>pages</c> each run half of the audit, and <c>audit</c> runs both.</summary>
internal static class AuditCommands
{
    public static IEnumerable<Command> Create(CliContext context)
    {
        var today = new Option<string?>("--today")
        {
            Description = "Treat this date (YYYY-MM-DD) as today, so the freshness rule gives the same answer on every day. Defaults to the current UTC date.",
        };
        var summary = new Option<string?>("--summary")
        {
            Description = "Append a Markdown report to this file. In GitHub Actions, pass the job summary file.",
        };

        yield return Define(
            context,
            "audit",
            "Run every documentation check: the tool inventory, discovery, the tool pages, and every Markdown page.",
            today,
            summary,
            (files, policy, date, findings) =>
            {
                var inventory = RunInventory(files, policy, date, findings);
                findings.AddRange(PageAuditor.Evaluate(policy, files, inventory, date));
            });

        yield return Define(
            context,
            "inventory",
            "Check the tool inventory, compare it with the tools the repository uses, and check the tool pages and the dependency catalog.",
            today,
            summary,
            (files, policy, date, findings) => RunInventory(files, policy, date, findings));

        yield return Define(
            context,
            "pages",
            "Check every Markdown page for front matter, freshness, tested commands, resolvable includes, and accessible diagrams.",
            today,
            summary,
            (files, policy, date, findings) =>
            {
                // The inventory is read only to know the tool IDs. Its own findings belong to the inventory command.
                var inventory = ToolInventory.Load(files, policy, []);
                findings.AddRange(PageAuditor.Evaluate(policy, files, inventory, date));
            });
    }

    private static Command Define(
        CliContext context,
        string name,
        string description,
        Option<string?> today,
        Option<string?> summary,
        Action<RepositoryFiles, DocumentationPolicy, DateOnly, List<Finding>> run)
    {
        var command = new Command(name, description) { today, summary };

        command.SetAction(parseResult =>
        {
            var files = context.Files(parseResult);
            var policy = DocumentationPolicy.Load(files);
            List<Finding> findings = [];

            run(files, policy, ResolveToday(context, parseResult.GetValue(today)), findings);
            return context.Report($"documentation {name}", findings, parseResult.GetValue(summary));
        });

        return command;
    }

    private static ToolInventory? RunInventory(RepositoryFiles files, DocumentationPolicy policy, DateOnly date, List<Finding> findings)
    {
        var inventory = ToolInventory.Load(files, policy, findings);

        if (inventory is null)
        {
            return null;
        }

        findings.AddRange(InventoryRules.Evaluate(inventory, policy, files, date));
        findings.AddRange(ToolDiscovery.Evaluate(inventory, policy, files));
        findings.AddRange(ToolPageRules.Evaluate(inventory, policy, files));
        return inventory;
    }

    private static DateOnly ResolveToday(CliContext context, string? option)
    {
        if (option is null)
        {
            return DateOnly.FromDateTime(context.Time.GetUtcNow().UtcDateTime);
        }

        return DateOnly.TryParseExact(option, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new GovernanceException($"--today must be a date in the form YYYY-MM-DD, and was '{option}'.");
    }
}
