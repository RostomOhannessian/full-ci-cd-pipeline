using System.Globalization;
using System.Text.RegularExpressions;
using Documentation.Auditor.Policy;
using Governance.Auditor.Common;

namespace Documentation.Auditor.Inventory;

/// <summary>
/// The rules that need only the inventory file: unique IDs, a lifecycle that agrees with the usage, ADR links that resolve, the license
/// note that ADR-0004 asks for, and freshness. What the repository actually uses is checked against the inventory by discovery.
/// </summary>
internal static partial class InventoryRules
{
    // Which usage each lifecycle allows. A tool becomes in-use in the work package that first runs it (docs/reference/tools/index.md).
    private static readonly Dictionary<string, string[]> UsageByLifecycle = new(StringComparer.Ordinal)
    {
        ["adopt"] = ["planned", "in-use"],
        ["trial"] = ["planned", "in-use"],
        ["assess"] = ["planned"],
        ["hold"] = ["evaluated-only"],
        ["retired"] = ["removed"],
    };

    [GeneratedRegex(@"\s+(?:and|or|with)\s+|[()]", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LicenseSeparatorPattern();

    public static IReadOnlyList<Finding> Evaluate(ToolInventory inventory, DocumentationPolicy policy, RepositoryFiles files, DateOnly today)
    {
        List<Finding> findings = [];
        var path = policy.Inventory.Path;

        foreach (var duplicate in inventory.Entries.GroupBy(entry => entry.Id, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            findings.Add(Finding.Error("inventory-duplicate-id", $"The ID '{duplicate.Key}' appears {duplicate.Count()} times. Every tool has one entry.", path));
        }

        var adrFiles = files.ListFiles(policy.Inventory.AdrDirectory);

        foreach (var entry in inventory.Entries)
        {
            CheckConsistency(entry, path, findings);
            CheckAdrLinks(entry, policy.Inventory.AdrDirectory, adrFiles, path, findings);
            CheckLicenseNote(entry, policy, path, findings);
        }

        CheckFreshness(inventory, policy, path, today, findings);
        return findings;
    }

    private static void CheckConsistency(ToolEntry entry, string path, List<Finding> findings)
    {
        if (UsageByLifecycle.TryGetValue(entry.Lifecycle, out var usages) && !usages.Contains(entry.Usage, StringComparer.Ordinal))
        {
            findings.Add(Finding.Error(
                "inventory-consistency",
                $"{entry.Id}: the lifecycle '{entry.Lifecycle}' allows the usage {string.Join(" or ", usages.Select(usage => $"'{usage}'"))}, and the entry says '{entry.Usage}'.",
                path));
        }

        var held = string.Equals(entry.Lifecycle, "hold", StringComparison.Ordinal);
        var tierUnknown = string.Equals(entry.Tier, "n/a", StringComparison.Ordinal);
        var phaseUnknown = string.Equals(entry.Introduced, "n/a", StringComparison.Ordinal);

        if (held != tierUnknown || held != phaseUnknown)
        {
            findings.Add(Finding.Error(
                "inventory-consistency",
                $"{entry.Id}: a tool on hold has the tier 'n/a' and the introduced phase 'n/a', because it is never documented or introduced, and every other tool has both.",
                path));
        }
    }

    private static void CheckAdrLinks(ToolEntry entry, string adrDirectory, IReadOnlyList<string> adrFiles, string path, List<Finding> findings)
    {
        foreach (var adr in entry.Adrs)
        {
            var exists = adrFiles.Any(file => file.StartsWith($"{adrDirectory}/{adr}-", StringComparison.Ordinal) && file.EndsWith(".md", StringComparison.Ordinal));

            if (!exists)
            {
                findings.Add(Finding.Error("inventory-adr-link", $"{entry.Id}: ADR-{adr} does not exist under {adrDirectory}.", path));
            }
        }
    }

    /// <summary>ADR-0004: a tool whose license is not permissive carries a note that says why it is acceptable here.</summary>
    private static void CheckLicenseNote(ToolEntry entry, DocumentationPolicy policy, string path, List<Finding> findings)
    {
        var permissive = new HashSet<string>(policy.Inventory.PermissiveLicenses.Concat(policy.Inventory.NoLicenseTerms), StringComparer.OrdinalIgnoreCase);

        var restricted = LicenseSeparatorPattern()
            .Split(entry.License)
            .Select(term => term.Trim())
            .Where(term => term.Length > 0 && !permissive.Contains(term))
            .ToList();

        if (restricted.Count > 0 && string.IsNullOrWhiteSpace(entry.Notes))
        {
            findings.Add(Finding.Error(
                "inventory-license-note",
                $"{entry.Id}: the license '{entry.License}' is not on the permissive list, so the entry needs a 'notes' value that says why the tool is acceptable (ADR-0004).",
                path));
        }
    }

    private static void CheckFreshness(ToolInventory inventory, DocumentationPolicy policy, string path, DateOnly today, List<Finding> findings)
    {
        if (!DateOnly.TryParseExact(inventory.LastVerified, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var verified))
        {
            findings.Add(Finding.Error("inventory-stale", $"The last-verified date '{inventory.LastVerified}' is not a calendar date in the form YYYY-MM-DD.", path));
            return;
        }

        var age = today.DayNumber - verified.DayNumber;

        if (age > policy.Freshness.WarnAfterDays)
        {
            findings.Add(Finding.Warning("inventory-stale", $"The inventory was last verified {age} days ago, which is more than {policy.Freshness.WarnAfterDays}. Re-check the versions and licenses, then update last-verified.", path));
        }
    }
}
