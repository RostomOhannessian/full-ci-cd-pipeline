using System.Globalization;
using AwesomeAssertions;
using Catalog.Domain.Common;
using Catalog.Domain.Pricing;
using Catalog.Domain.Tests.Support;
using Xunit;

namespace Catalog.Domain.Tests;

/// <summary>REQ-DOM-002: the currencies of the version 1 contract, and the decimal places that each one allows.</summary>
public sealed class CurrencyTests
{
    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void The_accepted_currencies_are_the_four_of_the_contract_in_a_fixed_order()
    {
        Currency.All.Select(currency => currency.Code).Should().Equal("USD", "EUR", "GBP", "JPY");
    }

    [Theory]
    [InlineData("USD", 2)]
    [InlineData("EUR", 2)]
    [InlineData("GBP", 2)]
    [InlineData("JPY", 0)]
    [Trait("Requirement", "REQ-DOM-002")]
    public void Each_currency_has_the_decimal_places_of_ISO_4217(string code, int minorUnits)
    {
        var currency = Currency.Create(code).Value;

        currency.Code.Should().Be(code);
        currency.MinorUnits.Should().Be(minorUnits);
        currency.ToString().Should().Be(code);
        currency.Should().BeSameAs(Currency.All.Single(known => known.Code == code));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void The_named_currencies_are_the_ones_in_the_list()
    {
        Currency.All.Should().Equal(Currency.Usd, Currency.Eur, Currency.Gbp, Currency.Jpy);
    }

    [Theory]
    [InlineData("usd")]
    [InlineData("Usd")]
    [InlineData(" USD")]
    [InlineData("USD ")]
    [InlineData("US")]
    [InlineData("XXX")]
    [InlineData("")]
    [InlineData(null)]
    [Trait("Requirement", "REQ-DOM-002")]
    public void Any_other_spelling_or_currency_is_refused_and_the_error_lists_the_accepted_ones(string? code)
    {
        var result = Currency.Create(code);

        result.Error.Should().Be(DomainError.Invalid("currency", "The currency must be one of USD, EUR, GBP, JPY."));
    }
}

/// <summary>REQ-DOM-002: <see cref="Money"/> keeps the amount exact, never rounds one that does not fit, and has the bounds of the contract's <c>Price</c>.</summary>
public sealed class MoneyTests
{
    private static string Text(Money money) => money.Amount.ToString(CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("89.9", "89.90")]
    [InlineData("89.90", "89.90")]
    [InlineData("89.9000", "89.90")]
    [InlineData("5", "5.00")]
    [InlineData("0", "0.00")]
    [InlineData("0.01", "0.01")]
    [Trait("Requirement", "REQ-DOM-002")]
    public void A_two_decimal_currency_always_has_exactly_two_decimal_places(string amount, string expected)
    {
        var money = Money.Create(decimal.Parse(amount, CultureInfo.InvariantCulture), Currency.Usd).Value;

        Text(money).Should().Be(expected);
        money.ToString().Should().Be($"{expected} USD");
    }

