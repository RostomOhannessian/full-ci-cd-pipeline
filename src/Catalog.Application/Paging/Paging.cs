using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Catalog.Application.Results;

namespace Catalog.Application.Paging;

/// <summary>
/// The position after which the next page of a keyset query starts. A collection is ordered by a sort key and then by identifier, so the pair identifies one row and no row is skipped or repeated
/// when others are added (plan section 8.4). The persistence layer turns it into a <c>WHERE</c> clause (WP1.8).
/// </summary>
/// <param name="SortKey">The value of the sort key of the last row of the previous page: the creation time in round-trip format, or the name.</param>
/// <param name="Id">The identifier of that row, which breaks a tie between equal sort keys.</param>
public sealed record KeysetPosition(string SortKey, Guid Id);

/// <summary>One page of a collection.</summary>
/// <typeparam name="T">The type of an item.</typeparam>
/// <param name="Items">The items of the page.</param>
/// <param name="NextCursor">The opaque cursor of the next page, or null on the last page.</param>
public sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor);

/// <summary>The size and the start of a page, after they were checked.</summary>
/// <param name="Size">The number of items that the caller wants.</param>
/// <param name="After">The position after which the page starts, or null for the first page.</param>
public sealed record PageRequest(int Size, KeysetPosition? After)
{
    /// <summary>The page size when the caller names none.</summary>
    public const int DefaultSize = 20;

    /// <summary>The smallest page size.</summary>
    public const int MinSize = 1;

    /// <summary>The largest page size.</summary>
    public const int MaxSize = 100;

    /// <summary>Checks the page size and the cursor of a request. The messages name the parameters as the contract does.</summary>
    /// <param name="size">The page size that the caller sent.</param>
    /// <param name="cursor">The cursor that the caller sent, or null for the first page.</param>
    /// <param name="sort">The name of the sort order of the request, which the cursor must have been made for.</param>
    public static Result<PageRequest> Parse(int size, string? cursor, string sort)
    {
        List<FieldError> errors = [];

        if (size is < MinSize or > MaxSize)
        {
            errors.Add(new FieldError("pageSize", $"The page size must be between {MinSize} and {MaxSize}."));
        }

        KeysetPosition? after = null;

        if (cursor is not null && !PageCursor.TryDecode(cursor, sort, out after))
        {
            errors.Add(new FieldError("cursor", "The cursor is not valid for this request. Start again without a cursor."));
        }

        return errors.Count > 0 ? Result.Failure<PageRequest>(new ValidationFailed(errors)) : Result.Success(new PageRequest(size, after));
    }
}

/// <summary>
/// Makes and reads the opaque, versioned cursor of a page. The cursor is the base64url form of a small JSON document that names its version, the sort order, and the position, so a client treats it as a
/// token, and the server can change the format by raising the version (plan section 8.6). A cursor made for one sort order is refused for another.
/// </summary>
public static class PageCursor
{
    /// <summary>The largest length of a cursor, which the contract states.</summary>
    public const int MaxLength = 512;

    private const int Version = 1;

    /// <summary>Makes the cursor of the page that follows a row.</summary>
    /// <param name="sort">The name of the sort order.</param>
    /// <param name="position">The position of the last row of the page.</param>
    public static string Encode(string sort, KeysetPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);

        return Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new CursorPayload(Version, sort, position.SortKey, position.Id)));
    }

    /// <summary>Reads a cursor, or reports that it is not one that <see cref="Encode"/> made for this sort order.</summary>
    /// <param name="cursor">The text that the caller sent back.</param>
    /// <param name="sort">The name of the sort order of the request.</param>
    /// <param name="position">The position, or null when the cursor is not valid.</param>
    /// <returns>True when the cursor is valid for the sort order.</returns>
    public static bool TryDecode(string? cursor, string sort, [NotNullWhen(true)] out KeysetPosition? position)
    {
        position = null;

        if (string.IsNullOrEmpty(cursor) || cursor.Length > MaxLength || !Base64Url.IsValid(cursor))
        {
            return false;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<CursorPayload>(Base64Url.DecodeFromChars(cursor));

            if (payload is not { V: Version, S: var payloadSort, K: { Length: > 0 } key, I: var id } || payloadSort != sort || id == Guid.Empty)
            {
                return false;
            }

            position = new KeysetPosition(key, id);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

/// <summary>The content of a cursor.</summary>
/// <param name="V">The version of the format.</param>
/// <param name="S">The sort order.</param>
/// <param name="K">The sort key of the last row.</param>
/// <param name="I">The identifier of the last row.</param>
internal sealed record CursorPayload(int V, string S, string K, Guid I);
