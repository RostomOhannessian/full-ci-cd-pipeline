using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AwesomeAssertions;
using Catalog.Api.Tests.Support;
using Xunit;

namespace Catalog.Api.Tests;

/// <summary>
/// The catalog realm against a real Keycloak (REQ-IDN-001, REQ-IDN-005, REQ-QUA-001, plan section 8.7, ADR-0026, ADR-0042). The server is the pinned image of <c>compose.yaml</c>, and it imports the one realm file of the
/// repository with its placeholders filled from the environment. The tests read what the server puts in a token (the issuer, the audience, the scopes, and the roles), and then send the real tokens to the real API, so the
/// contract between the realm and the API is proven with the tokens that Keycloak signs and not with tokens that a test makes. Keycloak 26.8.0 grants a requested scope to a user who lacks the mapped role (risk R21),
/// so the API requires the scope and the role, and the clients of the realm cannot ask for more than their default scopes.
/// </summary>
public sealed class KeycloakRealmContractTests(KeycloakFixture keycloak) : IClassFixture<KeycloakFixture>
{
    private static readonly string[] AdminScopes = ["catalog.admin", "catalog.read", "catalog.write"];

    private static string[] Scopes(JsonElement claims) => [.. claims.GetProperty("scope").GetString()!.Split(' ', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal)];

    private static string[] Audiences(JsonElement claims) =>
        claims.GetProperty("aud").ValueKind == JsonValueKind.Array ? [.. claims.GetProperty("aud").EnumerateArray().Select(value => value.GetString()!)] : [claims.GetProperty("aud").GetString()!];

    private static string[] RealmRoles(JsonElement claims) => [.. claims.GetProperty("realm_access").GetProperty("roles").EnumerateArray().Select(value => value.GetString()!)];

    private HostSettings Settings(string issuer) => new HostSettings(issuer).With("Catalog:Auth:MetadataAddress", keycloak.MetadataAddress);

    private async Task<string> TokenAsync(string clientId, string secret)
    {
        var (status, body) = await keycloak.RequestTokenAsync(clientId, secret);

        status.Should().Be(HttpStatusCode.OK, body.ToString());

        return body.GetProperty("access_token").GetString()!;
    }

    private static async Task<HttpStatusCode> SendAsync(HttpClient client, HttpMethod method, string path, string? token)
    {
        using var request = new HttpRequestMessage(method, path);

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        return response.StatusCode;
    }

