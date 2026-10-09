using System.Text.Json.Nodes;
using Governance.Auditor.Common;
using Governance.Auditor.Documents;
using Xunit;

namespace Governance.Auditor.Tests;

public sealed class JsonSchemaValidatorTests
{
    [Theory]
    [Trait("Requirement", "REQ-GOV-002")]
    [InlineData("""{ "type": "string" }""", "5", "expected string but found number")]
    [InlineData("""{ "type": ["string", "null"] }""", "5", "expected string or null but found number")]
    [InlineData("""{ "type": "integer" }""", "1.5", "expected integer but found number")]
    [InlineData("""{ "enum": ["a", "b"] }""", "\"c\"", "must be one of")]
    [InlineData("""{ "const": 1 }""", "2", "must be 1")]
    [InlineData("""{ "type": "string", "minLength": 3 }""", "\"ab\"", "at least 3 characters")]
    [InlineData("""{ "type": "string", "maxLength": 2 }""", "\"abc\"", "at most 2 characters")]
    [InlineData("""{ "type": "string", "pattern": "^a+$" }""", "\"abc\"", "does not match the pattern")]
    [InlineData("""{ "type": "integer", "minimum": 3 }""", "2", "at least 3")]
    [InlineData("""{ "type": "integer", "maximum": 3 }""", "4", "at most 3")]
    [InlineData("""{ "type": "array", "minItems": 2 }""", "[1]", "at least 2 items")]
    [InlineData("""{ "type": "array", "maxItems": 1 }""", "[1, 2]", "at most 1 items")]
    [InlineData("""{ "type": "array", "uniqueItems": true }""", "[1, 2, 1]", "items 0 and 2 are the same")]
    [InlineData("""{ "type": "array", "items": { "type": "integer" } }""", "[1, \"x\"]", "/1: expected integer")]
    [InlineData("""{ "type": "object", "required": ["a"] }""", "{}", "the required property 'a' is missing")]
    [InlineData("""{ "type": "object", "additionalProperties": false, "properties": { "a": {} } }""", "{ \"b\": 1 }", "the property 'b' is not allowed")]
    [InlineData("""{ "type": "object", "additionalProperties": { "type": "integer" } }""", "{ \"b\": \"x\" }", "/b: expected integer")]
    [InlineData("""{ "type": "object", "propertyNames": { "pattern": "^[a-z]+$" } }""", "{ \"B\": 1 }", "does not match the pattern")]
    [InlineData("""{ "type": "object", "minProperties": 2 }""", "{ \"a\": 1 }", "at least 2 properties")]
    [InlineData("""{ "oneOf": [{ "type": "integer" }, { "type": "number" }] }""", "1", "matches 2")]
    [InlineData("""{ "oneOf": [{ "type": "integer" }, { "type": "string" }] }""", "true", "matches 0")]
    [InlineData("""{ "anyOf": [{ "type": "integer" }, { "type": "string" }] }""", "true", "matches none")]
    [InlineData("""{ "allOf": [{ "type": "integer" }, { "minimum": 5 }] }""", "3", "at least 5")]
    [InlineData("""{ "not": { "type": "string" } }""", "\"x\"", "not allowed")]
    public void A_value_that_breaks_a_keyword_is_reported(string schema, string instance, string expectedMessage)
    {
        var errors = Validate(schema, instance);

        Assert.Contains(errors, error => error.Contains(expectedMessage, StringComparison.Ordinal));
    }

