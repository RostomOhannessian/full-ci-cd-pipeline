using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;

namespace Catalog.Application.Persistence;

/// <summary>
/// The version of a stored resource, as an opaque token that is safe in a URL. The persistence layer makes it from the row version of the database, and the API
/// writes it as a strong <c>ETag</c> in quotes. Two tags are the same version when their values are equal, which is the strong comparison of RFC 9110.
/// </summary>
public sealed record EntityTag
{
    private EntityTag(string value) => Value = value;

    /// <summary>Gets the token.</summary>
    public string Value { get; }

    /// <summary>Creates a tag from a row version.</summary>
    /// <param name="version">The bytes of the version. They must not be empty.</param>
    public static EntityTag FromBytes(ReadOnlySpan<byte> version)
    {
        if (version.IsEmpty)
        {
            throw new ArgumentException("A version has at least one byte.", nameof(version));
        }

        return new EntityTag(Base64Url.EncodeToString(version));
    }

    /// <summary>Creates a tag from the text that a caller sent back, or reports that the text cannot be a tag.</summary>
    /// <param name="raw">The token without its quotes.</param>
    /// <param name="tag">The tag, or null when the text is not a token that <see cref="FromBytes"/> could have made.</param>
    /// <returns>True when the text is a valid tag.</returns>
    public static bool TryCreate(string? raw, [NotNullWhen(true)] out EntityTag? tag)
    {
        if (!string.IsNullOrEmpty(raw) && raw.Length <= 64 && Base64Url.IsValid(raw))
        {
            tag = new EntityTag(raw);
            return true;
        }

        tag = null;
        return false;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>A resource together with the version that it had when it was loaded.</summary>
/// <typeparam name="T">The type of the resource.</typeparam>
/// <param name="Entity">The resource.</param>
/// <param name="Tag">The version.</param>
public sealed record Versioned<T>(T Entity, EntityTag Tag);
