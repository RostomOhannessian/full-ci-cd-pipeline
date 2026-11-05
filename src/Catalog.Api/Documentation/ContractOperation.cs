namespace Catalog.Api.Documentation;

/// <summary>
/// What an endpoint tells the generator of the served document about its operation (plan section 8.6, REQ-CON-002, ADR-0042). The endpoint table gives the path, the method, the operation identifier, and the scope, so
/// those are never written twice. This record holds the rest, which is the shape of the request and the response in the words of the contract: the names of the shared parameters, the schemas, and the headers. The
/// error responses follow from these facts by the rules of the style guide, so an operation lists its refusals only through what it accepts: a path identifier is a 404, a precondition header is a 412 and a 428, a body
/// is a 413 and a 415, and an idempotency key is a 422 and a 409.
/// </summary>
/// <param name="Tag">The tag of the operation, <c>Categories</c> or <c>Products</c>.</param>
/// <param name="ResponseSchema">The name of the schema of the success response, such as <c>Category</c> or <c>ProductPage</c>.</param>
/// <param name="SuccessStatus">The status of the success response: 200, or 201 for a create.</param>
/// <param name="PathParameter">The name of the shared parameter of the path identifier, such as <c>CategoryId</c>, or null for a collection.</param>
/// <param name="Parameters">The names of the shared parameters of the query string and the headers, such as <c>IfMatch</c> or <c>PageSize</c>.</param>
/// <param name="RequestSchema">The name of the schema of the body, or null when the operation has no body.</param>
/// <param name="ConflictResponse">The name of the shared 409 response of the operation, or null when it has none besides the one of an idempotent create.</param>
internal sealed record ContractOperation(
    string Tag,
    string ResponseSchema,
    int SuccessStatus = StatusCodes.Status200OK,
    string? PathParameter = null,
    IReadOnlyList<string>? Parameters = null,
    string? RequestSchema = null,
    string? ConflictResponse = null)
{
    /// <summary>Gets the shared parameters of the operation, or an empty list.</summary>
    public IReadOnlyList<string> OperationParameters => Parameters ?? [];
}

/// <summary>Attaches a <see cref="ContractOperation"/> to an endpoint.</summary>
internal static class ContractOperationExtensions
{
    /// <summary>Declares the contract of the operation that the endpoint serves.</summary>
    /// <param name="builder">The endpoint.</param>
    /// <param name="operation">The shape of the operation.</param>
    public static RouteHandlerBuilder WithContract(this RouteHandlerBuilder builder, ContractOperation operation)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithMetadata(operation);
    }
}
