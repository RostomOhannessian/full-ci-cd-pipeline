using System.Text.Json;
using Governance.Auditor.Common;
using Governance.Auditor.Documents;

namespace Governance.Auditor.BlastRadius;

/// <summary>The typed view of <c>governance/policies/blast-radius-map.yaml</c>: which paths belong to which layer and area, and what a change there requires.</summary>
internal sealed record BlastRadiusMap
{
    public const string Path = "governance/policies/blast-radius-map.yaml";

    /// <summary>Written in a rule's job list to mean every job.</summary>
    public const string AllJobs = "all";

    public required int SchemaVersion { get; init; }

    public string? Description { get; init; }

    /// <summary>Jobs that run for every change, because they are required checks.</summary>
    public required IReadOnlyList<string> Always { get; init; }

    public required IReadOnlyDictionary<string, JobDefinition> Jobs { get; init; }

    public required IReadOnlyDictionary<string, string> DocsCatalog { get; init; }

    public required IReadOnlyDictionary<string, string> ReviewsCatalog { get; init; }

    public required IReadOnlyList<BlastRadiusRule> Rules { get; init; }

    public required BlastRadiusFallback Fallback { get; init; }

    public static BlastRadiusMap Load(RepositoryFiles files, string path = Path)
    {
        var node = YamlDocument.Parse(files.ReadAllText(path), path) ?? throw new GovernanceException($"{path}: the map is empty.");
        var map = DocumentJson.Deserialize<BlastRadiusMap>(node, path);
        map.Validate(path);
        return map;
    }

    private void Validate(string path)
    {
        if (SchemaVersion != 1)
        {
            throw new GovernanceException($"{path}: schema-version {SchemaVersion} is not supported. This tool reads version 1.");
        }

        List<string> problems = [];

        foreach (var duplicate in Rules.GroupBy(rule => rule.Id, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            problems.Add($"The rule '{duplicate.Key}' appears {duplicate.Count()} times.");
        }

        foreach (var job in Always.Where(job => !Jobs.ContainsKey(job)))
        {
            problems.Add($"'always' names the unknown job '{job}'.");
        }

        foreach (var rule in Rules)
        {
            if (rule.Paths.Count == 0)
            {
                problems.Add($"The rule '{rule.Id}' lists no paths.");
            }

            Check(rule.Id, rule.Jobs, rule.Docs, rule.Reviews, problems);
        }

        Check("fallback", Fallback.Jobs, Fallback.Docs, Fallback.Reviews, problems);

        if (problems.Count > 0)
        {
            throw new GovernanceException($"{path}: {string.Join(" ", problems)}");
        }
    }

    private void Check(string owner, IReadOnlyList<string> jobs, IReadOnlyList<string> docs, IReadOnlyList<string> reviews, List<string> problems)
    {
        problems.AddRange(jobs.Where(job => job != AllJobs && !Jobs.ContainsKey(job)).Select(job => $"'{owner}' names the unknown job '{job}'."));
        problems.AddRange(docs.Where(doc => !DocsCatalog.ContainsKey(doc)).Select(doc => $"'{owner}' names the unknown documentation item '{doc}'."));
        problems.AddRange(reviews.Where(review => !ReviewsCatalog.ContainsKey(review)).Select(review => $"'{owner}' names the unknown review '{review}'."));
    }
}

internal sealed record JobDefinition
{
    public required string Workflow { get; init; }

    public required string Name { get; init; }
}

internal sealed record BlastRadiusRule
{
    public required string Id { get; init; }

    public string? Description { get; init; }

    public required IReadOnlyList<string> Paths { get; init; }

    public string? Layer { get; init; }

    public required string Area { get; init; }

    public IReadOnlyList<string> Jobs { get; init; } = [];

    public IReadOnlyList<string> Docs { get; init; } = [];

    public IReadOnlyList<string> Reviews { get; init; } = [];

    public IReadOnlyList<string> Flags { get; init; } = [];
}

/// <summary>What applies to a path that no rule matches. It should be strict, so a new kind of path cannot skip a check unnoticed.</summary>
internal sealed record BlastRadiusFallback
{
    public string? Layer { get; init; }

    public required string Area { get; init; }

    public IReadOnlyList<string> Jobs { get; init; } = [];

    public IReadOnlyList<string> Docs { get; init; } = [];

    public IReadOnlyList<string> Reviews { get; init; } = [];

