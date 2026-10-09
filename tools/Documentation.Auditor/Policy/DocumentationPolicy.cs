using Governance.Auditor.Common;
using Governance.Auditor.Documents;

namespace Documentation.Auditor.Policy;

/// <summary>
/// The typed view of <c>governance/policies/documentation-policy.yaml</c>. The rules read their settings from here, never from code, so a
/// change to a required section, an allow-list, or an exception is a reviewed change to that file (CODEOWNERS covers governance/).
/// </summary>
internal sealed record DocumentationPolicy
{
    public const string Path = "governance/policies/documentation-policy.yaml";

    public required int SchemaVersion { get; init; }

    public string? Description { get; init; }

    public required InventoryPolicy Inventory { get; init; }

    public required FreshnessPolicy Freshness { get; init; }

    public required ToolPagePolicy ToolPages { get; init; }

    public required DiscoveryPolicy Discovery { get; init; }

    public required PagePolicy Pages { get; init; }

    public static DocumentationPolicy Load(RepositoryFiles files)
    {
        var node = YamlDocument.Parse(files.ReadAllText(Path), Path)
            ?? throw new GovernanceException($"{Path}: the policy is empty.");
        var policy = DocumentJson.Deserialize<DocumentationPolicy>(node, Path);

        if (policy.SchemaVersion != 1)
        {
            throw new GovernanceException($"{Path}: schema-version {policy.SchemaVersion} is not supported. This tool reads version 1.");
        }

        if (policy.Freshness.WarnAfterDays < 1)
        {
            throw new GovernanceException($"{Path}: freshness.warn-after-days must be at least 1.");
        }

        foreach (var tier in new[] { "A", "B" })
        {
            if (!policy.ToolPages.Sections.TryGetValue(tier, out var sections) || sections.Count == 0)
            {
                throw new GovernanceException($"{Path}: tool-pages.sections must list the required sections of tier {tier}.");
            }
        }

        foreach (var rule in policy.Pages.FrontMatter.Where(rule => rule.Paths.Count == 0 || rule.Required.Count == 0))
        {
            throw new GovernanceException($"{Path}: the front-matter rule '{rule.Id}' needs paths and required keys.");
        }

        foreach (var owed in policy.ToolPages.Owed.Where(owed => owed.Tools.Count == 0 || string.IsNullOrWhiteSpace(owed.Due) || string.IsNullOrWhiteSpace(owed.Reason)))
        {
            throw new GovernanceException($"{Path}: every owed-pages entry needs tools, a due work package, and a reason.");
        }

        if (string.IsNullOrWhiteSpace(policy.Pages.CommandBlocks.Marker))
        {
            throw new GovernanceException($"{Path}: pages.command-blocks.marker must not be empty.");
        }

        return policy;
    }
}

internal sealed record InventoryPolicy
{
    public required string Path { get; init; }

    public required string Schema { get; init; }

    public required string AdrDirectory { get; init; }

    /// <summary>License names that need no note. Anything else needs a <c>notes</c> entry that says why the tool is acceptable (ADR-0004).</summary>
    public required IReadOnlyList<string> PermissiveLicenses { get; init; }

    /// <summary>Values of the license field that name no license, such as a hosted service. They need no note.</summary>
    public required IReadOnlyList<string> NoLicenseTerms { get; init; }
}

internal sealed record FreshnessPolicy
{
    public required int WarnAfterDays { get; init; }
}

internal sealed record ToolPagePolicy
{
    public required string Directory { get; init; }

    /// <summary>The page that holds the Tier C entries, one level-2 heading for each library.</summary>
    public required string Catalog { get; init; }

    /// <summary>The level-2 headings that a page of each tier must have, with the tier as the key.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> Sections { get; init; }

    public IReadOnlyList<OwedPages> Owed { get; init; } = [];
}

/// <summary>Pages that are due later. Each group names the work package that owes them, and the page becomes an error once that work package is completed.</summary>
internal sealed record OwedPages
{
    public required string Due { get; init; }

    public required string Reason { get; init; }

    public required IReadOnlyList<string> Tools { get; init; }
}

internal sealed record DiscoveryPolicy
{
    /// <summary>Files that hold tools the auditor cannot read yet. Such a file fails the audit, so a tool in it cannot go undiscovered.</summary>
    public IReadOnlyList<UnsupportedSource> UnsupportedSources { get; init; } = [];

    /// <summary>Paths the scanners skip, as glob patterns, for example test fixtures that hold deliberately wrong files.</summary>
    public IReadOnlyList<string> ExcludePaths { get; init; } = [];
}

internal sealed record UnsupportedSource
{
    public required string Pattern { get; init; }

    public required string Owner { get; init; }

    public required string Description { get; init; }
}

internal sealed record PagePolicy
{
    /// <summary>Markdown files the page rules never read, as glob patterns.</summary>
    public IReadOnlyList<string> Exclude { get; init; } = [];

    /// <summary>The first rule whose paths match a page decides which front matter keys it needs. A page that matches no rule is not checked.</summary>
    public required IReadOnlyList<FrontMatterRule> FrontMatter { get; init; }

    public required CommandBlockPolicy CommandBlocks { get; init; }

    public required IncludePolicy Includes { get; init; }
}

internal sealed record FrontMatterRule
{
    public required string Id { get; init; }

    public required IReadOnlyList<string> Paths { get; init; }

    /// <summary>Pages the rule skips although its paths match, so the next rule can decide for them.</summary>
    public IReadOnlyList<string> Exclude { get; init; } = [];

    /// <summary>Keys that must have a value.</summary>
    public required IReadOnlyList<string> Required { get; init; }

    /// <summary>Keys that must be present and may be empty, such as the lists of ADRs that a decision replaces.</summary>
    public IReadOnlyList<string> Present { get; init; } = [];
}

internal sealed record CommandBlockPolicy
{
    /// <summary>The pages where a hand-written command block must be marked. Records such as ADRs and plans quote commands and are not listed.</summary>
    public required IReadOnlyList<string> Paths { get; init; }

    /// <summary>A fenced block in one of these languages is a command block.</summary>
    public required IReadOnlyList<string> Languages { get; init; }

    /// <summary>A block with one of these languages, or none, is a command block when its first line starts with one of the commands.</summary>
    public required IReadOnlyList<string> PlainLanguages { get; init; }

    public required IReadOnlyList<string> Commands { get; init; }

    /// <summary>The word that marks a block as illustrative, after the language: <c>```bash illustrative</c>.</summary>
    public required string Marker { get; init; }
}

internal sealed record IncludePolicy
{
    /// <summary>A code include may read only from these folders and files, which a build or a test runs, so the snippet cannot drift unnoticed.</summary>
    public required IReadOnlyList<string> AllowedRoots { get; init; }
}