    [Theory]
    [InlineData("100", "100")]
    [InlineData("100.0", "100")]
    [InlineData("0", "0")]
    [Trait("Requirement", "REQ-DOM-002")]
    public void Yen_has_no_decimal_places(string amount, string expected)
    {
        var money = Money.Create(decimal.Parse(amount, CultureInfo.InvariantCulture), Currency.Jpy).Value;

        Text(money).Should().Be(expected);
        money.ToString().Should().Be($"{expected} JPY");
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void Amounts_that_differ_only_in_trailing_zeros_are_equal_and_hash_alike()
    {
        var one = Make.Usd(89.9m);
        var other = Make.Usd(89.90m);

        one.Should().Be(other);
        one.GetHashCode().Should().Be(other.GetHashCode());
        one.Should().NotBe(Make.Usd(89.91m));
        one.Should().NotBe(Money.Create(89.9m, Currency.Eur).Value);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void A_negative_zero_becomes_an_ordinary_zero()
    {
        // A decimal keeps the sign of a zero in its bits and prints it as 0.00, so only the sign bit shows whether the amount is an ordinary zero.
        var negativeZero = new decimal(0, 0, 0, isNegative: true, 2);
        var negativeYen = new decimal(0, 0, 0, isNegative: true, 0);

        var money = Money.Create(negativeZero, Currency.Usd).Value;
        var yen = Money.Create(negativeYen, Currency.Jpy).Value;

        money.ToString().Should().Be("0.00 USD");
        money.IsZero.Should().BeTrue();
        decimal.IsNegative(money.Amount).Should().BeFalse();
        decimal.IsNegative(yen.Amount).Should().BeFalse();
        decimal.IsNegative(Money.Zero(Currency.Eur).Amount).Should().BeFalse();
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("-1")]
    [Trait("Requirement", "REQ-DOM-002")]
    public void A_negative_amount_is_refused(string amount)
    {
        var result = Money.Create(decimal.Parse(amount, CultureInfo.InvariantCulture), Currency.Usd);

        result.Error.Should().Be(DomainError.Invalid("amount", "The amount must not be negative."));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void The_largest_amount_has_twelve_integer_digits_and_one_more_is_refused()
    {
        Money.Create(999_999_999_999.99m, Currency.Usd).IsSuccess.Should().BeTrue();
        Money.Create(999_999_999_999m, Currency.Jpy).IsSuccess.Should().BeTrue();
        Money.Create(Money.Limit, Currency.Usd).Error.Should().Be(DomainError.Invalid("amount", "The amount must have at most 12 integer digits."));
        Money.Create(decimal.MaxValue, Currency.Usd).IsSuccess.Should().BeFalse();
        Money.Limit.Should().Be((decimal)Math.Pow(10, Money.MaxIntegerDigits));
    }

    [Theory]
    [InlineData("1.001")]
    [InlineData("89.995")]
    [InlineData("0.001")]
    [InlineData("0.0000000001")]
    [Trait("Requirement", "REQ-DOM-002")]
    public void An_amount_with_too_many_decimal_places_is_refused_and_not_rounded(string amount)
    {
        var result = Money.Create(decimal.Parse(amount, CultureInfo.InvariantCulture), Currency.Usd);

        result.Error.Should().Be(DomainError.Invalid("amount", "The amount must have at most 2 decimal places for USD."));
    }

    [Theory]
    [InlineData("0.5")]
    [InlineData("1.01")]
    [InlineData("100.4")]
    [Trait("Requirement", "REQ-DOM-002")]
    public void Yen_with_a_fraction_is_refused(string amount)
    {
        var result = Money.Create(decimal.Parse(amount, CultureInfo.InvariantCulture), Currency.Jpy);

        result.Error.Should().Be(DomainError.Invalid("amount", "The amount must be a whole number for JPY."));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void Creating_money_needs_a_currency()
    {
        var create = () => Money.Create(1m, null!);
        var zero = () => Money.Zero(null!);

        create.Should().Throw<ArgumentNullException>();
        zero.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void Zero_has_the_decimal_places_of_its_currency()
    {
        Text(Money.Zero(Currency.Usd)).Should().Be("0.00");
        Text(Money.Zero(Currency.Jpy)).Should().Be("0");
        Money.Zero(Currency.Eur).IsZero.Should().BeTrue();
        Make.Usd(0.01m).IsZero.Should().BeFalse();
        Money.Zero(Currency.Usd).Should().Be(Make.Usd(0m));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void Adding_decimals_is_exact()
    {
        var sum = Make.Usd(0.1m).Add(Make.Usd(0.2m)).Value;

        sum.Should().Be(Make.Usd(0.3m));
        Text(sum).Should().Be("0.30");
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void Adding_past_the_limit_is_refused()
    {
        var result = Make.Usd(999_999_999_999.99m).Add(Make.Usd(0.01m));

        result.Error.Should().Be(DomainError.Invalid("amount", "The amount must have at most 12 integer digits."));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void Money_of_two_currencies_cannot_be_added_or_subtracted()
    {
        var expected = DomainError.Invalid("currency", "The currencies must match.");

        Make.Usd(1m).Add(Money.Create(1m, Currency.Eur).Value).Error.Should().Be(expected);
        Make.Usd(1m).Subtract(Money.Create(1m, Currency.Eur).Value).Error.Should().Be(expected);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void Subtracting_the_same_amount_gives_an_ordinary_zero()
    {
        var difference = Make.Usd(12.34m).Subtract(Make.Usd(12.34m)).Value;

        difference.ToString().Should().Be("0.00 USD");
        difference.IsZero.Should().BeTrue();
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void Subtracting_more_than_the_amount_is_refused_and_one_cent_less_is_not()
    {
        Make.Usd(1.00m).Subtract(Make.Usd(1.01m)).Error.Should().Be(DomainError.Invalid("amount", "The result must not be negative."));
        Make.Usd(1.01m).Subtract(Make.Usd(1.00m)).Value.Should().Be(Make.Usd(0.01m));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void Arithmetic_needs_another_amount()
    {
        var add = () => Make.Usd(1m).Add(null!);
        var subtract = () => Make.Usd(1m).Subtract(null!);

        add.Should().Throw<ArgumentNullException>();
        subtract.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-002")]
    public void Yen_arithmetic_stays_whole()
    {
        Make.Jpy(100m).Add(Make.Jpy(250m)).Value.Should().Be(Make.Jpy(350m));
        Text(Make.Jpy(300m).Subtract(Make.Jpy(100m)).Value).Should().Be("200");
    }
}
