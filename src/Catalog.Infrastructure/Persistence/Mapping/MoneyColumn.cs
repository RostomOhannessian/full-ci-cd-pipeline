using Catalog.Domain.Pricing;

namespace Catalog.Infrastructure.Persistence.Mapping;

/// <summary>The width of the column that holds an amount of money, which both models use, so the write model and the read model state the same precision.</summary>
internal static class MoneyColumn
{
    /// <summary>The number of decimal places of the column: the most that any accepted currency has.</summary>
    public static int Scale() => Currency.All.Max(currency => currency.MinorUnits);

    /// <summary>The number of digits of the column: the integer digits of the contract and the decimal places.</summary>
    public static int Precision() => Money.MaxIntegerDigits + Scale();
}
