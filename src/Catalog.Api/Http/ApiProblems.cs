using Catalog.Application.Results;
using Microsoft.AspNetCore.WebUtilities;

namespace Catalog.Api.Http;

/// <summary>
/// The entries of the error catalog that only the API raises (<c>docs/reference/error-catalog.md</c>, ADR-0028). The application raises the other nine, and <c>ProblemMapping</c> holds them. A test compares
/// every row here with the catalog page, so a status or a title cannot drift from what the clients read.
/// </summary>
internal static class ApiProblems
{
    /// <summary>The address of the error catalog. The slug of an entry follows the fragment sign.</summary>
    public const string CatalogAddress = "https://rostomohannessian.github.io/full-ci-cd-pipeline/reference/error-catalog.html";

    /// <summary>The type of an error whose status has no entry in the catalog (RFC 9457).</summary>
    public const string AboutBlank = "about:blank";

    /// <summary>Gets <c>malformed-request</c>: the body cannot be parsed.</summary>
    public static ProblemType MalformedRequest { get; } = new("malformed-request", 400, "The request could not be parsed.");

    /// <summary>Gets <c>idempotency-key-required</c>: a create has no valid <c>Idempotency-Key</c>.</summary>
    public static ProblemType IdempotencyKeyRequired { get; } = new("idempotency-key-required", 400, "An Idempotency-Key header is required.");

    /// <summary>Gets <c>unauthorized</c>: the token is missing or not accepted.</summary>
    public static ProblemType Unauthorized { get; } = new("unauthorized", 401, "Authentication is required.");

    /// <summary>Gets <c>payload-too-large</c>: the body is larger than the limit.</summary>
    public static ProblemType PayloadTooLarge { get; } = new("payload-too-large", 413, "The request body is too large.");

    /// <summary>Gets <c>unsupported-media-type</c>: the body is not <c>application/json</c>.</summary>
    public static ProblemType UnsupportedMediaType { get; } = new("unsupported-media-type", 415, "The request body must be application/json.");

    /// <summary>Gets <c>rate-limited</c>: the caller sent too many requests.</summary>
    public static ProblemType RateLimited { get; } = new("rate-limited", 429, "Too many requests.");

    /// <summary>Gets <c>internal-error</c>: the server failed in a way that it did not expect.</summary>
    public static ProblemType InternalError { get; } = new("internal-error", 500, "An unexpected error occurred.");

    /// <summary>Gets <c>service-unavailable</c>: a dependency cannot be reached.</summary>
    public static ProblemType ServiceUnavailable { get; } = new("service-unavailable", 503, "The service is temporarily unavailable.");

    /// <summary>Gets every entry that only the API raises.</summary>
    public static IReadOnlyList<ProblemType> All { get; } =
    [
        MalformedRequest,
        IdempotencyKeyRequired,
        Unauthorized,
        PayloadTooLarge,
        UnsupportedMediaType,
        RateLimited,
        InternalError,
        ServiceUnavailable,
    ];

    /// <summary>Makes the entry for a status that the catalog does not list, such as 405. RFC 9457 gives such an error the type <c>about:blank</c>.</summary>
    /// <param name="status">The HTTP status code.</param>
    public static ProblemType Other(int status) => new(AboutBlank, status, ReasonPhrases.GetReasonPhrase(status));

    /// <summary>Returns the <c>type</c> address of an entry.</summary>
    /// <param name="type">The entry.</param>
    public static string AddressOf(ProblemType type) => type.Slug == AboutBlank ? AboutBlank : $"{CatalogAddress}#{type.Slug}";
}
