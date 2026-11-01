using Catalog.Application.Persistence;
using Catalog.Application.Results;
using Catalog.Domain.Common;

namespace Catalog.Application.Messaging;

/// <summary>Small helpers that every handler shares, so each rule of the contract is written once.</summary>
internal static class Handling
{
    /// <summary>Returns a failed result for a rule of the domain model.</summary>
    public static Result<T> Fail<T>(DomainError error) => Result.Failure<T>(ApplicationError.From(error));

    /// <summary>Returns a validation error for one field.</summary>
    public static Result<T> Invalid<T>(string field, string message) => Result.Failure<T>(new ValidationFailed([new FieldError(field, message)]));

    /// <summary>Checks that the caller sent a version (428 when it did not). The check comes first, because it needs no lookup.</summary>
    public static ApplicationError? RequireVersion(EntityTag? ifMatch) => ifMatch is null ? new PreconditionRequired() : null;

    /// <summary>Checks that the version that the caller sent is the current one (412 when it is not). The comparison is the strong one: the tokens are equal.</summary>
    public static ApplicationError? MatchVersion(EntityTag sent, EntityTag current) => sent.Equals(current) ? null : new PreconditionFailed();
}
