using System.Diagnostics.CodeAnalysis;

namespace Catalog.Application.Results;

/// <summary>
/// The place that makes every outcome of a use case. A refusal that a caller can cause with ordinary input is a value, a failed
/// <see cref="Result{T}"/> that holds an <see cref="ApplicationError"/>, and an exception is for a programming error or a fault such as a lost connection.
/// </summary>
public static class Result
{
    /// <summary>Returns the success of a use case.</summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="value">The value.</param>
    public static Result<T> Success<T>(T value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new Result<T>(value, null);
    }

    /// <summary>Returns the failure of a use case.</summary>
    /// <typeparam name="T">The type of the value that the use case would have returned.</typeparam>
    /// <param name="error">The reason.</param>
    public static Result<T> Failure<T>(ApplicationError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new Result<T>(default, error);
    }
}

/// <summary>The outcome of a use case. <see cref="Result"/> makes it, and a failure holds one of the typed errors of <see cref="ApplicationError"/>.</summary>
/// <typeparam name="T">The type of the value of a success.</typeparam>
public sealed record Result<T>
{
    private readonly T? _value;

    internal Result(T? value, ApplicationError? error)
    {
        _value = value;
        Error = error;
    }

    /// <summary>Gets a value that says whether the use case succeeded.</summary>
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    /// <summary>Gets the reason of a failure, or null when the use case succeeded.</summary>
    public ApplicationError? Error { get; }

    /// <summary>Gets the value of a success. Reading it from a failure is a programming error and throws.</summary>
    /// <exception cref="InvalidOperationException">The use case failed.</exception>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"The use case failed with {Error.GetType().Name}, so it has no value. Check IsSuccess first.");
}
