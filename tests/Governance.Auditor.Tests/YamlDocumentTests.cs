using System.Text.Json.Nodes;
using Governance.Auditor.Common;
using Governance.Auditor.Documents;
using Xunit;

namespace Governance.Auditor.Tests;

public sealed class YamlDocumentTests
{
    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public void An_unquoted_scalar_takes_the_type_the_yaml_core_schema_gives_it()
    {
        var document = (JsonObject)YamlDocument.Parse(
            """
            text: hello
            integer: 42
            negative: -7
            float: 1.5
            truthy: true
            falsy: False
            nothing: null
            tilde: ~
            empty:
            date: 2026-10-08
            yes-word: yes
            """,
            "sample.yaml")!;

        Assert.Equal("hello", (string?)document["text"]);
        Assert.Equal(42L, (long?)document["integer"]);
        Assert.Equal(-7L, (long?)document["negative"]);
        Assert.Equal(1.5, (double?)document["float"]);
        Assert.True((bool?)document["truthy"]);
        Assert.False((bool?)document["falsy"]);
        Assert.Null(document["nothing"]);
        Assert.Null(document["tilde"]);
        Assert.Null(document["empty"]);
        Assert.Equal("2026-10-08", (string?)document["date"]);
        Assert.Equal("yes", (string?)document["yes-word"]);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public void A_quoted_scalar_is_always_a_string()
    {
        var document = (JsonObject)YamlDocument.Parse("version: \"1\"\nflag: 'true'\nnothing: \"null\"", "sample.yaml")!;

        Assert.Equal("1", (string?)document["version"]);
        Assert.Equal("true", (string?)document["flag"]);
        Assert.Equal("null", (string?)document["nothing"]);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public void Sequences_and_nested_mappings_keep_their_order_and_shape()
    {
        var document = (JsonObject)YamlDocument.Parse("outer:\n  inner: [1, two, { three: 3 }]\n", "sample.yaml")!;

        var inner = (JsonArray)document["outer"]!["inner"]!;
        Assert.Equal(3, inner.Count);
        Assert.Equal(1L, (long?)inner[0]);
        Assert.Equal("two", (string?)inner[1]);
        Assert.Equal(3L, (long?)inner[2]!["three"]);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public void A_key_that_appears_twice_is_rejected_because_the_second_value_would_silently_win()
    {
        var exception = Assert.Throws<GovernanceException>(() => YamlDocument.Parse("a: 1\na: 2\n", "sample.yaml"));

        Assert.Contains("sample.yaml", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public void An_anchor_and_its_alias_are_rejected_because_aliases_can_multiply_a_file_into_a_bomb()
    {
        var exception = Assert.Throws<GovernanceException>(() => YamlDocument.Parse("a: &x 1\nb: *x\n", "sample.yaml"));

        Assert.Contains("anchors and aliases are not supported", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public void Invalid_yaml_is_reported_with_the_file_name_and_line()
    {
        var exception = Assert.Throws<GovernanceException>(() => YamlDocument.Parse("a: \"never closed\nb: 3\n", "broken.yaml"));

        Assert.Contains("broken.yaml", exception.Message, StringComparison.Ordinal);
        Assert.Contains("line", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public void An_unbalanced_flow_collection_is_a_governance_error_and_not_a_crash()
    {
        Assert.Throws<GovernanceException>(() => YamlDocument.Parse("a: [1, 2\nb: 3\n", "broken.yaml"));
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public void A_file_with_two_documents_is_rejected_and_an_empty_file_gives_nothing()
    {
        Assert.Throws<GovernanceException>(() => YamlDocument.Parse("a: 1\n---\nb: 2\n", "sample.yaml"));
        Assert.Null(YamlDocument.Parse(string.Empty, "empty.yaml"));
    }
}
