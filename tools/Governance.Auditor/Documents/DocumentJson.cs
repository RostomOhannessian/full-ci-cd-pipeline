using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Governance.Auditor.Common;

namespace Governance.Auditor.Documents;

/// <summary>The JSON settings shared by every document the tool reads. Property names are kebab-case, as in the YAML files.</summary>
internal static class DocumentJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public static T Deserialize<T>(JsonNode node, string sourceName)
    {
        try
        {
            return node.Deserialize<T>(Options) ?? throw new GovernanceException($"{sourceName}: the document is empty.");
        }
        catch (JsonException exception)
        {
            throw new GovernanceException($"{sourceName}: the document does not have the expected shape. {exception.Message}", exception);
        }
    }
}

/// <summary>Loads a YAML document, checks it against its JSON Schema, and reports every violation as a finding.</summary>
internal static class SchemaDocument
{
    /// <summary>Returns the document tree, or null when the file is not valid YAML or does not match the schema. The reasons are added to <paramref name="findings"/>.</summary>
    public static JsonNode? Load(RepositoryFiles files, string documentPath, string schemaPath, string rule, List<Finding> findings)
    {
        JsonNode? document;

        try
        {
            document = YamlDocument.Parse(files.ReadAllText(documentPath), documentPath);
        }
        catch (GovernanceException exception)
        {
            findings.Add(Finding.Error(rule, exception.Message, documentPath));
            return null;
        }

        var schema = JsonNode.Parse(files.ReadAllText(schemaPath)) ?? throw new GovernanceException($"{schemaPath}: the schema is empty.");
        var errors = JsonSchemaValidator.Create(schema, schemaPath).Validate(document);

        findings.AddRange(errors.Select(error => Finding.Error(rule, error, documentPath)));
        return errors.Count == 0 ? document : null;
    }
}
