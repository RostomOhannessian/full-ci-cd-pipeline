namespace Catalog.Contracts.V1;

/// <summary>An amount of money (<c>Money</c> in the contract). The amount is a string, because a JSON number loses digits in many clients.</summary>
/// <param name="Amount">A decimal number that is not negative, with no sign and no exponent, such as <c>89.99</c>.</param>
/// <param name="Currency">The ISO 4217 code of the currency, one of <c>USD</c>, <c>EUR</c>, <c>GBP</c>, and <c>JPY</c>.</param>
public sealed record Money(string Amount, string Currency);
