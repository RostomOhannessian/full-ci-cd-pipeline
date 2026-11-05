using System.Net;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Catalog.Api.Tests.Support;
using Xunit;

namespace Catalog.Api.Tests;

/// <summary>Builds the text of a snapshot from an answer: the status, the media type, the headers that matter, and the body. A snapshot never holds a token, a credential, or a clock value.</summary>
internal static class SnapshotShape
{
    public static JsonNode Of(ApiResponse response, params string[] headers)
    {
        var shown = new JsonObject();

        foreach (var name in headers)
        {
            if (response.Header(name) is { } value)
            {
                shown[name] = value;
            }
        }

        return new JsonObject
        {
            ["status"] = (int)response.Status,
            ["contentType"] = response.ContentType,
            ["headers"] = shown,
            ["body"] = response.Body.Length == 0 ? null : JsonNode.Parse(response.Body),
        };
    }
}

/// <summary>
/// The Problem Details bodies that the API makes by itself, on the real pipeline (REQ-API-005, ADR-0042). Each body is compared with a snapshot, so a change to a status, a type, a title, a detail, a header, or the shape of
/// the field errors shows in a review. The trace identifier is replaced, and the tests need no database.
/// </summary>
public sealed class ErrorSnapshotTests(SecuredApiFixture api) : IClassFixture<SecuredApiFixture>
{
    private static readonly string[] Shown = ["Retry-After", "WWW-Authenticate", "api-supported-versions"];

    private async Task<JsonNode> SendAsync(HttpMethod method, string path, string? token = null, Action<HttpRequestMessage>? configure = null)
    {
        using var response = await api.SendAsync(method, path, token, configure);
        var answer = new ApiResponse(response.StatusCode, response.Headers, response.Content.Headers.ContentType?.MediaType, await response.TextAsync());

        return SnapshotShape.Of(answer, Shown);
    }

    [Fact]
    [Trait("Requirement", "REQ-API-005")]
    public async Task The_errors_that_the_api_makes_by_itself()
    {
        var admin = api.Token(new TokenSpec { Scopes = ["catalog.read", "catalog.write", "catalog.admin"], Roles = ["catalog-admin"] });
        var key = ApiCalls.WithKey(ApiCalls.NewKey());

        var errors = new JsonObject
        {
            ["unauthorized"] = await SendAsync(HttpMethod.Get, "/api/v1/categories"),
            ["forbidden"] = await SendAsync(HttpMethod.Get, "/api/v1/categories", api.Token(new TokenSpec { Scopes = [], Roles = ["catalog-admin"] })),
            ["validation-failed"] = await SendAsync(HttpMethod.Get, "/api/v1/products?pageSize=0&sort=price&categoryId=nope", admin),
            ["malformed-request"] = await SendAsync(HttpMethod.Post, "/api/v1/categories", admin, request =>
            {
                key(request);
                request.Content = new StringContent("{\"name\":", System.Text.Encoding.UTF8, "application/json");
            }),
            ["unknown-member"] = await SendAsync(HttpMethod.Post, "/api/v1/categories", admin, request =>
            {
                key(request);
                request.Content = new StringContent("{\"name\":\"Garden\",\"color\":\"green\"}", System.Text.Encoding.UTF8, "application/json");
            }),
            ["idempotency-key-required"] = await SendAsync(HttpMethod.Post, "/api/v1/categories", admin, request => request.Content = new StringContent("{\"name\":\"Garden\"}", System.Text.Encoding.UTF8, "application/json")),
            ["payload-too-large"] = await SendAsync(HttpMethod.Post, "/api/v1/categories", admin, request =>
            {
                key(request);
                request.Content = new StringContent(new string('x', 32 * 1024 + 1), System.Text.Encoding.UTF8, "application/json");
            }),
            ["unsupported-media-type"] = await SendAsync(HttpMethod.Post, "/api/v1/categories", admin, request =>
            {
                key(request);
                request.Content = new StringContent("name=Garden", System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");
            }),
            ["unknown-route"] = await SendAsync(HttpMethod.Get, "/api/v1/nothing", admin),
            ["method-not-allowed"] = await SendAsync(HttpMethod.Delete, "/api/v1/categories", admin),
            ["internal-error"] = await SendAsync(HttpMethod.Get, "/test/boom"),
            ["service-unavailable"] = await SendAsync(HttpMethod.Get, "/test/database-down"),
        };

        await Snapshot.MatchAsync(errors.ToJsonString());
    }
}

/// <summary>The 429 of the limiter, with the header that tells a caller when to come back (REQ-API-005, REQ-API-007, ADR-0042).</summary>
public sealed class RateLimitSnapshotTests
{
    [Fact]
    [Trait("Requirement", "REQ-API-005")]
    [Trait("Requirement", "REQ-API-007")]
    public async Task The_answer_to_a_caller_that_sent_too_many_requests()
    {
        var issuer = await TestIssuer.StartAsync();

        try
        {
            await using var host = await ApiTestHost.StartAsync(new HostSettings(issuer).With("Catalog:Limits:ReadPermits", "1"));
            var token = ApiCalls.Reader;
            await host.Client.SendAsync(issuer, HttpMethod.Get, "/api/v1/categories/not-a-uuid", token);

            var limited = await host.Client.SendAsync(issuer, HttpMethod.Get, "/api/v1/categories/not-a-uuid", token);

            limited.Status.Should().Be(HttpStatusCode.TooManyRequests);
            await Snapshot.MatchAsync(SnapshotShape.Of(limited, "Retry-After", "api-supported-versions").ToJsonString());
        }
        finally
        {
            await issuer.DisposeAsync();
        }
    }
}

/// <summary>
/// The Problem Details bodies that come from the use cases, through the real pipeline over a real database (REQ-API-005, REQ-APP-004, ADR-0042): the nine typed errors that the application raises, mapped by the one table.
/// </summary>
public sealed class UseCaseErrorSnapshotTests(CatalogApiFixture api) : IClassFixture<CatalogApiFixture>
{
    private static readonly string[] Shown = ["Retry-After", "api-supported-versions"];

