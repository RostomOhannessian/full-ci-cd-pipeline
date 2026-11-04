using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;

namespace Catalog.Api.Documentation;

/// <summary>
/// Generates the served OpenAPI document of the version 1 contract (plan section 8.6, REQ-CON-002, ADR-0042). The document is compared with the authored contract by oasdiff, so it must say what the running endpoints do.
/// The endpoint table gives the paths, the methods, the operation identifiers, and the scopes, and each <see cref="ContractOperation"/> gives the shape of the request and the response, from which the error responses follow
/// by the rules of the style guide. What the contract owns outright, which is the information, the servers, the tags, the security that applies to every operation, and the shared components (the schemas with their
/// constraints, the parameters, the headers, and the responses), comes from the embedded copy of the authored file, because the code has no other statement of the length of a name or the pattern of a SKU.
/// A test compares the transport records with those schemas, so the code and the schemas cannot drift apart.
/// </summary>
internal static class ServedContract
{
    private const string ResourceName = "catalog.openapi.yaml";

    private static readonly Lazy<Task<JsonObject>> Authored = new(LoadAuthoredAsync);

    private static readonly string[] VersionHeaders = ["api-supported-versions", "api-deprecated-versions", "Deprecation", "Sunset"];

    /// <summary>Builds the document from the endpoints of the host.</summary>
    /// <param name="endpoints">The endpoints of the host, of which only those that declare a <see cref="ContractOperation"/> are operations of the contract.</param>
    public static async Task<string> BuildAsync(IEnumerable<Endpoint> endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var document = JsonNode.Parse((await Authored.Value).ToJsonString())!.AsObject();
        document["paths"] = Paths(endpoints);

        return document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static async Task<JsonObject> LoadAuthoredAsync()
    {
        await using var stream = typeof(ServedContract).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The authored contract is not embedded as {ResourceName}.");
        using var reader = new StreamReader(stream);

        var text = await reader.ReadToEndAsync();
        var settings = new OpenApiReaderSettings();
        settings.AddYamlReader();

        var result = OpenApiDocument.Parse(text, "yaml", settings);
        var parsed = result.Document ?? throw new InvalidOperationException("The embedded contract did not load as an OpenAPI document.");
        var document = JsonNode.Parse(await parsed.SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_1))!.AsObject();

        // The library writes the newest patch of the minor version, and the contract names the version that it was authored in, so the document says what the file says.
        document["openapi"] = DeclaredVersion(text);

        return document;
    }

    private static string DeclaredVersion(string yaml)
    {
        const string key = "openapi:";
        var line = yaml.ReplaceLineEndings("\n").Split('\n').First(candidate => candidate.StartsWith(key, StringComparison.Ordinal));

        return line[key.Length..].Trim().Trim('\'', '"');
    }

    private static JsonObject Paths(IEnumerable<Endpoint> endpoints)
    {
        var paths = new JsonObject();

        foreach (var endpoint in endpoints.OfType<RouteEndpoint>().Where(candidate => candidate.Metadata.GetMetadata<ContractOperation>() is not null).OrderBy(candidate => candidate.RoutePattern.RawText, StringComparer.Ordinal))
        {
            var contract = endpoint.Metadata.GetMetadata<ContractOperation>()!;
            var path = PathOf(endpoint.RoutePattern);
            var method = endpoint.Metadata.GetRequiredMetadata<HttpMethodMetadata>().HttpMethods.Single().ToLowerInvariant();
            var item = paths[path]?.AsObject() ?? NewPathItem(paths, path, contract);

            item[method] = Operation(endpoint, contract);
        }

        return paths;
    }

    // The route of the group names the version as a parameter, and the contract names it as the segment of the version that the document describes. An empty route of a group leaves a slash at the end.
    private static string PathOf(RoutePattern pattern) => pattern.RawText!.Replace("v{version:apiVersion}", "v1", StringComparison.Ordinal).TrimEnd('/');

    private static JsonObject NewPathItem(JsonObject paths, string path, ContractOperation contract)
    {
        var item = new JsonObject();

        if (contract.PathParameter is { } parameter)
        {
            item["parameters"] = new JsonArray(Ref("parameters", parameter));
        }

        paths[path] = item;

        return item;
    }

