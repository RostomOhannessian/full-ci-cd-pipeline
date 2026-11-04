using System.Diagnostics.CodeAnalysis;

namespace Catalog.Api.Http;

/// <summary>
/// The outcome of reading a request into a command or a query: the request, or the response that refuses it. The endpoint returns the failure as it is, so every refusal of the input is made in one place and
/// has the shape of the error catalog.
/// </summary>
/// <typeparam name="T">The type of the request that a success carries.</typeparam>
/// <param name="Value">The request, or null when reading failed.</param>
/// <param name="Failure">The response that refuses the request, or null when reading succeeded.</param>
internal readonly record struct Outcome<T>(T? Value, IResult? Failure)
    where T : class
{
    /// <summary>Gets a value that says whether the request was read.</summary>
    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Failure))]
    public bool IsSuccess => Failure is null;
}

/// <summary>Makes the outcomes of reading a request.</summary>
internal static class Outcome
{
    /// <summary>Returns the success that carries the request.</summary>
    /// <typeparam name="T">The type of the request.</typeparam>
    /// <param name="value">The request.</param>
    public static Outcome<T> Ok<T>(T value)
        where T : class => new(value, null);

    /// <summary>Returns the failure that carries the response.</summary>
    /// <typeparam name="T">The type of the request that was not read.</typeparam>
    /// <param name="failure">The response that refuses the request.</param>
    public static Outcome<T> Fail<T>(IResult failure)
        where T : class => new(null, failure);
}
