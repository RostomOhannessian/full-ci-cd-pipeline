using System.Text.Json.Nodes;
using Documentation.Auditor.Markdown;
using Documentation.Auditor.Policy;
using Governance.Auditor.Common;
using Governance.Auditor.Documents;

namespace Documentation.Auditor.Inventory;

/// <summary>
/// The rules for tool pages (REQ-DOC-003 and REQ-DOC-004). A tool that is in use and has a Tier A or B needs a page named after its inventory ID,
/// with the sections its tier requires. A Tier C library needs an entry in the dependency catalog. A page that a later work package owes is
/// listed in the policy with that work package, and it becomes an error when that work package is completed.
/// </summary>
internal static class ToolPageRules
{
    public static IReadOnlyList<Finding> Evaluate(ToolInventory inventory, DocumentationPolicy policy, RepositoryFiles files)
    {
        List<Finding> findings = [];
        var directory = policy.ToolPages.Directory;
        var pages = LoadPages(files, policy);
        var owed = policy.ToolPages.Owed.SelectMany(group => group.Tools.Select(tool => (Tool: tool, Group: group))).ToList();
        var states = owed.Count > 0 ? WorkPackageStates.Load(files, findings) : [];

        foreach (var (id, page) in pages)
        {
            if (inventory.Find(id) is not { } entry)
            {
                findings.Add(Finding.Error("tool-page-unknown", $"The page is named '{id}', and the inventory has no entry with that ID. A tool page is named after its inventory ID.", page.Path));
                continue;
            }

            CheckPage(entry, page, policy, findings);
        }

        foreach (var entry in inventory.Entries.Where(entry => entry.NeedsPage && !pages.ContainsKey(entry.Id)))
        {
            ReportMissing(entry, $"{directory}/{entry.Id}.md", policy, owed, states, findings);
        }

        CheckCatalog(inventory, policy, files, owed, states, findings);

        foreach (var (tool, group) in owed)
        {
            var entry = inventory.Find(tool);

            if (entry is null)
            {
                findings.Add(Finding.Error("tool-page-owed-stale", $"The owed-pages list names '{tool}', which is not in the inventory. Remove it from {DocumentationPolicy.Path}.", DocumentationPolicy.Path));
            }
            else if (pages.ContainsKey(tool) || !(entry.NeedsPage || entry.NeedsCatalogEntry) || HasCatalogEntry(entry, policy, files))
            {
                findings.Add(Finding.Error("tool-page-owed-stale", $"{tool} is on the owed-pages list for {group.Due}, and the page exists or the tool needs none. Remove it from {DocumentationPolicy.Path}.", DocumentationPolicy.Path));
            }

            if (states.Count > 0 && !states.ContainsKey(group.Due))
            {
                findings.Add(Finding.Error("tool-page-owed-stale", $"The owed-pages list names the work package '{group.Due}', which is not in the status file.", DocumentationPolicy.Path));
            }
        }

        return findings;
    }

