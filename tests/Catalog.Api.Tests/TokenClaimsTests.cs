using System.Security.Claims;
using System.Text.Json;
using AwesomeAssertions;
using Catalog.Api.Security;
using Xunit;

namespace Catalog.Api.Tests;

/// <summary>
/// The claims that the API reads from a validated token: the space-delimited scope, the realm roles of Keycloak, and the caller (plan section 8.7, REQ-IDN-002, REQ-IDN-003). A claim that is missing or malformed gives an
/// empty set, so a token never grants more than it says.
/// </summary>
public sealed class TokenClaimsTests
{
    private static readonly string[] EditorRole = ["catalog-editor"];

    private static ClaimsPrincipal Authenticated(params Claim[] claims) => new(new ClaimsIdentity(claims, "test"));

    private static Claim Roles(string json) => new(TokenClaims.RealmAccess, json, "JSON");

    [Fact]
    [Trait("Requirement", "REQ-IDN-002")]
    public void The_scopes_are_read_from_the_space_delimited_claim()
    {
        var user = Authenticated(new Claim("scope", "catalog.read  catalog.write catalog.admin "));

        TokenClaims.Scopes(user).Should().BeEquivalentTo(["catalog.read", "catalog.write", "catalog.admin"]);
    }

    [Fact]
    [Trait("Requirement", "REQ-IDN-002")]
    public void A_missing_scope_claim_gives_no_scope_and_a_scope_is_compared_exactly()
    {
        TokenClaims.Scopes(Authenticated()).Should().BeEmpty();
        TokenClaims.Scopes(Authenticated(new Claim("scope", "Catalog.Read catalog.read.extra"))).Should().NotContain("catalog.read");
    }

    [Fact]
    [Trait("Requirement", "REQ-IDN-002")]
    public void The_realm_roles_are_read_from_the_realm_access_object()
    {
        var user = Authenticated(Roles("""{"roles":["catalog-reader","catalog-editor","offline_access"]}"""));

        TokenClaims.Roles(user).Should().BeEquivalentTo(["catalog-reader", "catalog-editor", "offline_access"]);
    }

    [Theory]
    [Trait("Requirement", "REQ-IDN-002")]
    [InlineData("not json")]
    [InlineData("""["catalog-admin"]""")]
    [InlineData("""{"roles":"catalog-admin"}""")]
    [InlineData("""{"other":["catalog-admin"]}""")]
    [InlineData("""{"roles":[1,null,{"x":"catalog-admin"}]}""")]
    [InlineData("")]
    public void A_realm_access_claim_in_another_shape_grants_no_role(string value)
    {
        TokenClaims.Roles(Authenticated(Roles(value))).Should().BeEmpty();
    }

    [Fact]
    [Trait("Requirement", "REQ-IDN-002")]
    public void A_flat_roles_claim_is_not_a_source_of_roles()
    {
        // Only the realm_access object of the catalog realm counts, so a claim that another provider or a client could set grants nothing.
        TokenClaims.Roles(Authenticated(new Claim("roles", "catalog-admin"), new Claim("role", "catalog-admin"))).Should().BeEmpty();
    }

    [Fact]
    [Trait("Requirement", "REQ-IDN-002")]
    public void The_caller_is_the_subject_and_falls_back_to_the_authorized_party()
    {
        TokenClaims.CallerId(Authenticated(new Claim("sub", "user-1"), new Claim("azp", "catalog-cli"))).Should().Be("user-1");
        TokenClaims.CallerId(Authenticated(new Claim("azp", "smoke-tests"))).Should().Be("smoke-tests");
        TokenClaims.CallerId(Authenticated()).Should().BeEmpty();
    }

    [Fact]
    [Trait("Requirement", "REQ-IDN-002")]
    public void A_principal_that_is_not_authenticated_has_no_caller_scope_or_role_even_if_a_claim_is_present()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "user-1"), new Claim("scope", "catalog.admin"), Roles("""{"roles":["catalog-admin"]}""")]));
        var principal = new ClaimsCurrentPrincipal(anonymous);

        principal.IsAuthenticated.Should().BeFalse();
        principal.CallerId.Should().BeEmpty();
        principal.Scopes.Should().BeEmpty();
        principal.Roles.Should().BeEmpty();
    }

    [Fact]
    [Trait("Requirement", "REQ-IDN-002")]
    public void An_authenticated_principal_exposes_its_caller_scopes_and_roles()
    {
        var principal = new ClaimsCurrentPrincipal(Authenticated(
            new Claim("sub", "user-1"),
            new Claim("scope", "catalog.read catalog.write"),
            Roles(JsonSerializer.Serialize(new { roles = EditorRole }))));

        principal.IsAuthenticated.Should().BeTrue();
        principal.CallerId.Should().Be("user-1");
        principal.Scopes.Should().BeEquivalentTo(["catalog.read", "catalog.write"]);
        principal.Roles.Should().BeEquivalentTo(["catalog-editor"]);
    }
}
