using Catalog.Application.Security;

namespace Catalog.Application.Results;

/// <summary>
/// The single mapping table from an <see cref="ApplicationError"/> to an entry of the error catalog (ADR-0028, ADR-0032). The API serializes the result as
/// <c>application/problem+json</c>, and a test compares every row with the catalog and the contract. The entries that the application never raises (a body that
/// cannot be parsed, a missing idempotency key, a missing token, a body that is too large or of the wrong type, a rate limit, and a server fault) are the API's.
/// </summary>
public static class ProblemMapping
{
    /// <summary>Gets <c>validation-failed</c>.</summary>
    public static ProblemType ValidationFailed { get; } = new("validation-failed", 400, "One or more validation errors occurred.");

    /// <summary>Gets <c>forbidden</c>.</summary>
    public static ProblemType Forbidden { get; } = new("forbidden", 403, "The token does not allow this operation.");

    /// <summary>Gets <c>not-found</c>.</summary>
    public static ProblemType NotFound { get; } = new("not-found", 404, "The resource was not found.");

    /// <summary>Gets <c>sku-already-exists</c>.</summary>
    public static ProblemType SkuAlreadyExists { get; } = new("sku-already-exists", 409, "The SKU is already in use.");

    /// <summary>Gets <c>product-discontinued</c>.</summary>
    public static ProblemType ProductDiscontinued { get; } = new("product-discontinued", 409, "A discontinued product cannot change its price or category.");

    /// <summary>Gets <c>idempotency-request-in-progress</c>.</summary>
    public static ProblemType IdempotencyRequestInProgress { get; } = new("idempotency-request-in-progress", 409, "A request with this Idempotency-Key is still running.");

    /// <summary>Gets <c>precondition-failed</c>.</summary>
    public static ProblemType PreconditionFailed { get; } = new("precondition-failed", 412, "The resource has changed.");

    /// <summary>Gets <c>idempotency-key-reused</c>.</summary>
    public static ProblemType IdempotencyKeyReused { get; } = new("idempotency-key-reused", 422, "The Idempotency-Key was used with a different request.");

    /// <summary>Gets <c>precondition-required</c>.</summary>
    public static ProblemType PreconditionRequired { get; } = new("precondition-required", 428, "An If-Match header is required.");

    /// <summary>Gets every entry that the application can raise.</summary>
    public static IReadOnlyList<ProblemType> All { get; } =
    [
        ValidationFailed,
        Forbidden,
        NotFound,
        SkuAlreadyExists,
        ProductDiscontinued,
        IdempotencyRequestInProgress,
        PreconditionFailed,
        IdempotencyKeyReused,
        PreconditionRequired,
    ];

    /// <summary>Returns the entry of the error catalog for an application error, with the detail of this occurrence.</summary>
    /// <param name="error">The refusal.</param>
    /// <exception cref="ArgumentOutOfRangeException">The error is a case that the table does not know, which is a programming error.</exception>
    public static Problem Map(ApplicationError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return error switch
        {
            Results.ValidationFailed validation => new Problem(ValidationFailed, "One or more fields are not valid. The errors member lists each one.", validation.Errors),
            Results.NotFound notFound => new Problem(NotFound, $"The {notFound.Resource} was not found.", []),
            Conflict { Reason: ConflictReason.SkuAlreadyExists } => new Problem(SkuAlreadyExists, "Another product already uses this SKU.", []),
            Conflict { Reason: ConflictReason.ProductDiscontinued } => new Problem(ProductDiscontinued, "A discontinued product cannot change its price or its category.", []),
            Conflict { Reason: ConflictReason.IdempotencyRequestInProgress } => new Problem(IdempotencyRequestInProgress, "A first request with the same Idempotency-Key has not finished. Retry the same request later.", []),
            Results.PreconditionFailed => new Problem(PreconditionFailed, "The If-Match value is not the current ETag of the resource. Read the resource again, then retry.", []),
            Results.PreconditionRequired => new Problem(PreconditionRequired, "Send an If-Match header with the ETag of the resource.", []),
            Results.Forbidden forbidden => new Problem(Forbidden, DescribeForbidden(forbidden), []),
            Results.IdempotencyKeyReused => new Problem(IdempotencyKeyReused, "This Idempotency-Key was already used with a request that is not the same as this one.", []),
            _ => throw new ArgumentOutOfRangeException(nameof(error), error, "The error has no entry in the mapping table."),
        };
    }

    private static string DescribeForbidden(Forbidden forbidden)
    {
        var requirement = PermissionPolicy.For(forbidden.Required);

        return forbidden.Reason == ForbiddenReason.MissingScope
            ? $"This operation requires the {requirement.Scope} scope."
            : $"This operation requires the {requirement.Scope} scope and one of the roles {string.Join(", ", requirement.Roles)}.";
    }
}
