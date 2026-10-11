using Microsoft.OpenApi;

namespace Catalog.Contract.Tests.Support;

/// <summary>One operation of the contract, flattened so that a test can read it without walking the model.</summary>
/// <param name="OperationId">The operation ID.</param>
/// <param name="Method">The HTTP method in upper case.</param>
/// <param name="Path">The path template.</param>
/// <param name="Operation">The operation itself.</param>
internal sealed record ContractOperation(string OperationId, string Method, string Path, OpenApiOperation Operation)
{
    /// <summary>The scopes that the operation requires from the OAuth 2.0 scheme.</summary>
    public IReadOnlyList<string> Scopes =>
        [.. Operation.Security?.SelectMany(requirement => requirement.Values.SelectMany(scopes => scopes)) ?? []];

    /// <summary>The status codes that the operation documents, as written in the contract.</summary>
    public IReadOnlyList<string> Statuses => Operation.Responses is null ? [] : [.. Operation.Responses.Keys];

    /// <summary>The names of the header parameters of the operation, with whether each one is required.</summary>
    public IReadOnlyDictionary<string, bool> HeaderParameters =>
        (Operation.Parameters ?? []).Where(parameter => parameter.In == ParameterLocation.Header).ToDictionary(parameter => parameter.Name!, parameter => parameter.Required);

    /// <summary>The names of the query parameters of the operation.</summary>
    public IReadOnlyList<string> QueryParameters =>
        [.. (Operation.Parameters ?? []).Where(parameter => parameter.In == ParameterLocation.Query).Select(parameter => parameter.Name!)];

    public static IReadOnlyList<ContractOperation> All { get; } =
    [
        .. Authored.Document.Paths.SelectMany(path => (path.Value.Operations ?? []).Select(operation =>
            new ContractOperation(operation.Value.OperationId!, Describe(operation.Key), path.Key, operation.Value)))
            .OrderBy(operation => operation.Path, StringComparer.Ordinal)
            .ThenBy(operation => operation.Method, StringComparer.Ordinal),
    ];

    public static ContractOperation Get(string operationId) =>
        All.Single(operation => operation.OperationId == operationId);

    private static string Describe(HttpMethod method) => method.Method.ToUpperInvariant();
}
