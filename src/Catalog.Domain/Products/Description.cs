using Catalog.Domain.Common;

namespace Catalog.Domain.Products;

/// <summary>
/// The description of a product: up to 2000 characters of text with the white space around it trimmed. Line breaks and tabs are allowed, and every
/// other control character is refused. A product with no description holds null and not an empty description, so <see cref="Create"/> turns
/// missing text and blank text into null.
/// </summary>
public sealed record Description
{
    /// <summary>The most characters of a description.</summary>
    public const int MaxLength = 2000;

    private Description(string value) => Value = value;

    /// <summary>Gets the trimmed text, which is never empty.</summary>
    public string Value { get; }

    /// <summary>Normalizes a description. Missing and blank text succeed with no description.</summary>
    /// <param name="raw">The description as a caller sent it.</param>
    public static DomainResult<Description?> Create(string? raw)
    {
        var trimmed = raw?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return DomainResult.Success<Description?>(null);
        }

        return TextRules.Inspect(trimmed, MaxLength, allowLineBreaks: true, out _) switch
        {
            TextProblem.None => DomainResult.Success<Description?>(new Description(trimmed)),
            TextProblem.ControlCharacter => Fail("The description must not contain control characters other than line breaks and tabs."),
            TextProblem.InvalidUnicode => Fail("The description must be valid Unicode text."),
            _ => Fail($"The description must have at most {MaxLength} characters."),
        };
    }

    /// <inheritdoc />
    public override string ToString() => Value;

    private static DomainResult<Description?> Fail(string message) => DomainResult.Failure<Description?>(DomainError.Invalid("description", message));
}
