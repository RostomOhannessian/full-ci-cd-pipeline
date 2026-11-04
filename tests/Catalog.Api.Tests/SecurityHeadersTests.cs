using System.Net;
using AwesomeAssertions;
using Catalog.Api.Tests.Support;
using Xunit;

namespace Catalog.Api.Tests;

/// <summary>
/// The security headers are on every answer (plan section 9, ADR-0040): a success, an error from an endpoint, an error from a middleware, and a refusal before the endpoint runs. The API serves JSON to programs, so
/// no content can be loaded or framed, the type of the content is fixed, nothing is stored by a shared cache, and the referrer is never sent.
/// </summary>
public sealed class SecurityHeadersTests(SecuredApiFixture api) : IClassFixture<SecuredApiFixture>
{
    private static readonly (string Name, string Value)[] Expected =
    [
        ("X-Content-Type-Options", "nosniff"),
        ("X-Frame-Options", "DENY"),
        ("Content-Security-Policy", "default-src 'none'; frame-ancestors 'none'"),
        ("Referrer-Policy", "no-referrer"),
        ("Cross-Origin-Resource-Policy", "same-origin"),
    ];

    private static void AssertHeaders(HttpResponseMessage response)
    {
        response.Headers.TryGetValues("Cache-Control", out var cacheControl).Should().BeTrue("the answer must carry Cache-Control");
        cacheControl!.Single().Should().Contain("no-store");
        foreach (var (name, value) in Expected)
        {
            response.Headers.TryGetValues(name, out var values).Should().BeTrue($"the answer must carry {name}");
            values.Should().Equal(value);
        }

        response.Headers.Should().NotContain(header => header.Key == "Server");
        response.Headers.Contains("Permissions-Policy").Should().BeTrue();
    }

    [Fact]
    [Trait("Requirement", "REQ-ARC-002")]
    public async Task A_success_carries_the_security_headers()
    {
        using var response = await api.SendAsync(HttpMethod.Get, "/test/read", api.Token());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        AssertHeaders(response);
    }

    [Theory]
    [Trait("Requirement", "REQ-ARC-002")]
    [InlineData("/test/read", false, HttpStatusCode.Unauthorized)]
    [InlineData("/test/admin", true, HttpStatusCode.MethodNotAllowed)]
    [InlineData("/nothing", true, HttpStatusCode.NotFound)]
    [InlineData("/test/boom", true, HttpStatusCode.InternalServerError)]
    [InlineData("/test/validation", true, HttpStatusCode.BadRequest)]
    public async Task Every_kind_of_error_carries_the_security_headers(string path, bool authenticated, HttpStatusCode status)
    {
        using var response = await api.SendAsync(HttpMethod.Get, path, authenticated ? api.Token() : null);

        response.StatusCode.Should().Be(status);
        AssertHeaders(response);
    }

    [Fact]
    [Trait("Requirement", "REQ-ARC-002")]
    public async Task A_refusal_by_the_policy_carries_the_security_headers()
    {
        using var response = await api.SendAsync(HttpMethod.Post, "/test/admin", api.Token());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        AssertHeaders(response);
    }

    [Fact]
    [Trait("Requirement", "REQ-ARC-002")]
    public async Task Transport_security_is_not_sent_over_plain_http()
    {
        using var response = await api.SendAsync(HttpMethod.Get, "/test/remote");

        response.Headers.Contains("Strict-Transport-Security").Should().BeFalse();
    }
}
