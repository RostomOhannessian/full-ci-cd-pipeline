using System.Text.Json.Nodes;
using Documentation.Auditor.Policy;
using Governance.Auditor.Common;
using Governance.Auditor.Documents;

namespace Documentation.Auditor.Inventory;

/// <summary>One tool, library, action, image, or service. The schema <c>inventory.schema.json</c> describes every field.</summary>
internal sealed record ToolEntry
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Category { get; init; }

    public required string Tier { get; init; }

    public required string Lifecycle { get; init; }

    public required string Usage { get; init; }

    public required string Introduced { get; init; }

    public required string Purpose { get; init; }

    public required string License { get; init; }

    public required string LicenseSource { get; init; }

    public required string Version { get; init; }

    public string? Repo { get; init; }

    public required IReadOnlyList<string> Sources { get; init; }

    public required IReadOnlyList<string> Adrs { get; init; }

    public string? Notes { get; init; }

    /// <summary>Rules that tie the tool to what the repository uses, each as <c>kind:pattern</c>. They are how discovery finds the entry.</summary>
    public IReadOnlyList<string> Detect { get; init; } = [];

    public bool IsInUse => string.Equals(Usage, "in-use", StringComparison.Ordinal);

    public bool NeedsPage => IsInUse && Lifecycle is "adopt" or "trial" && Tier is "A" or "B";

    public bool NeedsCatalogEntry => IsInUse && Lifecycle is "adopt" or "trial" && string.Equals(Tier, "C", StringComparison.Ordinal);
}

internal sealed record ToolInventory
{
    public required int SchemaVersion { get; init; }

    public required string LastVerified { get; init; }

    public required IReadOnlyList<ToolEntry> Entries { get; init; }

    public ToolEntry? Find(string id) => Entries.FirstOrDefault(entry => string.Equals(entry.Id, id, StringComparison.Ordinal));

    /// <summary>Reads the inventory and checks it against its schema. Returns null, with the reasons in <paramref name="findings"/>, when it cannot be used.</summary>
    public static ToolInventory? Load(RepositoryFiles files, DocumentationPolicy policy, List<Finding> findings)
    {
        JsonNode? node;

        try
        {
            node = SchemaDocument.Load(files, policy.Inventory.Path, policy.Inventory.Schema, "inventory-schema", findings);
        }
        catch (GovernanceException exception)
        {
            findings.Add(Finding.Error("inventory-schema", exception.Message, policy.Inventory.Path));
            return null;
        }

        return node is null ? null : DocumentJson.Deserialize<ToolInventory>(node, policy.Inventory.Path);
    }
}
