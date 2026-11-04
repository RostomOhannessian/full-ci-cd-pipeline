using System.Net;
using AwesomeAssertions;
using Catalog.Api.Tests.Support;
using Xunit;

namespace Catalog.Api.Tests;

/// <summary>A host behind a proxy network, with the hosts that its public address may use.</summary>
public sealed class ProxiedApiFixture : SecuredApiFixture
{
    internal override HostSettings Customize(HostSettings settings) =>
        settings
            .With("Catalog:ForwardedHeaders:KnownNetworks", "10.0.0.0/8")
            .With("Catalog:Hosting:AllowedHosts", "localhost;api.example.test;*.edge.example.test");
}

/// <summary>
/// The API trusts a proxy only when a setting names its network or its address (plan section 9, ADR-0040). A request from a trusted network may tell the API the real address, scheme, and host of the caller, and a
/// request from anywhere else cannot, including the loopback address that ASP.NET Core trusts by default. The management and public hosts are checked in the same place.
/// </summary>
public sealed class ForwardedHeadersTests(ProxiedApiFixture api) : IClassFixture<ProxiedApiFixture>
{
    private async Task<System.Text.Json.JsonElement> RemoteAsync(string remote, Action<HttpRequestMessage>? configure = null)
    {
        using var response = await api.SendAsync(HttpMethod.Get, "/test/remote", configure: request =>
        {
            request.Headers.Add(RemoteAddressStartupFilter.Header, remote);
            configure?.Invoke(request);
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return await response.JsonAsync();
    }

    [Fact]
    [Trait("Requirement", "REQ-ARC-002")]
    public async Task A_proxy_in_a_known_network_may_tell_the_address_the_scheme_and_the_host()
    {
        var body = await RemoteAsync("10.1.2.3", request =>
        {
            request.Headers.Add("X-Forwarded-For", "203.0.113.7");
            request.Headers.Add("X-Forwarded-Proto", "https");
            request.Headers.Add("X-Forwarded-Host", "api.example.test");
        });

        body.GetProperty("address").GetString().Should().Be("203.0.113.7");
        body.GetProperty("scheme").GetString().Should().Be("https");
        body.GetProperty("host").GetString().Should().Be("api.example.test");
    }

    [Theory]
    [Trait("Requirement", "REQ-ARC-002")]
    [InlineData("192.0.2.9")]
    [InlineData("127.0.0.1")]
    [InlineData("11.0.0.1")]
    public async Task A_caller_outside_the_known_networks_cannot_spoof_the_address_the_scheme_or_the_host(string remote)
    {
        var body = await RemoteAsync(remote, request =>
        {
            request.Headers.Add("X-Forwarded-For", "203.0.113.7");
            request.Headers.Add("X-Forwarded-Proto", "https");
            request.Headers.Add("X-Forwarded-Host", "api.example.test");
        });

        body.GetProperty("address").GetString().Should().Be(remote);
        body.GetProperty("scheme").GetString().Should().Be("http");
        body.GetProperty("host").GetString().Should().Be("localhost");
    }

    [Fact]
    [Trait("Requirement", "REQ-ARC-002")]
    public async Task Transport_security_is_sent_when_a_trusted_proxy_says_the_caller_used_https()
    {
        using var response = await api.SendAsync(HttpMethod.Get, "/test/remote", configure: request =>
        {
            request.Headers.Add(RemoteAddressStartupFilter.Header, "10.0.0.5");
            request.Headers.Add("X-Forwarded-Proto", "https");
        });

        response.Headers.GetValues("Strict-Transport-Security").Should().Equal("max-age=31536000; includeSubDomains");
    }

    [Fact]
    [Trait("Requirement", "REQ-ARC-002")]
    public async Task A_forwarded_host_that_is_not_allowed_is_refused_even_from_a_trusted_proxy()
    {
        using var response = await api.SendAsync(HttpMethod.Get, "/test/remote", configure: request =>
        {
            request.Headers.Add(RemoteAddressStartupFilter.Header, "10.0.0.5");
            request.Headers.Add("X-Forwarded-Host", "evil.example.org");
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [Trait("Requirement", "REQ-ARC-002")]
    [InlineData("localhost", HttpStatusCode.OK)]
    [InlineData("LOCALHOST", HttpStatusCode.OK)]
    [InlineData("api.example.test", HttpStatusCode.OK)]
    [InlineData("eu.edge.example.test", HttpStatusCode.OK)]
    [InlineData("edge.example.test", HttpStatusCode.BadRequest)]
    [InlineData("example.test", HttpStatusCode.BadRequest)]
    [InlineData("evil.example.org", HttpStatusCode.BadRequest)]
    [InlineData("api.example.test.evil.org", HttpStatusCode.BadRequest)]
    public async Task A_request_to_the_public_port_must_name_an_allowed_host(string host, HttpStatusCode expected)
    {
        using var response = await api.SendAsync(HttpMethod.Get, "/test/remote", configure: request => request.Headers.Host = host);

        response.StatusCode.Should().Be(expected);

        if (expected == HttpStatusCode.BadRequest)
        {
            (await response.TextAsync()).Should().Contain("#malformed-request").And.NotContain(host);
        }
    }
}
