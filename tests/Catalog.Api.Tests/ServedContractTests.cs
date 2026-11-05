using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Catalog.Api.Tests.Support;
using Catalog.Application.Security;
using Xunit;

namespace Catalog.Api.Tests;

/// <summary>
/// The served OpenAPI document and its viewer (REQ-CON-002, ADR-0042). The document is generated from the endpoints of the running host, and it is written to <c>TestResults/served/catalog.openapi.json</c> so that the
/// conformance script of CI can compare it with the authored contract through oasdiff. The viewer exists in the Development environment only, and a Production host answers 404 for both. These tests need no database.
/// </summary>
public sealed class ServedContractTests(SecuredApiFixture api) : IClassFixture<SecuredApiFixture>
{
    private static readonly string[] Operations =
    [
        "createCategory", "createProduct", "discontinueCategory", "discontinueProduct", "getCategory", "getProduct", "listCategories", "listProducts", "updateCategory", "updateProduct",
    ];

    private async Task<(HttpStatusCode Status, string Body)> GetAsync(string path)
    {
        using var response = await api.SendAsync(HttpMethod.Get, path);

        return (response.StatusCode, await response.TextAsync());
    }

    [Fact]
    [Trait("Requirement", "REQ-CON-002")]
    public async Task The_served_document_names_the_ten_operations_with_the_scope_of_each_and_is_written_for_the_conformance_check()
    {
        var (status, body) = await GetAsync("/openapi/v1.json");

        status.Should().Be(HttpStatusCode.OK, body);

        var document = JsonDocument.Parse(body).RootElement;
        var found = document.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject().Where(member => member.Name is "get" or "post" or "put").Select(member => (Path: path.Name, Method: member.Name, Operation: member.Value)))
            .ToList();

        found.Select(operation => operation.Operation.GetProperty("operationId").GetString()).Order(StringComparer.Ordinal).Should().Equal(Operations);
        found.Select(operation => operation.Path).Should().OnlyContain(path => path.StartsWith("/api/v1/", StringComparison.Ordinal));

        // The scope comes from the policy of the endpoint, and the permission table says which scope each access level needs.
        foreach (var (_, _, operation) in found)
        {
            var scope = operation.GetProperty("security")[0].GetProperty("oauth2")[0].GetString();
            PermissionPolicy.Requirements.Select(requirement => requirement.Scope).Should().Contain(scope);
        }

        var path = Path.Combine(Repository.Root, "TestResults", "served", "catalog.openapi.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, body, TestContext.Current.CancellationToken);
    }

    [Fact]
    [Trait("Requirement", "REQ-CON-002")]
    public async Task The_viewer_is_served_in_development_with_a_policy_of_its_own_and_the_api_keeps_the_strict_one()
    {
        using var viewer = await api.SendAsync(HttpMethod.Get, "/scalar/v1");
        using var strict = await api.SendAsync(HttpMethod.Get, "/api/v1/categories");

        viewer.StatusCode.Should().Be(HttpStatusCode.OK, await viewer.TextAsync());
        viewer.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
        viewer.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("script-src 'self'");
        strict.Headers.GetValues("Content-Security-Policy").Single().Should().Be("default-src 'none'; frame-ancestors 'none'");
    }

    [Fact]
    [Trait("Requirement", "REQ-CON-002")]
    public async Task A_production_host_serves_neither_the_document_nor_the_viewer()
    {
        var issuer = await TestIssuer.StartAsync();

        try
        {
            var settings = new HostSettings(issuer)
                .With("Catalog:Auth:Issuer", "https://identity.example.test/realms/catalog")
                .With("Catalog:Auth:RequireHttpsMetadata", "true");

            await using var production = await ApiTestHost.StartAsync(settings, environment: "Production");

            foreach (var path in new[] { "/openapi/v1.json", "/scalar", "/scalar/v1", "/scalar/scalar.js" })
            {
                using var response = await production.Client.GetAsync(path, TestContext.Current.CancellationToken);

                response.StatusCode.Should().Be(HttpStatusCode.NotFound, $"{path} must not exist in production");
            }
        }
        finally
        {
            await issuer.DisposeAsync();
        }
    }
}
