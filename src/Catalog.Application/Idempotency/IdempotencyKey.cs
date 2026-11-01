using System.Diagnostics.CodeAnalysis;

namespace Catalog.Application.Idempotency;

/// <summary>
/// The key that a caller sends in the <c>Idempotency-Key</c> header to make a <c>POST</c> that creates a resource safe to repeat: 1 to 128 printable ASCII
/// characters (ADR-0028). The API removes the quotes of the Structured Field string before it creates the key, so the key holds the text only.
/// </summary>
public sealed record IdempotencyKey
{
    /// <summary>The largest number of characters of a key.</summary>
    public const int MaxLength = 128;

    private IdempotencyKey(string value) => Value = value;

    /// <summary>Gets the text of the key.</summary>
    public string Value { get; }

    /// <summary>Creates a key, or reports that the text is not a valid one.</summary>
    /// <param name="raw">The text of the key without its quotes.</param>
    /// <param name="key">The key, or null when the text is not valid.</param>
    /// <returns>True when the text is a valid key.</returns>
    public static bool TryCreate(string? raw, [NotNullWhen(true)] out IdempotencyKey? key)
    {
        if (raw is { Length: > 0 and <= MaxLength } && raw.All(character => character is >= ' ' and <= '~'))
        {
            key = new IdempotencyKey(raw);
            return true;
        }

        key = null;
        return false;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
