using System.Text.RegularExpressions;
using AwesomeAssertions;
using Catalog.Domain.Common;
using Catalog.Domain.Products;
using Catalog.Domain.Tests.Support;
using CsCheck;
using Xunit;

namespace Catalog.Domain.Tests;

/// <summary>REQ-DOM-002: a description is optional, multi-line text of at most 2000 characters, and blank text means that there is none.</summary>
public sealed class DescriptionTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n \u00A0")]
    [Trait("Requirement", "REQ-DOM-002")]
    public void Missing_or_blank_text_means_no_description(string? raw)
    {
        var result = Description.Create(raw);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void The_text_is_trimmed_and_its_line_breaks_and_tabs_are_kept()
    {
        var description = Description.Create("\r\n  First line.\r\n\tSecond line.\n  \r\n").Value;

        description!.Value.Should().Be("First line.\r\n\tSecond line.");
        description.ToString().Should().Be("First line.\r\n\tSecond line.");
        Description.Create("same").Value.Should().Be(Description.Create(" same ").Value);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void A_description_of_the_longest_length_is_accepted_and_one_more_character_is_not()
    {
        Description.Create(new string('d', 2000)).IsSuccess.Should().BeTrue();
        Description.Create(new string('d', 2001)).Error.Should().Be(DomainError.Invalid("description", "The description must have at most 2000 characters."));
        Description.MaxLength.Should().Be(2000);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void Length_is_counted_in_unicode_characters()
    {
        var emoji = "\U0001F600";

        Description.Create(string.Concat(Enumerable.Repeat(emoji, 2000))).IsSuccess.Should().BeTrue();
        Description.Create(string.Concat(Enumerable.Repeat(emoji, 2001))).IsSuccess.Should().BeFalse();
    }

    [Theory]
    [InlineData("bell\u0007")]
    [InlineData("null\0byte")]
    [InlineData("delete\u007F")]
    [InlineData("form\ffeed")]
    [Trait("Requirement", "REQ-DOM-002")]
    public void Control_characters_other_than_line_breaks_and_tabs_are_refused(string raw)
    {
        Description.Create(raw).Error.Should().Be(
            DomainError.Invalid("description", "The description must not contain control characters other than line breaks and tabs."));
    }

    // The strings are built in the test and not passed as theory data, because the data of a theory goes through UTF-8 on its way to the runner, and a lone surrogate would arrive as U+FFFD.
    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void Text_that_is_not_valid_unicode_is_refused()
    {
        string[] invalid = ["a" + '\uD800' + "b", "a" + '\uDC00' + "b", "\uDC00", "\uD800"];

        foreach (var raw in invalid)
        {
            Description.Create(raw).Error.Should().Be(DomainError.Invalid("description", "The description must be valid Unicode text."), $"U+{(int)raw[^1]:X4}");
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void Normalizing_a_description_twice_changes_nothing()
    {
        Property.Check(
            Gen.String[0, 40],
            raw =>
            {
                var once = Description.Create(raw);
                if (once.IsSuccess && once.Value is not null)
                {
                    Description.Create(once.Value.Value).Value.Should().Be(once.Value);
                }
            });
    }
}

/// <summary>REQ-DOM-002: a SKU is three to 32 ASCII letters and digits with single hyphens between them, and it is kept in capital letters.</summary>
public sealed class SkuTests
{
    private static readonly Regex Pattern = new("^[A-Za-z0-9]+(-[A-Za-z0-9]+)*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly DomainError WrongLength = DomainError.Invalid("sku", "The SKU must have between 3 and 32 characters.");

    private static readonly DomainError WrongCharacters = DomainError.Invalid("sku", "The SKU must contain only letters, digits, and single hyphens between them.");

    [Theory]
    [InlineData("KB-100", "KB-100")]
    [InlineData("kb-100", "KB-100")]
    [InlineData("Kb-1a-Z", "KB-1A-Z")]
    [InlineData("abc", "ABC")]
    [InlineData("123", "123")]
    [InlineData("a-b", "A-B")]
    [Trait("Requirement", "REQ-DOM-002")]
    public void A_valid_SKU_is_kept_in_capital_letters(string raw, string expected)
    {
        var sku = Sku.Create(raw).Value;

        sku.Value.Should().Be(expected);
        sku.ToString().Should().Be(expected);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void The_longest_SKU_has_32_characters()
    {
        Sku.Create(new string('a', 32)).IsSuccess.Should().BeTrue();
        Sku.Create(new string('a', 33)).Error.Should().Be(WrongLength);
        Sku.Create("ab").Error.Should().Be(WrongLength);
        Sku.Create("a-b").IsSuccess.Should().BeTrue();
        (Sku.MinLength, Sku.MaxLength).Should().Be((3, 32));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a")]
    [Trait("Requirement", "REQ-DOM-002")]
    public void A_missing_or_too_short_SKU_is_refused(string? raw)
    {
        Sku.Create(raw).Error.Should().Be(WrongLength);
    }

    [Theory]
    [InlineData(" KB-100")]
    [InlineData("KB-100 ")]
    [InlineData("KB 100")]
    [InlineData("-KB100")]
    [InlineData("KB100-")]
    [InlineData("KB--100")]
    [InlineData("KB_100")]
    [InlineData("KB.100")]
    [InlineData("K\u00C9-100")]
    [InlineData("KB-\uFF11\uFF10\uFF10")]
    [InlineData("KB-100\n")]
    [Trait("Requirement", "REQ-DOM-002")]
    public void Anything_but_letters_digits_and_single_hyphens_is_refused_and_nothing_is_trimmed(string raw)
    {
        Sku.Create(raw).Error.Should().Be(WrongCharacters);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void Two_SKUs_that_differ_only_in_case_are_equal()
    {
        Sku.Create("kb-100").Value.Should().Be(Sku.Create("KB-100").Value);
        Sku.Create("kb-100").Value.GetHashCode().Should().Be(Sku.Create("KB-100").Value.GetHashCode());
        Sku.Create("kb-101").Value.Should().NotBe(Sku.Create("KB-100").Value);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void A_text_is_accepted_exactly_when_it_matches_the_pattern_of_the_contract_and_the_length_bounds()
    {
        string[] alphabet = ["a", "Z", "7", "-", "-", "_", " ", "\u00E9"];
        var text = Gen.Int[0, alphabet.Length - 1].Select(index => alphabet[index]).List[0, 40].Select(parts => string.Concat(parts));

        Property.Check(
            text,
            raw =>
            {
                var expected = raw.Length is >= 3 and <= 32 && Pattern.IsMatch(raw);

                var result = Sku.Create(raw);

                result.IsSuccess.Should().Be(expected, $"'{raw}'");
                if (result.IsSuccess)
                {
                    result.Value.Value.Should().Be(raw.ToUpperInvariant());
                    result.Value.Should().Be(Sku.Create(raw.ToLowerInvariant()).Value, "the case does not matter");
                    Sku.Create(result.Value.Value).Value.Should().Be(result.Value, "normalizing twice changes nothing");
                }
                else
                {
                    result.Error.Field.Should().Be("sku");
                }
            });
    }
}
