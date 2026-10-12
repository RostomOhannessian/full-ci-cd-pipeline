namespace Catalog.Domain.Common;

/// <summary>The kinds of failure that a domain rule reports. The application maps each kind to one entry of the error catalog.</summary>
public enum DomainErrorCode
{
    /// <summary>A value breaks a rule of its type, such as a name that is too long. It maps to <c>validation-failed</c>.</summary>
    Invalid,

    /// <summary>A discontinued product would change its price or its category. It maps to <c>product-discontinued</c>.</summary>
    ProductDiscontinued,

    /// <summary>A product would move to a category that is discontinued. It maps to <c>validation-failed</c> on <c>categoryId</c>.</summary>
    CategoryNotActive,
}

/// <summary>
/// A failed domain rule. The field is the JSON path of the value in the version 1 contract, such as <c>name</c> or <c>price.amount</c>,
/// so the application can list it under <c>errors</c> without a translation table. A test of the contract checks that the paths agree.
/// </summary>
/// <param name="Code">The kind of failure.</param>
/// <param name="Field">The path of the value that failed, in lower camel case, or the empty string when the failure belongs to no single value.</param>
/// <param name="Message">A message for a person. It is safe to return to a caller, and it never holds a value that a caller sent.</param>
public sealed record DomainError(DomainErrorCode Code, string Field, string Message)
{
    /// <summary>Returns the same error for a value that sits inside another one, such as the amount inside a price.</summary>
    /// <param name="parent">The path of the containing value.</param>
    public DomainError Within(string parent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parent);

        return this with { Field = Field.Length == 0 ? parent : $"{parent}.{Field}" };
    }

    /// <summary>Creates an error for a value that breaks a rule of its type.</summary>
    /// <param name="field">The path of the value.</param>
    /// <param name="message">The message.</param>
    public static DomainError Invalid(string field, string message) => new(DomainErrorCode.Invalid, field, message);
}
