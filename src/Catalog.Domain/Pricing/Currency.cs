using Catalog.Domain.Common;

namespace Catalog.Domain.Pricing;

/// <summary>
/// An ISO 4217 currency from the set that the catalog accepts. The set is the one in the version 1 contract (<c>CurrencyCode</c>), and a
/// test compares the two, so adding a currency is a change to the contract first (ADR-0028). The code is the three capital letters and nothing else,
/// because the contract does not accept another spelling.
/// </summary>
public sealed record Currency
{
    private Currency(string code, int minorUnits)
    {
        Code = code;
        MinorUnits = minorUnits;
    }

    /// <summary>Gets the United States dollar, which has two decimal places.</summary>
    public static Currency Usd { get; } = new("USD", 2);

    /// <summary>Gets the euro, which has two decimal places.</summary>
    public static Currency Eur { get; } = new("EUR", 2);

    /// <summary>Gets the pound sterling, which has two decimal places.</summary>
    public static Currency Gbp { get; } = new("GBP", 2);

    /// <summary>Gets the Japanese yen, which has no decimal places.</summary>
    public static Currency Jpy { get; } = new("JPY", 0);

    /// <summary>Gets every accepted currency, in a fixed order.</summary>
    public static IReadOnlyList<Currency> All { get; } = [Usd, Eur, Gbp, Jpy];

    /// <summary>Gets the three-letter ISO 4217 code.</summary>
    public string Code { get; }

    /// <summary>Gets the number of decimal places of the smallest unit, which is the most that an amount in this currency may have.</summary>
    public int MinorUnits { get; }

    /// <summary>Finds a currency by its code.</summary>
    /// <param name="code">The code, in capital letters.</param>
    public static DomainResult<Currency> Create(string? code)
    {
        var found = All.FirstOrDefault(currency => string.Equals(currency.Code, code, StringComparison.Ordinal));

        return found is null
            ? DomainResult.Failure<Currency>(DomainError.Invalid("currency", $"The currency must be one of {string.Join(", ", All.Select(currency => currency.Code))}."))
            : DomainResult.Success<Currency>(found);
    }

    /// <inheritdoc />
    public override string ToString() => Code;
}
