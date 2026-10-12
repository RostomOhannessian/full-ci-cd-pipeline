using System.Diagnostics.CodeAnalysis;

namespace Catalog.Domain.Common;

/// <summary>
/// The outcome of a domain operation that returns nothing, and the place that makes every outcome. A failure is a value and not an exception, because a rule
/// that a caller can break with ordinary input is an expected outcome. An exception is for a programming error, such as a null argument.
/// </summary>
public sealed record DomainResult
{
    private static readonly DomainResult Succeeded = new((DomainError?)null);

    private DomainResult(DomainError? error) => Error = error;

    /// <summary>Gets a value that says whether the operation succeeded.</summary>
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    /// <summary>Gets the failure, or null when the operation succeeded.</summary>
    public DomainError? Error { get; }

    /// <summary>Returns the success of an operation that returns nothing.</summary>
    public static DomainResult Success() => Succeeded;

    /// <summary>Returns the success of an operation that returns a value.</summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="value">The value.</param>
    public static DomainResult<T> Success<T>(T value) => new(value, null);

    /// <summary>Returns the failure of an operation that returns nothing.</summary>
    /// <param name="error">The failed rule.</param>
    public static DomainResult Failure(DomainError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new DomainResult(error);
    }

    /// <summary>Returns the failure of an operation that returns a value.</summary>
    /// <typeparam name="T">The type of the value that the operation would have returned.</typeparam>
    /// <param name="error">The failed rule.</param>
    public static DomainResult<T> Failure<T>(DomainError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new DomainResult<T>(default, error);
    }
}

/// <summary>The outcome of a domain operation that returns a value. <see cref="DomainResult"/> makes it.</summary>
/// <typeparam name="T">The type of the value.</typeparam>
public sealed record DomainResult<T>
{
    private readonly T? _value;

    internal DomainResult(T? value, DomainError? error)
    {
        _value = value;
        Error = error;
    }

    /// <summary>Gets a value that says whether the operation succeeded.</summary>
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    /// <summary>Gets the failure, or null when the operation succeeded.</summary>
    public DomainError? Error { get; }

    /// <summary>Gets the value of a success. Reading it from a failure is a programming error and throws.</summary>
    /// <exception cref="InvalidOperationException">The operation failed.</exception>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"The operation failed with {Error.Code} on '{Error.Field}', so it has no value. Check IsSuccess first.");

    /// <summary>Returns the failure of this result for a value that sits inside another one, or this result when it succeeded.</summary>
    /// <param name="parent">The path of the containing value.</param>
    public DomainResult<T> Within(string parent) => IsSuccess ? this : DomainResult.Failure<T>(Error.Within(parent));
}