    public IReadOnlyList<string> Flags { get; init; } = [];
}

internal sealed record NamedText(string Id, string Text);

/// <summary>The outcome of evaluating a change: what it touches, and what it therefore requires.</summary>
internal sealed record BlastRadiusResult(
    int ChangedPaths,
    IReadOnlyList<string> Layers,
    IReadOnlyList<string> Areas,
    IReadOnlyList<string> Jobs,
    IReadOnlyDictionary<string, bool> Run,
    IReadOnlyList<NamedText> Docs,
    IReadOnlyList<NamedText> Reviews,
    IReadOnlyList<string> Flags,
    IReadOnlyList<string> Unclassified)
{
    private static readonly JsonSerializerOptions CompactJson = new() { WriteIndented = false };

    public string ToJson() => JsonSerializer.Serialize(
        new
        {
            changedPaths = ChangedPaths,
            layers = Layers,
            areas = Areas,
            jobs = Jobs,
            run = Run,
            docs = Docs.Select(doc => doc.Id),
            reviews = Reviews.Select(review => review.Id),
            flags = Flags,
            unclassifiedCount = Unclassified.Count,
        },
        CompactJson);
}

internal static class BlastRadiusEvaluator
{
    public static BlastRadiusResult Evaluate(BlastRadiusMap map, IEnumerable<string> changedPaths)
    {
        var paths = changedPaths.Select(Normalize).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        HashSet<string> layers = new(StringComparer.Ordinal);
        HashSet<string> areas = new(StringComparer.Ordinal);
        HashSet<string> jobs = new(map.Always, StringComparer.Ordinal);
        HashSet<string> docs = new(StringComparer.Ordinal);
        HashSet<string> reviews = new(StringComparer.Ordinal);
        HashSet<string> flags = new(StringComparer.Ordinal);
        List<string> unclassified = [];

        foreach (var path in paths)
        {
            var matches = map.Rules.Where(rule => Glob.IsMatchAny(rule.Paths, path)).ToList();

            if (matches.Count == 0)
            {
                unclassified.Add(path);
                Add(map, map.Fallback.Layer, map.Fallback.Area, map.Fallback.Jobs, map.Fallback.Docs, map.Fallback.Reviews, map.Fallback.Flags, layers, areas, jobs, docs, reviews, flags);
                continue;
            }

            foreach (var rule in matches)
            {
                Add(map, rule.Layer, rule.Area, rule.Jobs, rule.Docs, rule.Reviews, rule.Flags, layers, areas, jobs, docs, reviews, flags);
            }
        }

        var orderedJobs = map.Jobs.Keys.Where(jobs.Contains).Order(StringComparer.Ordinal).ToList();
        var run = map.Jobs.Keys.Order(StringComparer.Ordinal).ToDictionary(job => job, jobs.Contains, StringComparer.Ordinal);

        return new BlastRadiusResult(
            paths.Count,
            [.. layers.Order(StringComparer.Ordinal)],
            [.. areas.Order(StringComparer.Ordinal)],
            orderedJobs,
            run,
            [.. docs.Order(StringComparer.Ordinal).Select(id => new NamedText(id, map.DocsCatalog[id]))],
            [.. reviews.Order(StringComparer.Ordinal).Select(id => new NamedText(id, map.ReviewsCatalog[id]))],
            [.. flags.Order(StringComparer.Ordinal)],
            unclassified);
    }

    /// <summary>Turns a path from Git or the command line into the form the map uses: relative, with forward slashes.</summary>
    public static string Normalize(string path)
    {
        var normalized = path.Trim().Replace('\\', '/');

        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        if (normalized.Length == 0 || normalized.StartsWith('/') || normalized.Split('/').Contains(".."))
        {
            throw new GovernanceException($"'{path}' is not a path inside the repository.");
        }

        return normalized;
    }

    private static void Add(
        BlastRadiusMap map,
        string? layer,
        string area,
        IReadOnlyList<string> ruleJobs,
        IReadOnlyList<string> ruleDocs,
        IReadOnlyList<string> ruleReviews,
        IReadOnlyList<string> ruleFlags,
        HashSet<string> layers,
        HashSet<string> areas,
        HashSet<string> jobs,
        HashSet<string> docs,
        HashSet<string> reviews,
        HashSet<string> flags)
    {
        if (layer is not null)
        {
            layers.Add(layer);
        }

        areas.Add(area);
        IEnumerable<string> requiredJobs = ruleJobs.Contains(BlastRadiusMap.AllJobs, StringComparer.Ordinal) ? map.Jobs.Keys : ruleJobs;
        jobs.UnionWith(requiredJobs);
        docs.UnionWith(ruleDocs);
        reviews.UnionWith(ruleReviews);
        flags.UnionWith(ruleFlags);
    }
}
