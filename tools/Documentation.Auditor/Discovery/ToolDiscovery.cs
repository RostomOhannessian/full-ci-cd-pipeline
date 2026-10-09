using Documentation.Auditor.Inventory;
using Documentation.Auditor.Policy;
using Governance.Auditor.Common;

namespace Documentation.Auditor.Discovery;

/// <summary>
/// Compares what the repository uses with what the inventory says (REQ-DOC-002 and REQ-DOC-003). A tool that is used and has no entry fails,
/// and so does an entry whose usage, version, or detection rules disagree with the files. An inventory entry ties itself to the files with
/// <c>detect</c> rules, each written as <c>kind:pattern</c>, where a trailing <c>*</c> in the pattern matches any ending.
/// </summary>
internal static class ToolDiscovery
{
    public static IReadOnlyList<Finding> Evaluate(ToolInventory inventory, DocumentationPolicy policy, RepositoryFiles files)
    {
        List<Finding> findings = [];
        var paths = files.ListFiles().Where(path => !Glob.IsMatchAny(policy.Discovery.ExcludePaths, path)).ToList();
        var discovered = Scanners.ScanAll(files, paths, findings).ToList();
        var rules = inventory.Entries
            .SelectMany(entry => entry.Detect.Select(detect => DetectRule.Parse(entry, detect)))
            .ToList();

        CheckUnsupportedSources(policy, paths, findings);

        var matched = new HashSet<string>(StringComparer.Ordinal);

        foreach (var occurrence in discovered)
        {
            var entries = rules.Where(rule => rule.Matches(occurrence)).Select(rule => rule.Entry).Distinct().ToList();

            if (entries.Count == 0)
            {
                findings.Add(Finding.Error(
                    "tool-undocumented",
                    $"The {occurrence.Kind} '{occurrence.Identifier}' is used here and no inventory entry detects it. Add an entry to {policy.Inventory.Path} with the detect rule '{occurrence.Kind}:{occurrence.Identifier}', and write its page if its tier needs one.",
                    occurrence.Path,
                    occurrence.Line));
                continue;
            }

            if (entries.Count > 1)
            {
                findings.Add(Finding.Error(
                    "tool-ambiguous",
                    $"The {occurrence.Kind} '{occurrence.Identifier}' matches the entries {string.Join(", ", entries.Select(entry => entry.Id))}. A tool has one entry, so narrow the detect rules.",
                    occurrence.Path,
                    occurrence.Line));
                continue;
            }

            matched.Add(entries[0].Id);
            CheckUsage(entries[0], occurrence, findings);
            CheckVersion(entries[0], occurrence, findings);
        }

        foreach (var rule in rules.Where(rule => string.Equals(rule.Kind, DiscoveryKinds.File, StringComparison.Ordinal)))
        {
            var path = paths.FirstOrDefault(candidate => Glob.IsMatch(rule.Pattern, candidate));

            if (path is not null)
            {
                matched.Add(rule.Entry.Id);
                CheckUsage(rule.Entry, new DiscoveredTool(DiscoveryKinds.File, rule.Pattern, null, path, null), findings);
            }
        }

        foreach (var entry in inventory.Entries.Where(entry => entry.IsInUse && entry.Detect.Count > 0 && !matched.Contains(entry.Id)))
        {
            findings.Add(Finding.Error(
                "tool-not-found",
                $"{entry.Id}: the inventory says the tool is in use and gives detect rules ({string.Join(", ", entry.Detect)}), and no file matches any of them. The tool was removed, or a rule is out of date.",
                policy.Inventory.Path));
        }

        return findings;
    }

    private static void CheckUsage(ToolEntry entry, DiscoveredTool occurrence, List<Finding> findings)
    {
        if (entry.IsInUse)
        {
            return;
        }

        var message = string.Equals(entry.Lifecycle, "hold", StringComparison.Ordinal)
            ? $"{entry.Id} is on hold, and the repository uses it ({occurrence.Kind} '{occurrence.Identifier}'). Remove the use, or change the decision in an ADR first."
            : $"{entry.Id} is used here, and the inventory says its usage is '{entry.Usage}'. A tool becomes in-use in the work package that first runs it, so set usage to 'in-use' and write its page.";

        findings.Add(Finding.Error("tool-usage-state", message, occurrence.Path, occurrence.Line));
    }

    private static void CheckVersion(ToolEntry entry, DiscoveredTool occurrence, List<Finding> findings)
    {
        var pinned = Scanners.NormalizeVersion(occurrence.Version);
        var recorded = Scanners.NormalizeVersion(entry.Version);

        if (pinned is not null && recorded is not null && !string.Equals(pinned, recorded, StringComparison.Ordinal))
        {
            findings.Add(Finding.Error(
                "tool-version-drift",
                $"{entry.Id}: this file pins version {pinned}, and the inventory records {recorded}. Update the inventory entry, with its sources, in the same change that updates the pin.",
                occurrence.Path,
                occurrence.Line));
        }
    }

    private static void CheckUnsupportedSources(DocumentationPolicy policy, IReadOnlyList<string> paths, List<Finding> findings)
    {
        foreach (var source in policy.Discovery.UnsupportedSources)
        {
            var path = paths.FirstOrDefault(candidate => Glob.IsMatch(source.Pattern, candidate));

            if (path is not null)
            {
                findings.Add(Finding.Error(
                    "discovery-unsupported-source",
                    $"This file holds tools the auditor cannot read yet ({source.Description}). The tools in it would go undiscovered, so add the scanner in tools/Documentation.Auditor, and remove '{source.Pattern}' from the unsupported sources in {DocumentationPolicy.Path}, in the change that adds the file. The work package that owns it is {source.Owner}.",
                    path));
            }
        }
    }

    private sealed record DetectRule(ToolEntry Entry, string Kind, string Pattern)
    {
        public static DetectRule Parse(ToolEntry entry, string detect)
        {
            var colon = detect.IndexOf(':', StringComparison.Ordinal);
            return new DetectRule(entry, detect[..colon], detect[(colon + 1)..]);
        }

        public bool Matches(DiscoveredTool occurrence)
        {
            if (!string.Equals(Kind, occurrence.Kind, StringComparison.Ordinal))
            {
                return false;
            }

            return Pattern.EndsWith('*')
                ? occurrence.Identifier.StartsWith(Pattern[..^1], StringComparison.OrdinalIgnoreCase)
                : string.Equals(Pattern, occurrence.Identifier, StringComparison.OrdinalIgnoreCase);
        }
    }
}
