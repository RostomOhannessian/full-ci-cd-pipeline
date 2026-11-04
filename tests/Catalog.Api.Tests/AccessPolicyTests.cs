using System.Net;
using AwesomeAssertions;
using Catalog.Api.Tests.Support;
using Xunit;

namespace Catalog.Api.Tests;

/// <summary>
/// The endpoint policies need a scope and a role (plan section 8.7, REQ-IDN-002). Keycloak 26.8.0 grants a requested scope to a user who lacks the mapped role (ADR-0026, risk R21), so a token with the scope alone, or
/// with the role alone, must not pass. The oracle is written out here, apart from the permission table of the application, so a change to the table fails a test instead of passing it.
/// </summary>
public sealed class AccessPolicyTests(SecuredApiFixture api) : IClassFixture<SecuredApiFixture>
{
    private static readonly string[] Scopes = ["catalog.read", "catalog.write", "catalog.admin"];
    private static readonly string[] Roles = ["catalog-reader", "catalog-editor", "catalog-admin"];

    // The scope of each level and the roles that grant it, as plan section 8.7 states them.
    private static readonly (string Path, HttpMethod Method, string Scope, string[] Granting)[] Levels =
    [
        ("/test/read", HttpMethod.Get, "catalog.read", ["catalog-reader", "catalog-editor", "catalog-admin"]),
        ("/test/write", HttpMethod.Post, "catalog.write", ["catalog-editor", "catalog-admin"]),
        ("/test/admin", HttpMethod.Post, "catalog.admin", ["catalog-admin"]),
    ];

    private static IEnumerable<string[]> Subsets(string[] values) =>
        Enumerable.Range(0, 1 << values.Length).Select(mask => values.Where((_, index) => (mask & (1 << index)) != 0).ToArray());

    [Fact]
    [Trait("Requirement", "REQ-IDN-002")]
    public async Task Every_combination_of_scopes_and_roles_gets_the_answer_that_the_matrix_gives_for_every_level()
    {
        var checkedCombinations = 0;

        foreach (var (path, method, scope, granting) in Levels)
        {
            foreach (var scopes in Subsets(Scopes))
            {
                foreach (var roles in Subsets(Roles))
                {
                    using var response = await api.SendAsync(method, path, api.Token(new TokenSpec { Scopes = scopes, Roles = roles }));
                    var allowed = scopes.Contains(scope) && roles.Any(granting.Contains);

                    response.StatusCode.Should().Be(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, $"{method} {path} with scopes [{string.Join(' ', scopes)}] and roles [{string.Join(' ', roles)}]");
                    checkedCombinations++;
                }
            }
        }

        checkedCombinations.Should().Be(3 * 8 * 8);
    }

    [Fact]
    [Trait("Requirement", "REQ-IDN-002")]
    public async Task A_token_with_the_scope_and_without_the_role_is_refused_and_the_answer_names_the_roles()
    {
        using var response = await api.SendAsync(HttpMethod.Post, "/test/admin", api.Token(new TokenSpec { Scopes = ["catalog.admin"], Roles = ["catalog-reader"] }));
        var problem = await response.JsonAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        problem.ProblemSlug().Should().Be("forbidden");
        problem.GetProperty("detail").GetString().Should().Contain("catalog.admin").And.Contain("catalog-admin");
    }

    [Fact]
    [Trait("Requirement", "REQ-IDN-002")]
    public async Task A_token_without_the_scope_is_refused_and_the_answer_names_the_scope()
    {
        using var response = await api.SendAsync(HttpMethod.Post, "/test/write", api.Token(new TokenSpec { Scopes = ["catalog.read"], Roles = ["catalog-admin"] }));
        var problem = await response.JsonAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        problem.GetProperty("detail").GetString().Should().Be("This operation requires the catalog.write scope.");
    }

    [Fact]
    [Trait("Requirement", "REQ-IDN-002")]
    public async Task A_role_does_not_replace_a_scope_and_a_scope_does_not_replace_a_role()
    {
        using var roleOnly = await api.SendAsync(HttpMethod.Get, "/test/read", api.Token(new TokenSpec { Scopes = [], Roles = ["catalog-admin"] }));
        using var scopeOnly = await api.SendAsync(HttpMethod.Get, "/test/read", api.Token(new TokenSpec { Scopes = ["catalog.read"], Roles = [] }));

        roleOnly.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        scopeOnly.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    [Trait("Requirement", "REQ-IDN-002")]
    public async Task A_scope_with_another_case_or_a_longer_name_does_not_count()
    {
        using var upper = await api.SendAsync(HttpMethod.Get, "/test/read", api.Token(new TokenSpec { Scopes = ["Catalog.Read"] }));
        using var longer = await api.SendAsync(HttpMethod.Get, "/test/read", api.Token(new TokenSpec { Scopes = ["catalog.read.all"] }));

        upper.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        longer.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
