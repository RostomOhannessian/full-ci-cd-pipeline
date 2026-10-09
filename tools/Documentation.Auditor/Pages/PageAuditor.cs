using System.Globalization;
using System.Text.Json.Nodes;
using Documentation.Auditor.Inventory;
using Documentation.Auditor.Markdown;
using Documentation.Auditor.Policy;
using Governance.Auditor.Common;

namespace Documentation.Auditor.Pages;

/// <summary>
/// Reads every Markdown page the policy does not exclude and applies the page rules (REQ-DOC-005): front matter, freshness, tested commands,
/// resolvable includes, and accessible diagrams. The rules that need only one page live here and in <see cref="BlockRules"/> and
/// <see cref="IncludeRules"/>. The tool pages themselves are checked by <see cref="ToolPageRules"/>.
/// </summary>
internal static class PageAuditor
{
    public static IReadOnlyList<Finding> Evaluate(DocumentationPolicy policy, RepositoryFiles files, ToolInventory? inventory, DateOnly today)
    {
        List<Finding> findings = [];
        var known = inventory?.Entries.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var path in files.ListFiles().Where(path => path.EndsWith(".md", StringComparison.Ordinal) && !Glob.IsMatchAny(policy.Pages.Exclude, path)))
        {
            var page = MarkdownPage.Parse(path, files.ReadAllText(path));

            CheckFrontMatter(page, policy, known, today, findings);
            BlockRules.CheckCommandBlocks(page, policy.Pages.CommandBlocks, findings);
            BlockRules.CheckDiagrams(page, findings);
            IncludeRules.Check(page, policy.Pages.Includes, files, findings);
        }

        return findings;
    }

    private static void CheckFrontMatter(MarkdownPage page, DocumentationPolicy policy, HashSet<string>? knownTools, DateOnly today, List<Finding> findings)
    {
        var rule = policy.Pages.FrontMatter.FirstOrDefault(candidate => Glob.IsMatchAny(candidate.Paths, page.Path) && !Glob.IsMatchAny(candidate.Exclude, page.Path));

        if (rule is null)
        {
            return;
        }

        if (page.FrontMatterError is not null)
        {
            findings.Add(Finding.Error("front-matter-invalid", page.FrontMatterError, page.Path, 1));
            return;
        }

        if (!page.HasFrontMatter || page.FrontMatter is null)
        {
            findings.Add(Finding.Error("front-matter-missing", $"The page has no front matter. A page of kind '{rule.Id}' needs: {string.Join(", ", rule.Required)}.", page.Path, 1));
            return;
        }

        foreach (var key in rule.Required.Where(key => IsEmpty(page.FrontMatter[key])))
        {
            findings.Add(Finding.Error("front-matter-key", $"The front matter lacks '{key}', which a page of kind '{rule.Id}' needs.", page.Path, 1));
        }

        foreach (var key in rule.Present.Where(key => !page.FrontMatter.ContainsKey(key)))
        {
            findings.Add(Finding.Error("front-matter-key", $"The front matter lacks '{key}', which a page of kind '{rule.Id}' must declare, even when it is empty.", page.Path, 1));
        }

        CheckFreshness(page, policy, today, findings);

        if (knownTools is not null && page.FrontMatter["tools"] is JsonArray tools)
        {
            foreach (var tool in tools.Select(StringValue).Where(tool => tool is not null && !knownTools.Contains(tool)))
            {
                findings.Add(Finding.Error("front-matter-tools", $"The 'tools' list names '{tool}', which is not an inventory ID. Add the tool to the inventory, or correct the ID.", page.Path, 1));
            }
        }
    }

    private static void CheckFreshness(MarkdownPage page, DocumentationPolicy policy, DateOnly today, List<Finding> findings)
    {
        if (StringValue(page.FrontMatter?["last-verified"]) is not { } text)
        {
            return;
        }

        if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var verified))
        {
            findings.Add(Finding.Error("front-matter-date", $"The last-verified value '{text}' is not a calendar date in the form YYYY-MM-DD.", page.Path, 1));
            return;
        }

        var age = today.DayNumber - verified.DayNumber;

        if (age < 0)
        {
            findings.Add(Finding.Warning("front-matter-date", $"The last-verified date {text} is in the future.", page.Path, 1));
        }
        else if (age > policy.Freshness.WarnAfterDays)
        {
            findings.Add(Finding.Warning("page-stale", $"The page was last verified {age} days ago, which is more than {policy.Freshness.WarnAfterDays}. Re-check it, then update last-verified.", page.Path, 1));
        }
    }

    private static bool IsEmpty(JsonNode? node) => node switch
    {
        null => true,
        JsonArray array => array.Count == 0,
        JsonObject mapping => mapping.Count == 0,
        _ => string.IsNullOrWhiteSpace(StringValue(node) ?? node.ToString()),
    };

    private static string? StringValue(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