    [Theory]
    [Trait("Requirement", "REQ-GOV-002")]
    [InlineData("""{ "type": "string" }""", "\"x\"")]
    [InlineData("""{ "type": ["string", "null"] }""", "null")]
    [InlineData("""{ "type": "integer" }""", "2")]
    [InlineData("""{ "type": "integer" }""", "2.0")]
    [InlineData("""{ "type": "number" }""", "2.5")]
    [InlineData("""{ "type": "boolean" }""", "false")]
    [InlineData("""{ "enum": ["a", "b"] }""", "\"b\"")]
    [InlineData("""{ "type": "array", "uniqueItems": true, "items": { "type": "integer" } }""", "[1, 2, 3]")]
    [InlineData("""{ "type": "object", "required": ["a"], "additionalProperties": false, "properties": { "a": { "type": "string" } } }""", "{ \"a\": \"x\" }")]
    [InlineData("""{ "oneOf": [{ "type": "integer" }, { "const": "launch" }] }""", "\"launch\"")]
    [InlineData("""{ "$schema": "https://json-schema.org/draft/2020-12/schema", "title": "t", "description": "d", "format": "date" }""", "\"anything\"")]
    public void A_valid_value_gives_no_errors(string schema, string instance)
    {
        Assert.Empty(Validate(schema, instance));
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public void A_local_reference_is_followed_and_an_error_names_the_pointer_of_the_bad_value()
    {
        const string schema = """
            {
              "type": "object",
              "properties": { "items": { "type": "array", "items": { "$ref": "#/$defs/item" } } },
              "$defs": { "item": { "type": "object", "required": ["id"], "properties": { "id": { "type": "string" } } } }
            }
            """;

        var errors = Validate(schema, """{ "items": [{ "id": "a" }, { "id": 5 }] }""");

        Assert.Single(errors);
        Assert.StartsWith("/items/1/id:", errors[0], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public void A_schema_that_uses_a_keyword_the_validator_does_not_implement_is_rejected()
    {
        // A schema must never check less than it says, so an unknown keyword is an error and not a silent pass.
        var exception = Assert.Throws<GovernanceException>(() => JsonSchemaValidator.Create(JsonNode.Parse("""{ "type": "object", "patternProperties": { "^a": {} } }""")!, "sample.schema.json"));

        Assert.Contains("patternProperties", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public void An_unsupported_keyword_inside_a_nested_definition_is_found_too()
    {
        const string schema = """
            { "type": "object", "properties": { "a": { "type": "array", "items": { "if": {} } } }, "$defs": { "b": { "dependentRequired": {} } } }
            """;

        var exception = Assert.Throws<GovernanceException>(() => JsonSchemaValidator.Create(JsonNode.Parse(schema)!, "sample.schema.json"));

        Assert.Contains("'if'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("dependentRequired", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public void A_reference_that_leaves_the_document_or_does_not_resolve_fails_loudly()
    {
        var external = JsonSchemaValidator.Create(JsonNode.Parse("""{ "$ref": "https://example.test/other.json" }""")!, "sample.schema.json");
        var missing = JsonSchemaValidator.Create(JsonNode.Parse("""{ "$ref": "#/$defs/nowhere" }""")!, "sample.schema.json");

        Assert.Throws<GovernanceException>(() => external.Validate(JsonValue.Create(1)));
        Assert.Throws<GovernanceException>(() => missing.Validate(JsonValue.Create(1)));
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public void The_real_schemas_only_use_keywords_the_validator_implements()
    {
        foreach (var path in new[] { "docs/project/status.schema.json", "docs/requirements/requirements.schema.json", "docs/testing/evidence.schema.json" })
        {
            var schema = JsonNode.Parse(RealRepositoryText(path))!;

            _ = JsonSchemaValidator.Create(schema, path);
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public void A_schema_that_forbids_a_value_with_false_rejects_every_value()
    {
        var errors = Validate("""{ "properties": { "a": false } }""", """{ "a": 1 }""");

        Assert.Contains(errors, error => error.Contains("no value is allowed", StringComparison.Ordinal));
    }

    private static List<string> Validate(string schema, string instance) =>
        [.. JsonSchemaValidator.Create(JsonNode.Parse(schema)!, "sample.schema.json").Validate(JsonNode.Parse(instance))];

    private static string RealRepositoryText(string path) => File.ReadAllText(Path.Combine(Support.RealRepository.Root, path));
}
