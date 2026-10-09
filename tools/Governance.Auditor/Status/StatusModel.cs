using System.Text.Json;
using System.Text.Json.Serialization;

namespace Governance.Auditor.Status;

/// <summary>A phase ID is a number (0 to 4) or the word <c>launch</c>.</summary>
[JsonConverter(typeof(PhaseIdConverter))]
internal readonly record struct PhaseId(string Value)
{
    public bool IsLaunch => string.Equals(Value, "launch", StringComparison.Ordinal);

    /// <summary>The position of the phase in delivery order. The launch gate comes after phase 4.</summary>
    public int Order => IsLaunch ? int.MaxValue : int.Parse(Value, System.Globalization.CultureInfo.InvariantCulture);

    public override string ToString() => Value;
}

internal sealed class PhaseIdConverter : JsonConverter<PhaseId>
{
    public override PhaseId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
    {
        JsonTokenType.Number => new PhaseId(reader.GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture)),
        JsonTokenType.String => new PhaseId(reader.GetString()!),
        _ => throw new JsonException("A phase ID must be a number or a string."),
    };

    public override void Write(Utf8JsonWriter writer, PhaseId value, JsonSerializerOptions options)
    {
        if (long.TryParse(value.Value, out var number))
        {
            writer.WriteNumberValue(number);
        }
        else
        {
            writer.WriteStringValue(value.Value);
        }
    }
}

internal enum WorkState
{
    Planned,
    InProgress,
    Blocked,
    Completed,
}

/// <summary>The typed view of <c>docs/project/status.yaml</c>. The JSON Schema checks the shape first, so these records can trust it.</summary>
internal sealed record StatusDocument
{
    public required int SchemaVersion { get; init; }

    public required string Updated { get; init; }

    public required ProjectInfo Project { get; init; }

    public required CurrentInfo Current { get; init; }

    public required ResumeInfo Resume { get; init; }

    public required IReadOnlyList<PhaseInfo> Phases { get; init; }

    [JsonIgnore]
    public IEnumerable<WorkPackageInfo> WorkPackages => Phases.SelectMany(phase => phase.WorkPackages);

    public WorkPackageInfo? FindWorkPackage(string id) => WorkPackages.FirstOrDefault(workPackage => string.Equals(workPackage.Id, id, StringComparison.Ordinal));

    public PhaseInfo? FindPhase(PhaseId id) => Phases.FirstOrDefault(phase => phase.Id == id);
}

internal sealed record ProjectInfo
{
    public required string Name { get; init; }

    public required string Repository { get; init; }

    public required string Plan { get; init; }

    public required string PlanVersion { get; init; }
}

internal sealed record CurrentInfo
{
    public required PhaseId Phase { get; init; }

    public string? WorkPackage { get; init; }

    public required string Branch { get; init; }
}

internal sealed record ResumeInfo
{
    public required string LastCompleted { get; init; }

    public required string NextAction { get; init; }

    public required IReadOnlyList<OpenPullRequest> OpenPullRequests { get; init; }

    public required IReadOnlyList<string> EnvironmentNotes { get; init; }
}

internal sealed record OpenPullRequest
{
    public required int Number { get; init; }

    public required string Title { get; init; }

    public required string Branch { get; init; }
}

internal sealed record PhaseInfo
{
    public required PhaseId Id { get; init; }

    public required string Name { get; init; }

    public required WorkState State { get; init; }

    public string? Branch { get; init; }

    public required string Plan { get; init; }

    public string? Milestone { get; init; }

    public string? MilestoneUrl { get; init; }

    public string? Release { get; init; }

    public int? Issue { get; init; }

    public int? PullRequest { get; init; }

    public required IReadOnlyList<WorkPackageInfo> WorkPackages { get; init; }
}

internal sealed record WorkPackageInfo
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required string Size { get; init; }

    public required WorkState State { get; init; }

    public string? Branch { get; init; }

    public int? Issue { get; init; }

    public int? PullRequest { get; init; }

    public IReadOnlyList<string> DependsOn { get; init; } = [];

    public IReadOnlyList<string> Evidence { get; init; } = [];

    public IReadOnlyList<string> Blockers { get; init; } = [];
}
