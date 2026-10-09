using Governance.Auditor.Common;
using Governance.Auditor.Documents;

namespace Governance.Auditor.Trace;

/// <summary>The typed view of <c>docs/requirements/requirements.yaml</c>.</summary>
internal sealed record RequirementsRegister
{
    public required int SchemaVersion { get; init; }

    public required string LastVerified { get; init; }

    public required IReadOnlyDictionary<string, string> Areas { get; init; }

    public required IReadOnlyList<Requirement> Requirements { get; init; }
}

internal sealed record Requirement
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required string Statement { get; init; }

    public required string Source { get; init; }

    public IReadOnlyList<int> BriefRows { get; init; } = [];

    public required IReadOnlyList<string> DeliveredBy { get; init; }

    public required IReadOnlyList<string> Verification { get; init; }

    public string? Evidence { get; init; }

    public required string Status { get; init; }

    public string? Notes { get; init; }

    /// <summary>The area code in the ID, for example <c>GOV</c> in <c>REQ-GOV-002</c>.</summary>
    public string Area => Id.Split('-')[1];
}

/// <summary>The typed view of <c>docs/testing/evidence.yaml</c>: proof that is not an automated test.</summary>
internal sealed record EvidenceRegister
{
    public required int SchemaVersion { get; init; }

    public required string LastVerified { get; init; }

    public required IReadOnlyList<EvidenceEntry> Entries { get; init; }
}

internal sealed record EvidenceEntry
{
    public required IReadOnlyList<string> Requirements { get; init; }

    public required string Kind { get; init; }

    public required string Path { get; init; }

    public required string Summary { get; init; }

    public required string Recorded { get; init; }
}

internal static class TraceFiles
{
    public const string RequirementsPath = "docs/requirements/requirements.yaml";
    public const string RequirementsSchemaPath = "docs/requirements/requirements.schema.json";
    public const string EvidencePath = "docs/testing/evidence.yaml";
    public const string EvidenceSchemaPath = "docs/testing/evidence.schema.json";
    public const string ReportPath = "docs/testing/traceability.md";

    public const string SchemaRule = "trace-schema";

    public static RequirementsRegister? LoadRequirements(RepositoryFiles files, List<Finding> findings)
    {
        var node = SchemaDocument.Load(files, RequirementsPath, RequirementsSchemaPath, SchemaRule, findings);
        return node is null ? null : DocumentJson.Deserialize<RequirementsRegister>(node, RequirementsPath);
    }

    public static EvidenceRegister? LoadEvidence(RepositoryFiles files, List<Finding> findings)
    {
        var node = SchemaDocument.Load(files, EvidencePath, EvidenceSchemaPath, SchemaRule, findings);
        return node is null ? null : DocumentJson.Deserialize<EvidenceRegister>(node, EvidencePath);
    }
}