    private static JsonObject Operation(RouteEndpoint endpoint, ContractOperation contract)
    {
        var scope = endpoint.Metadata.GetMetadata<IAuthorizeData>()?.Policy ?? throw new InvalidOperationException($"The endpoint {endpoint.DisplayName} has no scope policy.");
        var parameters = contract.OperationParameters;
        var operation = new JsonObject
        {
            ["operationId"] = endpoint.Metadata.GetRequiredMetadata<EndpointNameMetadata>().EndpointName,
            ["tags"] = new JsonArray(contract.Tag),
            ["security"] = new JsonArray(new JsonObject { ["oauth2"] = new JsonArray(scope) }),
        };

        if (parameters.Count > 0)
        {
            operation["parameters"] = new JsonArray([.. parameters.Select(name => (JsonNode)Ref("parameters", name))]);
        }

        if (contract.RequestSchema is { } body)
        {
            operation["requestBody"] = new JsonObject { ["required"] = true, ["content"] = Json(body) };
        }

        operation["responses"] = Responses(contract, parameters);

        return operation;
    }

    private static JsonObject Responses(ContractOperation contract, IReadOnlyList<string> parameters)
    {
        var idempotent = parameters.Contains("IdempotencyKey");
        var preconditioned = parameters.Contains("IfMatch");
        var conditionalRead = parameters.Contains("IfNoneMatch");
        var page = contract.ResponseSchema.EndsWith("Page", StringComparison.Ordinal);
        var responses = new JsonObject { [Code(contract.SuccessStatus)] = Success(contract, page) };

        if (conditionalRead)
        {
            responses[Code(StatusCodes.Status304NotModified)] = new JsonObject { ["description"] = "Not modified.", ["headers"] = Headers(["ETag"]) };
        }

        responses[Code(StatusCodes.Status400BadRequest)] = Ref("responses", idempotent ? "BadRequestIdempotent" : "BadRequest");
        responses[Code(StatusCodes.Status401Unauthorized)] = Ref("responses", "Unauthorized");
        responses[Code(StatusCodes.Status403Forbidden)] = Ref("responses", "Forbidden");

        if (contract.PathParameter is not null)
        {
            responses[Code(StatusCodes.Status404NotFound)] = Ref("responses", "NotFound");
        }

        if (idempotent)
        {
            responses[Code(StatusCodes.Status409Conflict)] = Ref("responses", contract.ConflictResponse ?? "IdempotencyInProgress");
        }
        else if (contract.ConflictResponse is { } conflict)
        {
            responses[Code(StatusCodes.Status409Conflict)] = Ref("responses", conflict);
        }

        if (preconditioned)
        {
            responses[Code(StatusCodes.Status412PreconditionFailed)] = Ref("responses", "PreconditionFailed");
        }

        if (contract.RequestSchema is not null)
        {
            responses[Code(StatusCodes.Status413PayloadTooLarge)] = Ref("responses", "PayloadTooLarge");
            responses[Code(StatusCodes.Status415UnsupportedMediaType)] = Ref("responses", "UnsupportedMediaType");
        }

        if (idempotent)
        {
            responses[Code(StatusCodes.Status422UnprocessableEntity)] = Ref("responses", "IdempotencyKeyReused");
        }

        if (preconditioned)
        {
            responses[Code(StatusCodes.Status428PreconditionRequired)] = Ref("responses", "PreconditionRequired");
        }

        responses[Code(StatusCodes.Status429TooManyRequests)] = Ref("responses", "TooManyRequests");
        responses[Code(StatusCodes.Status500InternalServerError)] = Ref("responses", "InternalServerError");
        responses[Code(StatusCodes.Status503ServiceUnavailable)] = Ref("responses", "ServiceUnavailable");

        return responses;
    }

    // A page has a Link header and no tag of its own, a created resource has a Location, and a single resource has a tag. Every success lists the versions.
    private static JsonObject Success(ContractOperation contract, bool page)
    {
        List<string> headers = [];

        if (contract.SuccessStatus == StatusCodes.Status201Created)
        {
            headers.Add("Location");
        }

        headers.Add(page ? "Link" : "ETag");

        return new JsonObject
        {
            ["description"] = "Success.",
            ["headers"] = Headers(headers),
            ["content"] = Json(contract.ResponseSchema),
        };
    }

    private static JsonObject Headers(IEnumerable<string> names)
    {
        var headers = new JsonObject();

        foreach (var name in names)
        {
            headers[name] = Ref("headers", name);
        }

        foreach (var name in VersionHeaders)
        {
            headers[name] = Ref("headers", ComponentName(name));
        }

        return headers;
    }

    private static string ComponentName(string header) => header switch
    {
        "api-supported-versions" => "ApiSupportedVersions",
        "api-deprecated-versions" => "ApiDeprecatedVersions",
        _ => header,
    };

    private static JsonObject Json(string schema) => new() { ["application/json"] = new JsonObject { ["schema"] = Ref("schemas", schema) } };

    private static JsonObject Ref(string section, string name) => new() { ["$ref"] = $"#/components/{section}/{name}" };

    private static string Code(int status) => status.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
