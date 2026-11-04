using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Catalog.Api.Http;
using Catalog.Application.Results;
using Catalog.Contracts.V1;

namespace Catalog.Api.Mapping;

/// <summary>
/// Reads the parts of a request that every operation shares and that belong to the transport: the identifier in the path and the price in a body. A failure is a validation error that names the field, as the
/// contract says. What a value means to the catalog (a name, a SKU, how many decimal places a currency has) is for the use case, so nothing here repeats a rule of the model.
/// </summary>
internal static class TransportInput
{
    private const int MaxWholeDigits = 12;

    private const int MaxFractionDigits = 4;

    /// <summary>Reads an identifier from a path segment.</summary>
    /// <param name="field">The name of the path parameter, which names the field of the error.</param>
    /// <param name="text">The text of the segment.</param>
    /// <param name="id">The identifier, or the empty identifier when the text is not one.</param>
    /// <param name="failure">The response that refuses the request when the text is not an identifier.</param>
    /// <returns>True when the text is an identifier.</returns>
    public static bool TryReadId(string field, string? text, out Guid id, [NotNullWhen(false)] out IResult? failure)
    {
        if (Guid.TryParseExact(text, "D", out id))
        {
            failure = null;

            return true;
        }

        failure = Problems.Validation(new FieldError(field, "The identifier must be a UUID."));

        return false;
    }

    /// <summary>
    /// Reads the price of a body into the amount and the currency that a use case takes. The amount is a string in the contract, so it must have the shape that the contract gives it before it becomes a number,
    /// and anything else is an error of the field <c>price.amount</c>. A missing currency goes on to the use case, which names the field <c>price.currency</c>.
    /// </summary>
    /// <param name="price">The price of the body, or null when the body had none.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="currency">The currency code as the caller wrote it, or null.</param>
    /// <param name="failure">The response that refuses the request when the price cannot be read.</param>
    /// <returns>True when the price could be read.</returns>
    public static bool TryReadPrice(Money? price, out decimal amount, out string? currency, [NotNullWhen(false)] out IResult? failure)
    {
        amount = 0m;
        currency = price?.Currency;

        if (price is null)
        {
            failure = Problems.Validation(new FieldError("price", "The price is required."));

            return false;
        }

        if (price.Amount is not { } text || !IsAmount(text) || !decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount))
        {
            failure = Problems.Validation(new FieldError("price.amount", "The amount must be a decimal number such as 89.99, with at most 12 integer digits, no sign, and no exponent."));

            return false;
        }

        failure = null;

        return true;
    }

    // The shape that the contract gives an amount: no sign, no exponent, at most 12 digits before the point, no leading zero except for zero itself, and 1 to 4 digits after the point when there is one.
    private static bool IsAmount(string text)
    {
        var point = text.IndexOf('.', StringComparison.Ordinal);
        var whole = point < 0 ? text : text[..point];
        var fraction = point < 0 ? string.Empty : text[(point + 1)..];

        var wholeIsValid = whole.Length is >= 1 and <= MaxWholeDigits && whole.All(char.IsAsciiDigit) && (whole.Length == 1 || whole[0] != '0');
        var fractionIsValid = point < 0 || (fraction.Length is >= 1 and <= MaxFractionDigits && fraction.All(char.IsAsciiDigit));

        return wholeIsValid && fractionIsValid;
    }
}
