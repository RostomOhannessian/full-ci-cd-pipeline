using AwesomeAssertions;
using Catalog.Domain.Categories;
using Catalog.Domain.Common;
using Catalog.Domain.Products;
using Catalog.Domain.Tests.Support;
using CsCheck;
using Xunit;

namespace Catalog.Domain.Tests;

/// <summary>
/// REQ-DOM-002: <see cref="ProductName"/> and <see cref="CategoryName"/> follow one rule with two limits, so each test runs against both.
/// Length is counted in Unicode characters, which is what <c>maxLength</c> counts in the contract.
/// </summary>
public sealed class NameTests
{
    private static readonly string[] Pieces = ["a", "B", " ", "\t", "\u00E9", "\u65E5", "\U0001F600", "\u0007", "\u00A0", "x"];

    private static IEnumerable<(string Kind, Func<string?, DomainResult<string>> Create, int Max)> Kinds =>
    [
        ("product name", raw => Convert(ProductName.Create(raw)), ProductName.MaxLength),
        ("category name", raw => Convert(CategoryName.Create(raw)), CategoryName.MaxLength),
    ];

    private static DomainResult<string> Convert(DomainResult<ProductName> result) =>
        result.IsSuccess ? DomainResult.Success(result.Value.Value) : DomainResult.Failure<string>(result.Error);

    private static DomainResult<string> Convert(DomainResult<CategoryName> result) =>
        result.IsSuccess ? DomainResult.Success(result.Value.Value) : DomainResult.Failure<string>(result.Error);

    private static DomainError TooLongOrEmpty(int max) => DomainError.Invalid("name", $"The name must have between 1 and {max} characters.");

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void The_limits_are_the_ones_of_the_contract()
    {
        ProductName.MaxLength.Should().Be(200);
        CategoryName.MaxLength.Should().Be(100);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void White_space_around_a_name_is_trimmed_and_white_space_inside_it_is_kept()
    {
        foreach (var (kind, create, _) in Kinds)
        {
            create(" \t Mechanical  keyboard \r\n\u00A0").Value.Should().Be("Mechanical  keyboard", kind);
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void A_name_keeps_its_case_and_its_accents()
    {
        foreach (var (kind, create, _) in Kinds)
        {
            create("Clavier \u00E9crit").Value.Should().Be("Clavier \u00E9crit", kind);
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void A_name_of_the_longest_length_is_accepted_and_one_more_character_is_not()
    {
        foreach (var (kind, create, max) in Kinds)
        {
            create(new string('a', max)).IsSuccess.Should().BeTrue(kind);
            create(new string('a', max + 1)).Error.Should().Be(TooLongOrEmpty(max), kind);
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void Length_is_counted_in_unicode_characters_and_not_in_utf16_units()
    {
        foreach (var (kind, create, max) in Kinds)
        {
            var emoji = "\U0001F600";

            string.Concat(Enumerable.Repeat(emoji, max)).Length.Should().Be(2 * max);
            create(string.Concat(Enumerable.Repeat(emoji, max))).IsSuccess.Should().BeTrue(kind);
            create(string.Concat(Enumerable.Repeat(emoji, max + 1))).Error.Should().Be(TooLongOrEmpty(max), kind);
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void A_combining_mark_is_a_character_of_its_own()
    {
        foreach (var (kind, create, max) in Kinds)
        {
            var decomposed = "e\u0301";

            create(string.Concat(Enumerable.Repeat(decomposed, max / 2))).IsSuccess.Should().BeTrue(kind);
            create(string.Concat(Enumerable.Repeat(decomposed, (max / 2) + 1))).IsSuccess.Should().BeFalse(kind);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    [InlineData("\u00A0")]
    [Trait("Requirement", "REQ-DOM-002")]
    public void A_missing_or_blank_name_is_refused(string? raw)
    {
        foreach (var (kind, create, max) in Kinds)
        {
            create(raw).Error.Should().Be(TooLongOrEmpty(max), kind);
        }
    }

    [Theory]
    [InlineData("two\nlines")]
    [InlineData("tab\there")]
    [InlineData("bell\u0007")]
    [InlineData("\u0007leading")]
    [InlineData("delete\u007F")]
    [InlineData("next\u0085line")]
    [Trait("Requirement", "REQ-DOM-002")]
    public void A_control_character_inside_a_name_is_refused(string raw)
    {
        foreach (var (kind, create, _) in Kinds)
        {
            create(raw).Error.Should().Be(DomainError.Invalid("name", "The name must not contain control characters."), kind);
        }
    }

    // The strings are built in the test and not passed as theory data, because the data of a theory goes through UTF-8 on its way to the runner, and a lone surrogate would arrive as U+FFFD.
    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void Text_that_is_not_valid_unicode_is_refused()
    {
        string[] invalid = ["a" + '\uD800' + "b", "a" + '\uDC00' + "b", "\uD800", "trailing" + '\uD83D'];

        foreach (var (kind, create, _) in Kinds)
        {
            foreach (var raw in invalid)
            {
                create(raw).Error.Should().Be(DomainError.Invalid("name", "The name must be valid Unicode text."), $"{kind} from U+{(int)raw[^1]:X4}");
            }
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void Two_names_that_differ_only_in_the_white_space_around_them_are_equal()
    {
        ProductName.Create(" Keyboard ").Value.Should().Be(ProductName.Create("Keyboard").Value);
        CategoryName.Create("\tKeyboards").Value.Should().Be(CategoryName.Create("Keyboards ").Value);
        ProductName.Create("Keyboard").Value.ToString().Should().Be("Keyboard");
        CategoryName.Create("Keyboards").Value.ToString().Should().Be("Keyboards");
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void A_name_is_accepted_exactly_when_it_has_one_line_of_valid_text_within_the_limit()
    {
        var text = Gen.Int[0, Pieces.Length - 1].Select(index => Pieces[index]).List[0, 260].Select(parts => string.Concat(parts));

        Property.Check(
            text,
            raw =>
            {
                foreach (var (kind, create, max) in Kinds)
                {
                    var trimmed = raw.Trim();
                    var expected = trimmed.Length > 0 && trimmed.EnumerateRunes().Count() <= max && !trimmed.Any(char.IsControl);

                    var result = create(raw);

                    result.IsSuccess.Should().Be(expected, $"{kind} from '{raw}'");
                    if (result.IsSuccess)
                    {
                        result.Value.Should().Be(trimmed);
                        create(result.Value).Value.Should().Be(result.Value, "normalizing twice changes nothing");
                    }
                    else
                    {
                        result.Error.Field.Should().Be("name");
                    }
                }
            });
    }
}