    [Fact]
    [Trait("Requirement", "REQ-API-005")]
    [Trait("Requirement", "REQ-APP-004")]
    public async Task The_errors_that_the_use_cases_raise()
    {
        await api.ResetAsync();
        var category = await api.CreateCategoryAsync("Keyboards");
        var product = await api.CreateProductAsync(category.Id, "KB-100");
        var retired = await api.CreateProductAsync(category.Id, "KB-200");
        var retiredPath = $"/api/v1/products/{retired.Id}";
        await api.SendAsync(HttpMethod.Post, $"{retiredPath}/discontinue", ApiCalls.Admin, null, ApiCalls.WithIfMatch(ApiCalls.Quote(retired.ETag!)));
        var current = await api.SendAsync(HttpMethod.Get, retiredPath, ApiCalls.Reader);
        var key = ApiCalls.NewKey();
        var body = CatalogApiFixture.ProductJson(category.Id, "KB-300", "Keyboard", "10", "USD");
        await api.SendAsync(HttpMethod.Post, "/api/v1/products", ApiCalls.Editor, body, ApiCalls.WithKey(key));

        var errors = new JsonObject
        {
            ["not-found"] = SnapshotShape.Of(await api.SendAsync(HttpMethod.Get, $"/api/v1/products/{Guid.CreateVersion7()}", ApiCalls.Reader), Shown),
            ["validation-failed"] = SnapshotShape.Of(await api.SendAsync(HttpMethod.Post, "/api/v1/products", ApiCalls.Editor, CatalogApiFixture.ProductJson(category.Id, "KB-1", "", "1.234", "USD"), ApiCalls.WithKey(ApiCalls.NewKey())), Shown),
            ["sku-already-exists"] = SnapshotShape.Of(await api.SendAsync(HttpMethod.Post, "/api/v1/products", ApiCalls.Editor, CatalogApiFixture.ProductJson(category.Id, "kb-100", "Other", "10", "USD"), ApiCalls.WithKey(ApiCalls.NewKey())), Shown),
            ["product-discontinued"] = SnapshotShape.Of(await api.SendAsync(HttpMethod.Put, retiredPath, ApiCalls.Editor, $$"""{"name":"Keyboard","price":{"amount":"1","currency":"USD"},"categoryId":"{{category.Id}}"}""", ApiCalls.WithIfMatch(ApiCalls.Quote(current.ETag!))), Shown),
            ["precondition-failed"] = SnapshotShape.Of(await api.SendAsync(HttpMethod.Post, $"/api/v1/products/{product.Id}/discontinue", ApiCalls.Admin, null, ApiCalls.WithIfMatch(ApiCalls.Quote("AAAAAAAAAAA"))), Shown),
            ["precondition-required"] = SnapshotShape.Of(await api.SendAsync(HttpMethod.Post, $"/api/v1/products/{product.Id}/discontinue", ApiCalls.Admin), Shown),
            ["idempotency-key-reused"] = SnapshotShape.Of(await api.SendAsync(HttpMethod.Post, "/api/v1/products", ApiCalls.Editor, CatalogApiFixture.ProductJson(category.Id, "KB-400", "Keyboard", "10", "USD"), ApiCalls.WithKey(key)), Shown),
        };

        await Snapshot.MatchAsync(errors.ToJsonString());
    }
}

/// <summary>
/// The part of the served document that the code generates (REQ-CON-002, ADR-0042): the paths, the methods, the operations, the scopes, the parameters, the bodies, and the responses of the ten endpoints. The snapshot shows a
/// change to an endpoint in a review, and the conformance check compares the whole document with the authored contract.
/// </summary>
public sealed class ServedPathsSnapshotTests(SecuredApiFixture api) : IClassFixture<SecuredApiFixture>
{
    [Fact]
    [Trait("Requirement", "REQ-CON-002")]
    public async Task The_paths_that_the_endpoints_generate()
    {
        using var response = await api.SendAsync(HttpMethod.Get, "/openapi/v1.json");
        var document = JsonNode.Parse(await response.TextAsync())!;

        await Snapshot.MatchAsync(document["paths"]!.ToJsonString());
    }
}
