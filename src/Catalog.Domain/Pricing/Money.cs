using System.Globalization;
using Catalog.Domain.Common;

namespace Catalog.Domain.Pricing;

/// <summary>
/// An amount of money in one currency. The amount is not negative, it has no more decimal places than the currency has, and it has at most
/// 12 integer digits (the bound of the version 1 contract). The decimal places are always exactly the currency's, so <c>89.9</c> and <c>89.90</c>
/// become one value and print the same way, and the amount never loses a digit to rounding because a value that needs rounding is refused.
/// </summary>
public sealed record Money
{
    /// <summary>The number of integer digits that an amount may have at most.</summary>
    public const int MaxIntegerDigits = 12;

    /// <summary>The first amount that is too large, which is 10 to the power of <see cref="MaxIntegerDigits"/>.</summary>
    public const decimal Limit = 1_000_000_000_000m;

    private Money(decimal amount, Currency currency)
    {
        Amount = amount;
        Currency = currency;
    }

    /// <summary>Gets the amount, with exactly as many decimal places as the currency has.</summary>
    public decimal Amount { get; }

    /// <summary>Gets the currency.</summary>
    public Currency Currency { get; }

    /// <summary>Gets a value that says whether the amount is zero.</summary>
    public bool IsZero => Amount == 0m;

    /// <summary>Creates an amount of money, or reports which rule it breaks.</summary>
    /// <param name="amount">The amount.</param>
    /// <param name="currency">The currency.</param>
    public static DomainResult<Money> Create(decimal amount, Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);

        if (amount < 0m)
        {
            return Fail("The amount must not be negative.");
        }

        if (amount >= Limit)
        {
            return Fail($"The amount must have at most {MaxIntegerDigits} integer digits.");
        }

        var rounded = decimal.Round(amount, currency.MinorUnits, MidpointRounding.ToZero);
        if (rounded != amount)
        {
            return Fail(currency.MinorUnits == 0
                ? $"The amount must be a whole number for {currency.Code}."
                : $"The amount must have at most {currency.MinorUnits} decimal places for {currency.Code}.");
        }

        return DomainResult.Success<Money>(new Money(WithScale(rounded, currency.MinorUnits), currency));
    }

    /// <summary>Creates a zero amount in a currency.</summary>
    /// <param name="currency">The currency.</param>
    public static Money Zero(Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);

        return new Money(WithScale(0m, currency.MinorUnits), currency);
    }

    /// <summary>Adds an amount in the same currency.</summary>
    /// <param name="other">The amount to add.</param>
    public DomainResult<Money> Add(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return other.Currency != Currency ? CurrencyMismatch() : Create(Amount + other.Amount, Currency);
    }

    /// <summary>Subtracts an amount in the same currency. The result must not be negative, because an amount of money is not.</summary>
    /// <param name="other">The amount to subtract.</param>
    public DomainResult<Money> Subtract(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (other.Currency != Currency)
        {
            return CurrencyMismatch();
        }

        return other.Amount > Amount ? Fail("The result must not be negative.") : Create(Amount - other.Amount, Currency);
    }

    /// <inheritdoc />
    public override string ToString() => $"{Amount.ToString(CultureInfo.InvariantCulture)} {Currency.Code}";

    private static DomainResult<Money> Fail(string message) => DomainResult.Failure<Money>(DomainError.Invalid("amount", message));

    private static DomainResult<Money> CurrencyMismatch() => DomainResult.Failure<Money>(DomainError.Invalid("currency", "The currencies must match."));

    // A zero amount is built from its parts, because the sign of a decimal zero would otherwise survive the arithmetic and print as "-0.00".
    private static decimal WithScale(decimal value, int scale) =>
        value == 0m
            ? new decimal(0, 0, 0, isNegative: false, (byte)scale)
            : value + new decimal(0, 0, 0, isNegative: false, (byte)scale);
}