    [Fact]
    [Trait("Requirement", "REQ-IDN-005")]
    [Trait("Requirement", "REQ-QUA-001")]
    public async Task The_realm_file_imports_with_its_placeholders_and_the_server_advertises_the_fixed_issuer_and_the_allowed_algorithms()
    {
        using var client = new HttpClient();
        using var discovery = await client.GetAsync(keycloak.MetadataAddress, TestContext.Current.CancellationToken);
        var document = JsonDocument.Parse(await discovery.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;
        var algorithms = document.GetProperty("id_token_signing_alg_values_supported").EnumerateArray().Select(value => value.GetString()).ToList();

        document.GetProperty("issuer").GetString().Should().Be(keycloak.Issuer);
        algorithms.Should().Contain("RS256");

        using var keys = await client.GetAsync(document.GetProperty("jwks_uri").GetString(), TestContext.Current.CancellationToken);
        var published = JsonDocument.Parse(await keys.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement.GetProperty("keys").EnumerateArray().ToList();

        published.Should().NotBeEmpty();
        published.Should().Contain(key => key.GetProperty("use").GetString() == "sig" && key.GetProperty("alg").GetString() == "RS256");
    }

    [Fact]
    [Trait("Requirement", "REQ-IDN-001")]
    public async Task The_token_of_a_client_names_the_api_as_its_audience_and_carries_the_scopes_and_the_role_of_the_client()
    {
        var admin = KeycloakFixture.ClaimsOf(await TokenAsync("smoke-tests", keycloak.SmokeTestsSecret));
        var shopper = KeycloakFixture.ClaimsOf(await TokenAsync("synthetic-shopper", keycloak.ShopperSecret));

        admin.GetProperty("iss").GetString().Should().Be(keycloak.Issuer);
        Audiences(admin).Should().Contain("catalog-api");
        Scopes(admin).Should().Equal(AdminScopes);
        RealmRoles(admin).Should().Contain("catalog-admin");

        Audiences(shopper).Should().Contain("catalog-api");
        Scopes(shopper).Should().Equal("catalog.read");
        RealmRoles(shopper).Should().Contain("catalog-reader").And.NotContain("catalog-admin").And.NotContain("catalog-editor");
    }

    [Fact]
    [Trait("Requirement", "REQ-IDN-001")]
    public async Task A_client_cannot_ask_for_a_scope_that_the_realm_did_not_give_it()
    {
        var (status, body) = await keycloak.RequestTokenAsync("synthetic-shopper", keycloak.ShopperSecret, "catalog.read catalog.write catalog.admin");

        status.Should().Be(HttpStatusCode.BadRequest, body.ToString());
        body.GetProperty("error").GetString().Should().Be("invalid_scope");
    }

    [Fact]
    [Trait("Requirement", "REQ-IDN-001")]
    public async Task A_wrong_secret_gets_no_token()
    {
        var (status, _) = await keycloak.RequestTokenAsync("smoke-tests", "not-the-secret");

        status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Requirement", "REQ-IDN-001")]
    [Trait("Requirement", "REQ-IDN-002")]
    [Trait("Requirement", "REQ-IDN-003")]
    public async Task The_api_accepts_the_tokens_that_keycloak_signs_and_applies_the_scope_and_the_role_of_each()
    {
        var admin = await TokenAsync("smoke-tests", keycloak.SmokeTestsSecret);
        var shopper = await TokenAsync("synthetic-shopper", keycloak.ShopperSecret);
        await using var host = await ApiTestHost.StartAsync(Settings(keycloak.Issuer));

        // An allowed caller passes the policy and ends at a 400 that the API makes before it reads a database, and a caller without the scope gets a 403.
        (await SendAsync(host.Client, HttpMethod.Get, "/api/v1/categories?pageSize=abc", shopper)).Should().Be(HttpStatusCode.BadRequest);
        (await SendAsync(host.Client, HttpMethod.Post, "/api/v1/categories", shopper)).Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(host.Client, HttpMethod.Post, "/api/v1/categories", admin)).Should().Be(HttpStatusCode.BadRequest);
        (await SendAsync(host.Client, HttpMethod.Post, "/api/v1/products", admin)).Should().Be(HttpStatusCode.BadRequest);
        (await SendAsync(host.Client, HttpMethod.Get, "/api/v1/categories?pageSize=abc", null)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Requirement", "REQ-IDN-003")]
    public async Task The_api_refuses_a_token_of_keycloak_whose_signature_was_changed_and_a_token_for_another_issuer()
    {
        var admin = await TokenAsync("smoke-tests", keycloak.SmokeTestsSecret);
        var parts = admin.Split('.');
        var changed = $"{parts[0]}.{parts[1]}.{(parts[2][0] == 'A' ? 'B' : 'A')}{parts[2][1..]}";
        await using var host = await ApiTestHost.StartAsync(Settings(keycloak.Issuer));
        await using var other = await ApiTestHost.StartAsync(Settings($"{keycloak.Issuer}-other"));

        (await SendAsync(host.Client, HttpMethod.Get, "/api/v1/categories?pageSize=abc", changed)).Should().Be(HttpStatusCode.Unauthorized);
        (await SendAsync(other.Client, HttpMethod.Get, "/api/v1/categories?pageSize=abc", admin)).Should().Be(HttpStatusCode.Unauthorized);
    }
}

/// <summary>The realm file as it is stored, which needs no server (REQ-IDN-005).</summary>
public sealed class RealmFileTests
{
    [Fact]
    [Trait("Requirement", "REQ-IDN-005")]
    public void The_realm_file_holds_no_literal_secret_or_password()
    {
        var path = Path.Combine(Repository.Root, "identity", "keycloak", "realms", "catalog-realm.json");
        var realm = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
        List<string> literals = [];

        void Walk(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var member in element.EnumerateObject())
                    {
                        if (member.Name == "secret" && member.Value.ValueKind == JsonValueKind.String && !member.Value.GetString()!.StartsWith("${", StringComparison.Ordinal))
                        {
                            literals.Add(member.Name);
                        }

                        Walk(member.Value);
                    }

                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        Walk(item);
                    }

                    break;
            }
        }

        Walk(realm);

        literals.Should().BeEmpty("every secret of the realm is a placeholder that the environment fills");
        File.ReadAllText(path).Should().Contain("${CATALOG_SMOKE_TESTS_SECRET}").And.Contain("${CATALOG_SYNTHETIC_SHOPPER_SECRET}");
    }
}
