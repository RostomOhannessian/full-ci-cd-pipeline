using Catalog.Domain.Common;

namespace Catalog.Domain.Products;

/// <summary>
/// A stock keeping unit: three to 32 letters and digits, with single hyphens between them. It is kept in capital letters, so <c>kb-100</c> and
/// <c>KB-100</c> are one SKU and the unique index of the database needs no collation. Only ASCII is accepted, which is what the contract's pattern
/// accepts, and the checks need no culture. Surrounding white space is refused and not trimmed, because the contract's pattern refuses it too.
/// </summary>
public sealed record Sku
{
    /// <summary>The fewest characters of a SKU.</summary>
    public const int MinLength = 3;

    /// <summary>The most characters of a SKU.</summary>
    public const int MaxLength = 32;

    private Sku(string value) => Value = value;

    /// <summary>Gets the SKU in capital letters.</summary>
    public string Value { get; }

    /// <summary>Normalizes a SKU, or reports which rule it breaks.</summary>
    /// <param name="raw">The SKU as a caller sent it.</param>
    public static DomainResult<Sku> Create(string? raw)
    {
        if (raw is null || raw.Length is < MinLength or > MaxLength)
        {
            return Fail($"The SKU must have between {MinLength} and {MaxLength} characters.");
        }

        if (!HasOnlyLettersDigitsAndSingleHyphens(raw))
        {
            return Fail("The SKU must contain only letters, digits, and single hyphens between them.");
        }

        return DomainResult.Success<Sku>(new Sku(raw.ToUpperInvariant()));
    }

    /// <inheritdoc />
    public override string ToString() => Value;

    private static DomainResult<Sku> Fail(string message) => DomainResult.Failure<Sku>(DomainError.Invalid("sku", message));

    private static bool HasOnlyLettersDigitsAndSingleHyphens(string text)
    {
        if (text[0] == '-' || text[^1] == '-')
        {
            return false;
        }

        var previousWasHyphen = false;
        foreach (var character in text)
        {
            var isHyphen = character == '-';
            if (isHyphen && previousWasHyphen)
            {
                return false;
            }

            if (!isHyphen && !char.IsAsciiLetterOrDigit(character))
            {
                return false;
            }

            previousWasHyphen = isHyphen;
        }

        return true;
    }
}
