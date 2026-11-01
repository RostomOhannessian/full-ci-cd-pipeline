using AwesomeAssertions;
using Catalog.Application.Idempotency;
using Catalog.Application.Persistence;
using Catalog.Domain.Tests.Support;
using CsCheck;
using Xunit;

namespace Catalog.Application.Tests;

/// <summary>REQ-API-003 and REQ-API-004: the entity tag and the idempotency key accept only the tokens that the contract allows, so the API can reject the rest at the edge.</summary>
public sealed class TagAndKeyTests
{
    [Fact]
    [Trait("Requirement", "REQ-API-003")]
    public void A_tag_made_from_a_row_version_is_a_url_safe_token_that_comes_back_as_the_same_tag()
    {
        var tag = EntityTag.FromBytes([0, 0, 0, 0, 0, 0, 7, 0xD1]);

        tag.Value.Should().Be("AAAAAAAAB9E");
        EntityTag.TryCreate(tag.Value, out var parsed).Should().BeTrue();
        parsed.Should().Be(tag);
        tag.ToString().Should().Be(tag.Value);
    }

    [Fact]
    [Trait("Requirement", "REQ-API-003")]
    public void Every_tag_that_is_made_from_bytes_is_accepted_when_it_comes_back()
    {
        Property.Check(
            Gen.Byte.Array[1, 16],
            bytes =>
            {
                var tag = EntityTag.FromBytes(bytes);

                EntityTag.TryCreate(tag.Value, out var parsed).Should().BeTrue();
                parsed.Should().Be(tag);
                tag.Value.Should().MatchRegex("^[A-Za-z0-9_-]+$");
            });
    }

    [Fact]
    [Trait("Requirement", "REQ-API-003")]
    public void A_row_version_needs_at_least_one_byte()
    {
        var empty = () => EntityTag.FromBytes([]);

        empty.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\"quoted\"")]
    [InlineData("W/abc")]
    [InlineData("*")]
    [InlineData("a b")]
    [InlineData("abc+/=")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [Trait("Requirement", "REQ-API-003")]
    public void Text_that_is_not_a_token_is_not_a_tag(string? raw)
    {
        EntityTag.TryCreate(raw, out var tag).Should().BeFalse();
        tag.Should().BeNull();
    }

    [Theory]
    [InlineData("a")]
    [InlineData("0b7c1f4e-5a1d-4f43-9d52-0a2b3c4d5e6f")]
    [InlineData("a key with spaces and symbols !#$%&'()*+,-./:;<=>?@[]^_`{|}~")]
    [Trait("Requirement", "REQ-API-004")]
    public void A_key_of_printable_ascii_is_accepted(string raw)
    {
        IdempotencyKey.TryCreate(raw, out var key).Should().BeTrue();
        key!.Value.Should().Be(raw);
        key.ToString().Should().Be(raw);
    }

    [Fact]
    [Trait("Requirement", "REQ-API-004")]
    public void A_key_of_128_characters_is_accepted_and_one_of_129_is_not()
    {
        IdempotencyKey.TryCreate(new string('k', IdempotencyKey.MaxLength), out _).Should().BeTrue();
        IdempotencyKey.TryCreate(new string('k', IdempotencyKey.MaxLength + 1), out var tooLong).Should().BeFalse();
        tooLong.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("tab\there")]
    [InlineData("line\nbreak")]
    [InlineData("caf\u00e9")]
    [InlineData("\u007f")]
    [Trait("Requirement", "REQ-API-004")]
    public void A_key_that_is_empty_or_holds_a_control_or_non_ascii_character_is_refused(string? raw)
    {
        IdempotencyKey.TryCreate(raw, out var key).Should().BeFalse();
        key.Should().BeNull();
    }
}