    private static Dictionary<string, MarkdownPage> LoadPages(RepositoryFiles files, DocumentationPolicy policy)
    {
        var directory = policy.ToolPages.Directory;
        Dictionary<string, MarkdownPage> pages = new(StringComparer.Ordinal);

        foreach (var path in files.ListFiles(directory).Where(path => path.EndsWith(".md", StringComparison.Ordinal) && string.Equals(System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/'), directory, StringComparison.Ordinal)))
        {
            if (string.Equals(path, policy.ToolPages.Catalog, StringComparison.Ordinal) || string.Equals(path, $"{directory}/index.md", StringComparison.Ordinal))
            {
                continue;
            }

            pages[System.IO.Path.GetFileNameWithoutExtension(path)] = MarkdownPage.Parse(path, files.ReadAllText(path));
        }

        return pages;
    }

    private static void CheckPage(ToolEntry entry, MarkdownPage page, DocumentationPolicy policy, List<Finding> findings)
    {
        var declaredTier = page.FrontMatter?["tier"]?.GetValue<string>();
        var tier = entry.Tier is "A" or "B" ? entry.Tier : declaredTier;

        if (entry.Tier is "A" or "B" && declaredTier is not null && !string.Equals(declaredTier, entry.Tier, StringComparison.Ordinal))
        {
            findings.Add(Finding.Error("tool-page-tier", $"{entry.Id}: the page says tier '{declaredTier}', and the inventory says '{entry.Tier}'. They must agree.", page.Path));
        }

        if (tier is not ("A" or "B"))
        {
            findings.Add(Finding.Error("tool-page-tier", $"{entry.Id}: a tool page is Tier A or B. A Tier C library belongs in the dependency catalog, and a page for one declares the tier 'A' or 'B' in its front matter.", page.Path));
            return;
        }

        foreach (var section in policy.ToolPages.Sections[tier])
        {
            var body = page.Section(section);

            if (body is null)
            {
                findings.Add(Finding.Error("tool-page-section", $"{entry.Id}: a Tier {tier} page needs the section '## {section}'.", page.Path));
            }
            else if (body.Count == 0)
            {
                findings.Add(Finding.Error("tool-page-section", $"{entry.Id}: the section '## {section}' is empty. Write it, or say that nothing exists yet and name the work package that adds it.", page.Path));
            }
        }

        if (page.FrontMatter is null)
        {
            return;
        }

        if (page.FrontMatter["tools"] is not JsonArray tools || !tools.Any(tool => string.Equals(tool?.GetValue<string>(), entry.Id, StringComparison.Ordinal)))
        {
            findings.Add(Finding.Error("tool-page-front-matter", $"{entry.Id}: the 'tools' list in the front matter must include '{entry.Id}'.", page.Path));
        }

        var verified = page.FrontMatter["verified-against"] is JsonObject against && against[entry.Id] is { } version ? version.ToString() : null;

        if (verified is null)
        {
            findings.Add(Finding.Error("tool-page-front-matter", $"{entry.Id}: 'verified-against' must name the version of '{entry.Id}' that the page was checked against.", page.Path));
        }
        else if (Discovery.Scanners.NormalizeVersion(verified) is { } pageVersion && Discovery.Scanners.NormalizeVersion(entry.Version) is { } inventoryVersion && !string.Equals(pageVersion, inventoryVersion, StringComparison.Ordinal))
        {
            findings.Add(Finding.Warning("tool-page-version", $"{entry.Id}: the page was checked against {pageVersion}, and the inventory records {inventoryVersion}. Re-check the page, then update verified-against.", page.Path));
        }
    }

    private static void CheckCatalog(
        ToolInventory inventory,
        DocumentationPolicy policy,
        RepositoryFiles files,
        IReadOnlyList<(string Tool, OwedPages Group)> owed,
        IReadOnlyDictionary<string, string> states,
        List<Finding> findings)
    {
        foreach (var entry in inventory.Entries.Where(entry => entry.NeedsCatalogEntry && !HasCatalogEntry(entry, policy, files)))
        {
            ReportMissing(entry, $"{policy.ToolPages.Catalog}, as a '## {entry.Name}' section", policy, owed, states, findings);
        }
    }

    private static bool HasCatalogEntry(ToolEntry entry, DocumentationPolicy policy, RepositoryFiles files)
    {
        if (!files.FileExists(policy.ToolPages.Catalog))
        {
            return false;
        }

        var catalog = MarkdownPage.Parse(policy.ToolPages.Catalog, files.ReadAllText(policy.ToolPages.Catalog));
        return catalog.Headings.Any(heading => heading.Level == 2 && string.Equals(heading.Text, entry.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static void ReportMissing(
        ToolEntry entry,
        string expected,
        DocumentationPolicy policy,
        IReadOnlyList<(string Tool, OwedPages Group)> owed,
        IReadOnlyDictionary<string, string> states,
        List<Finding> findings)
    {
        var due = owed.FirstOrDefault(item => string.Equals(item.Tool, entry.Id, StringComparison.Ordinal)).Group;

        if (due is null)
        {
            findings.Add(Finding.Error("tool-page-missing", $"{entry.Id} is in use and needs documentation at {expected}. Write it with the tool-doc-author skill, or ask the maintainer to list it as owed.", policy.Inventory.Path));
        }
        else if (states.TryGetValue(due.Due, out var state) && string.Equals(state, "completed", StringComparison.Ordinal))
        {
            findings.Add(Finding.Error("tool-page-missing", $"{entry.Id} needs documentation at {expected}. It was owed by {due.Due}, which is completed.", policy.Inventory.Path));
        }
        else
        {
            findings.Add(Finding.Note("tool-page-owed", $"{entry.Id} needs documentation at {expected}. It is owed by {due.Due}.", DocumentationPolicy.Path));
        }
    }
}

/// <summary>The state of each work package in <c>docs/project/status.yaml</c>, by ID. The owed-pages rule needs only this.</summary>
internal static class WorkPackageStates
{
    public const string Path = "docs/project/status.yaml";

    public static Dictionary<string, string> Load(RepositoryFiles files, List<Finding> findings)
    {
        Dictionary<string, string> states = new(StringComparer.Ordinal);

        try
        {
            var root = YamlDocument.Parse(files.ReadAllText(Path), Path) as JsonObject;

            foreach (var phase in (root?["phases"] as JsonArray ?? []).OfType<JsonObject>())
            {
                foreach (var workPackage in (phase["work-packages"] as JsonArray ?? []).OfType<JsonObject>())
                {
                    if (workPackage["id"]?.GetValue<string>() is { } id && workPackage["state"]?.GetValue<string>() is { } state)
                    {
                        states[id] = state;
                    }
                }
            }
        }
        catch (GovernanceException exception)
        {
            findings.Add(Finding.Error("status-unreadable", $"The owed-pages rule needs the work package states. {exception.Message}", Path));
        }

        return states;
    }
}
