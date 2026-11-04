namespace Catalog.Contracts.V1;

/// <summary>
/// An error as RFC 9457 Problem Details, the <c>ProblemDetails</c> schema of the version 1 contract. The <see cref="Type"/> is a URL of the error catalog, and a client branches on it and never on the title or the
/// detail. The detail is a safe message for a person and never holds a stack trace, a query, or a secret.
/// </summary>
/// <param name="Type">The URL of the entry of the error catalog. Its fragment names the entry.</param>
/// <param name="Title">A short summary that is the same for every error of the type.</param>
/// <param name="Status">The HTTP status code of the response.</param>
/// <param name="Detail">An explanation of this occurrence, or null.</param>
/// <param name="Instance">The path of the request, or null.</param>
/// <param name="TraceId">The W3C trace identifier of the request, which links the error to its trace and its logs.</param>
public record ProblemDetails(string Type, string Title, int Status, string? Detail, string? Instance, string TraceId);

/// <summary>
/// A Problem Details body that also lists the messages for each invalid field, the <c>ValidationProblemDetails</c> schema of the version 1 contract. The <see cref="Errors"/> member is present when the type is
/// <c>validation-failed</c>.
/// </summary>
/// <param name="Type">The URL of the entry of the error catalog.</param>
/// <param name="Title">A short summary that is the same for every error of the type.</param>
/// <param name="Status">The HTTP status code of the response.</param>
/// <param name="Detail">An explanation of this occurrence, or null.</param>
/// <param name="Instance">The path of the request, or null.</param>
/// <param name="TraceId">The W3C trace identifier of the request.</param>
/// <param name="Errors">The messages for each invalid field. A key is the JSON path of the field, such as <c>price.amount</c>, and a parameter or a header uses its own name.</param>
public sealed record ValidationProblemDetails(string Type, string Title, int Status, string? Detail, string? Instance, string TraceId, IReadOnlyDictionary<string, string[]> Errors)
    : ProblemDetails(Type, Title, Status, Detail, Instance, TraceId);
