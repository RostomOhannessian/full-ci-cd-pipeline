using System.Text.Json.Serialization;
using Governance.Auditor.Common;
using Governance.Auditor.Documents;
using Governance.Auditor.Status;

namespace Governance.Auditor.Usage;

/// <summary>The names that the ledger, its schema, and the report share. A name that is not listed here is not a valid value.</summary>
internal static class UsageVocabulary
{
    public const string Delivery = "delivery";
    public const string Fix = "fix";
    public const string Planning = "planning";
    public const string Coordination = "coordination";
    public const string Exact = "exact";
    public const string Windowed = "windowed";
    public const string Main = "main";
    public const string SubAgent = "sub-agent";
    public const string Store = "store";
    public const string Derived = "derived";
    public const string Unknown = "unknown";

    /// <summary>The kinds of work that a slice of a session can be. Only <see cref="Delivery"/> belongs to one work package and feeds the estimate.</summary>
    public static IReadOnlyList<string> Activities { get; } = [Delivery, Fix, Planning, Coordination];

    public static IReadOnlyList<string> Confidences { get; } = [Exact, Windowed];

    public static IReadOnlyList<string> Agents { get; } = [Main, SubAgent];

    /// <summary>Where the numbers of a row come from: the session store, which reports them, or the token counts of the cloud store, from which they are computed.</summary>
    public static IReadOnlyList<string> Sources { get; } = [Store, Derived];

    /// <summary>The reasoning efforts. <see cref="Unknown"/> is only for a derived row, because the cloud store does not report the effort.</summary>
    public static IReadOnlyList<string> Efforts { get; } = ["none", "low", "medium", "high", "xhigh", "max", "unspecified", Unknown];

    public static IReadOnlyList<string> Sizes { get; } = ["S", "M", "L"];

    /// <summary>The weight of a size class in the estimate: the midpoint of the focused effort in plan section 5.7, in days. A size that is not S, M, or L has no weight.</summary>
    public static decimal Weight(string size) => size switch
    {
        "S" => 0.5m,
        "M" => 3m,
        "L" => 6.5m,
        _ => throw new GovernanceException($"'{size}' is not a size class. Use S, M, or L."),
    };
}

/// <summary>The window of time that a slice covers. Both ends are the time of a call: the first and the last of the slice.</summary>
internal sealed record UsageWindow
{
    public required DateTimeOffset From { get; init; }

    public required DateTimeOffset To { get; init; }
}

/// <summary>The AIU for each million tokens of each kind that the store charged for a model, which a derived row used to compute its AIU.</summary>
internal sealed record UsagePrices
{
    public required decimal FreshInput { get; init; }

    public required decimal CacheRead { get; init; }

    public required decimal CacheWrite { get; init; }

    public required decimal Output { get; init; }
}

/// <summary>What one model at one reasoning effort did, as the main agent or as sub-agents, inside one slice.</summary>
internal sealed record UsageRow
{
    public required string Model { get; init; }

    /// <summary>Where the numbers come from. A row without a source is read from the store, so every record written before derived rows stays as it was.</summary>
    public string Source { get; init; } = UsageVocabulary.Store;

    public required string ReasoningEffort { get; init; }

    public required string Agent { get; init; }

    public decimal? RequestMultiplier { get; init; }

    public required int Calls { get; init; }

    public required decimal Aiu { get; init; }

    /// <summary>The prices that a derived row used. A row that is read from the store has none, because the store reports the AIU of every call.</summary>
    public UsagePrices? Prices { get; init; }

    public required long FreshInputTokens { get; init; }

    public required long CacheReadTokens { get; init; }

    public required long CacheWriteTokens { get; init; }

    public required long OutputTokens { get; init; }

    /// <summary>The reasoning tokens, or null when they are unknown, which only a derived row may say.</summary>
    public long? ReasoningTokens { get; init; }

    public required long PeakContextTokens { get; init; }

    public required long ModelSeconds { get; init; }

    /// <summary>Every token that the calls sent in, which is what the store calls the input tokens and which includes the cache reads and writes.</summary>
    [JsonIgnore]
    public long InputTokens => FreshInputTokens + CacheReadTokens + CacheWriteTokens;

