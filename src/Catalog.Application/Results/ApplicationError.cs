using Catalog.Application.Security;
using Catalog.Domain.Common;

namespace Catalog.Application.Results;

/// <summary>
/// The reason that a use case refused to do its work. The set is closed by design: only the cases below exist, and the constructor is not reachable from outside this
/// assembly. <see cref="ProblemMapping"/> turns each case into one entry of the error catalog, and nothing else decides the status code of a refusal.
/// </summary>
public abstract record ApplicationError
{
    private protected ApplicationError()
    {
    }

    /// <summary>Returns the application error for a rule that the domain model refused.</summary>
    /// <param name="error">The failed rule.</param>
    public static ApplicationError From(DomainError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return error.Code switch
        {
            DomainErrorCode.Invalid or DomainErrorCode.CategoryNotActive => new ValidationFailed([new FieldError(error.Field, error.Message)]),
            DomainErrorCode.ProductDiscontinued => new Conflict(ConflictReason.ProductDiscontinued),
            _ => throw new ArgumentOutOfRangeException(nameof(error), error.Code, "The domain error code has no application error."),
        };
    }
}

/// <summary>A message about one field, header, or parameter of a request.</summary>
/// <param name="Field">The JSON path of the field, such as <c>price.amount</c>, or the name of the parameter or the header, such as <c>pageSize</c>.</param>
/// <param name="Message">A message for a person. It is safe to return to a caller, and it never holds a value that a caller sent.</param>
public sealed record FieldError(string Field, string Message);

/// <summary>A field, a parameter, or a header is not valid. It maps to <c>validation-failed</c>.</summary>
/// <param name="Errors">The messages, in the order that the rules ran. A field can appear more than once.</param>
public sealed record ValidationFailed(IReadOnlyList<FieldError> Errors) : ApplicationError;

/// <summary>No product or category has the identifier. It maps to <c>not-found</c>.</summary>
/// <param name="Resource">The kind of resource, <c>product</c> or <c>category</c>.</param>
public sealed record NotFound(string Resource) : ApplicationError;

/// <summary>The kinds of state conflict that a use case reports.</summary>
public enum ConflictReason
{
    /// <summary>Another product has the same SKU. It maps to <c>sku-already-exists</c>.</summary>
    SkuAlreadyExists,

    /// <summary>A discontinued product would change its price or its category. It maps to <c>product-discontinued</c>.</summary>
    ProductDiscontinued,

    /// <summary>A request with the same idempotency key has not finished. It maps to <c>idempotency-request-in-progress</c>.</summary>
    IdempotencyRequestInProgress,
}

/// <summary>The request conflicts with the state of the catalog.</summary>
/// <param name="Reason">What conflicts.</param>
public sealed record Conflict(ConflictReason Reason) : ApplicationError;

/// <summary>The version that the caller sent is not the current version of the resource. It maps to <c>precondition-failed</c>.</summary>
public sealed record PreconditionFailed : ApplicationError;

/// <summary>The caller did not send the version of the resource that the operation needs. It maps to <c>precondition-required</c>.</summary>
public sealed record PreconditionRequired : ApplicationError;

/// <summary>Why a caller is not allowed to do an operation.</summary>
public enum ForbiddenReason
{
    /// <summary>The token does not carry the scope of the access level.</summary>
    MissingScope,

    /// <summary>The token carries the scope, and none of its roles grants the access level. Keycloak can grant a scope to a user who lacks the role (risk R21).</summary>
    MissingRole,
}

/// <summary>The caller is authenticated and is not allowed to do the operation. It maps to <c>forbidden</c>.</summary>
/// <param name="Required">The access level that the operation needs.</param>
/// <param name="Reason">What the caller lacks.</param>
public sealed record Forbidden(AccessLevel Required, ForbiddenReason Reason) : ApplicationError;

/// <summary>The idempotency key was already used with a different request. It maps to <c>idempotency-key-reused</c>.</summary>
public sealed record IdempotencyKeyReused : ApplicationError;
