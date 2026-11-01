namespace Catalog.Application.Results;

/// <summary>
/// One entry of the error catalog (<c>docs/reference/error-catalog.md</c>) as the application knows it. The API turns the slug into the <c>type</c> address and adds
/// the <c>instance</c> and the <c>traceId</c> (WP1.10).
/// </summary>
/// <param name="Slug">The heading of the entry, which ends the <c>type</c> address.</param>
/// <param name="Status">The HTTP status code of the response.</param>
/// <param name="Title">The summary that is the same for every error of the type.</param>
public sealed record ProblemType(string Slug, int Status, string Title);

/// <summary>A refusal in the shape of a Problem Details body, before the API adds the members that only it knows.</summary>
/// <param name="Type">The entry of the error catalog.</param>
/// <param name="Detail">An explanation of this occurrence for a person. It never holds a stack trace, a query, a secret, or a value that a caller sent.</param>
/// <param name="Errors">The messages for each invalid field. It is empty unless the type is <c>validation-failed</c>.</param>
public sealed record Problem(ProblemType Type, string Detail, IReadOnlyList<FieldError> Errors);