    [JsonIgnore]
    public bool IsDerived => string.Equals(Source, UsageVocabulary.Derived, StringComparison.Ordinal);

    /// <summary>The AIU that the tokens and the prices of a derived row give, or null when the row names no prices.</summary>
    [JsonIgnore]
    public decimal? ComputedAiu => Prices is { } prices
        ? ((FreshInputTokens * prices.FreshInput) + (CacheReadTokens * prices.CacheRead) + (CacheWriteTokens * prices.CacheWrite) + (OutputTokens * prices.Output)) / 1_000_000m
        : null;
}

/// <summary>One slice of a session: a window that belongs to one phase or work package and one kind of activity.</summary>
internal sealed record UsageRecord
{
    public required string Session { get; init; }

    public required PhaseId Phase { get; init; }

    public string? WorkPackage { get; init; }

    public required string Activity { get; init; }

    public required string Confidence { get; init; }

    public required UsageWindow Window { get; init; }

    public required int UserTurns { get; init; }

    public required int Compactions { get; init; }

    public string? Note { get; init; }

    public required IReadOnlyList<UsageRow> Usage { get; init; }

    [JsonIgnore]
    public decimal Aiu => Usage.Sum(row => row.Aiu);

    /// <summary>The part of the AIU that was computed from token counts and not read from the store.</summary>
    [JsonIgnore]
    public decimal DerivedAiu => Usage.Where(row => row.IsDerived).Sum(row => row.Aiu);

    [JsonIgnore]
    public int Calls => Usage.Sum(row => row.Calls);

    [JsonIgnore]
    public long PeakContextTokens => Usage.Max(row => row.PeakContextTokens);

    /// <summary>The key that says two records are the same slice. A second record with the same key is a duplicate.</summary>
    [JsonIgnore]
    public string Key => $"{Session}|{Phase}|{WorkPackage ?? "-"}|{Activity}|{Window.From:O}";
}

internal sealed record UsageWaiver
{
    public PhaseId? Phase { get; init; }

    public string? WorkPackage { get; init; }

    public required string Reason { get; init; }
}

internal sealed record UsageChange
{
    public PhaseId? Phase { get; init; }

    public string? WorkPackage { get; init; }

    public required IReadOnlyList<int> PullRequests { get; init; }

    public required int Files { get; init; }

    public required int Additions { get; init; }

    public required int Deletions { get; init; }

    [JsonIgnore]
    public int ChangedLines => Additions + Deletions;
}

/// <summary>The typed view of <c>docs/project/ai-usage.yaml</c>. The JSON Schema checks the shape first, so these records can trust it.</summary>
internal sealed record UsageLedger
{
    public required int SchemaVersion { get; init; }

    public required string Updated { get; init; }

    public IReadOnlyList<UsageWaiver> Waivers { get; init; } = [];

    public IReadOnlyList<UsageChange> Changes { get; init; } = [];

    public required IReadOnlyList<UsageRecord> Records { get; init; }

    public UsageChange? ChangeOf(string workPackage) =>
        Changes.FirstOrDefault(change => string.Equals(change.WorkPackage, workPackage, StringComparison.Ordinal));

    /// <summary>The slices of one work package that built it. Fixes, planning, and coordination are not part of what a package cost to deliver.</summary>
    public IEnumerable<UsageRecord> DeliveryOf(string workPackage) =>
        Records.Where(record => record.Activity == UsageVocabulary.Delivery && string.Equals(record.WorkPackage, workPackage, StringComparison.Ordinal));
}

/// <summary>Where the ledger files live, and how to load them.</summary>
internal static class UsageFile
{
    public const string DocumentPath = "docs/project/ai-usage.yaml";
    public const string SchemaPath = "docs/project/ai-usage.schema.json";
    public const string PagePath = "docs/project/AI-USAGE.md";
    public const string SchemaRule = "usage-schema";

    /// <summary>Loads the ledger, or returns null with the reasons in <paramref name="findings"/> when it does not match its schema.</summary>
    public static UsageLedger? Load(RepositoryFiles files, List<Finding> findings)
    {
        var node = SchemaDocument.Load(files, DocumentPath, SchemaPath, SchemaRule, findings);
        return node is null ? null : DocumentJson.Deserialize<UsageLedger>(node, DocumentPath);
    }
}
