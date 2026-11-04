using Catalog.Application.Idempotency;
using Catalog.Application.Persistence;
using Catalog.Application.Results;

namespace Catalog.Api.Http;

/// <summary>
/// Reads the headers that make a request conditional or idempotent (plan section 8.6, REQ-API-003 and REQ-API-004). Each value is one strong token in quotes, as the contract states, so a weak tag, a list of tags,
/// and <c>*</c> are errors of the header and never a way around the version check. The use cases decide what a missing <c>If-Match</c> means, so a missing header reads as no tag here. The shapes are checked
/// character by character, because the source generator of regular expressions adds a type of its own to the assembly, and the architecture tests allow only types of the layer there.
/// </summary>
internal static class ConditionalHeaders
{
    /// <summary>The name of the header that carries the version that a change is based on.</summary>
    public const string IfMatch = "If-Match";

    /// <summary>The name of the header that carries the version that a client already holds.</summary>
    public const string IfNoneMatch = "If-None-Match";

    /// <summary>The name of the header that makes a create safe to repeat.</summary>
    public const string IdempotencyKeyName = "Idempotency-Key";

    private const int MaxTagLength = 64;

    /// <summary>Reads <c>If-Match</c>, which is null when the caller sent none.</summary>
    /// <param name="request">The request.</param>
    public static Outcome<TagRead> ReadIfMatch(HttpRequest request) => ReadTag(request, IfMatch);

    /// <summary>Reads <c>If-None-Match</c>, which is null when the caller sent none.</summary>
    /// <param name="request">The request.</param>
    public static Outcome<TagRead> ReadIfNoneMatch(HttpRequest request) => ReadTag(request, IfNoneMatch);

    /// <summary>Reads <c>Idempotency-Key</c>. A missing or invalid key refuses the request with <c>idempotency-key-required</c>.</summary>
    /// <param name="request">The request.</param>
    public static Outcome<KeyRead> ReadIdempotencyKey(HttpRequest request)
    {
        var values = request.Headers[IdempotencyKeyName];

        if (values.Count == 1 && TryUnquote(values[0], IdempotencyKey.MaxLength, IsKeyCharacter, out var text) && IdempotencyKey.TryCreate(text, out var key))
        {
            return Outcome.Ok(new KeyRead(key));
        }

        return Outcome.Fail<KeyRead>(Problems.Of(
            ApiProblems.IdempotencyKeyRequired,
            "Send one Idempotency-Key header with a quoted string of 1 to 128 printable ASCII characters, such as \"c1b7e2a4-5f1d-4d0a-8f2e-3a6d9e1b7c42\"."));
    }

    private static Outcome<TagRead> ReadTag(HttpRequest request, string header)
    {
        var values = request.Headers[header];

        if (values.Count == 0)
        {
            return Outcome.Ok(new TagRead(null));
        }

        if (values.Count == 1 && TryUnquote(values[0], MaxTagLength, IsTagCharacter, out var text) && EntityTag.TryCreate(text, out var tag))
        {
            return Outcome.Ok(new TagRead(tag));
        }

        return Outcome.Fail<TagRead>(Problems.Validation(new FieldError(header, $"The {header} header must hold one strong ETag in quotes, as the API returned it. A weak tag, a list, and * are not accepted.")));
    }

    // The characters of the token of an ETag, which is base64url text.
    private static bool IsTagCharacter(char character) => char.IsAsciiLetterOrDigit(character) || character is '-' or '_';

    // The printable ASCII characters except the quote and the backslash, which a Structured Field string must escape and this API does not accept.
    private static bool IsKeyCharacter(char character) => character is >= '\u0021' and <= '\u007E' and not '"' and not '\\';

    // The value is a non-empty run of allowed characters between two quotes, and nothing else.
    private static bool TryUnquote(string? value, int maxLength, Func<char, bool> allowed, out string text)
    {
        text = string.Empty;

        if (value is not { Length: >= 3 } || value[0] != '"' || value[^1] != '"')
        {
            return false;
        }

        var inner = value.AsSpan(1, value.Length - 2);

        if (inner.Length > maxLength)
        {
            return false;
        }

        foreach (var character in inner)
        {
            if (!allowed(character))
            {
                return false;
            }
        }

        text = inner.ToString();

        return true;
    }
}

/// <summary>A tag that a header carried, or no tag when the header was absent.</summary>
/// <param name="Tag">The tag, or null when the caller sent none.</param>
internal sealed record TagRead(EntityTag? Tag);

/// <summary>A key that the header carried.</summary>
/// <param name="Key">The key without its quotes.</param>
internal sealed record KeyRead(IdempotencyKey Key);
