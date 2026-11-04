using System.Net;
using AwesomeAssertions;
using Catalog.Api.Tests.Support;
using Xunit;

namespace Catalog.Api.Tests;

/// <summary>A host whose limits are small enough to reach in a test: three reads and one write for each caller in a window of one minute.</summary>
public sealed class RateLimitedApiFixture : SecuredApiFixture
{
    internal override HostSettings Customize(HostSettings settings) =>
        settings.With("Catalog:Limits:ReadPermits", "3").With("Catalog:Limits:WritePermits", "1").With("Catalog:Limits:Window", "00:01:00");
}

/// <summary>
/// A caller that exceeds its limit gets 429 with <c>Retry-After</c> (plan section 8.6, REQ-API-007). The count belongs to the caller that the token names, or to the address when there is no token, and a read and a
/// write are counted apart. The window is one minute, so a test never waits for it: it spends the permits and reads the refusal.
/// </summary>
public sealed class RateLimitTests(RateLimitedApiFixture api) : IClassFixture<RateLimitedApiFixture>
{
    private static string Caller(string name) => $"caller-{name}";

    private Task<HttpResponseMessage> ReadAsync(string caller) => api.SendAsync(HttpMethod.Get, "/test/read", api.Token(new TokenSpec { Subject = Caller(caller) }));

    [Fact]
    [Trait("Requirement", "REQ-API-007")]
    public async Task The_request_after_the_last_permit_is_answered_with_429_retry_after_and_a_problem()
    {
        for (var request = 1; request <= 3; request++)
        {
            using var allowed = await ReadAsync("spender");
            allowed.StatusCode.Should().Be(HttpStatusCode.OK, $"request {request} is inside the limit");
        }

        using var refused = await ReadAsync("spender");
        var problem = await refused.JsonAsync();

        refused.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        refused.Headers.RetryAfter!.Delta!.Value.TotalSeconds.Should().BeInRange(1, 60);
        refused.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        problem.ProblemSlug().Should().Be("rate-limited");
        problem.GetProperty("title").GetString().Should().Be("Too many requests.");
        problem.GetProperty("status").GetInt32().Should().Be(429);
        refused.Headers.Contains("X-Content-Type-Options").Should().BeTrue("a refusal by the limiter carries the security headers too");
    }

    [Fact]
    [Trait("Requirement", "REQ-API-007")]
    public async Task One_caller_spending_its_permits_does_not_limit_another()
    {
        for (var request = 0; request < 4; request++)
        {
            using var response = await ReadAsync("noisy");
        }

        using var neighbor = await ReadAsync("quiet");

        neighbor.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Requirement", "REQ-API-007")]
    public async Task A_write_has_its_own_lower_limit_and_a_read_still_works_after_it()
    {
        var token = api.Token(new TokenSpec { Subject = Caller("writer"), Scopes = ["catalog.read", "catalog.write"], Roles = ["catalog-editor"] });

        using var first = await api.SendAsync(HttpMethod.Post, "/test/write", token);
        using var second = await api.SendAsync(HttpMethod.Post, "/test/write", token);
        using var read = await api.SendAsync(HttpMethod.Get, "/test/read", token);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        read.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Requirement", "REQ-API-007")]
    public async Task A_caller_without_a_token_is_counted_by_its_address()
    {
        Task<HttpResponseMessage> FromAddress(string address) => api.SendAsync(HttpMethod.Get, "/test/remote", configure: request => request.Headers.Add(RemoteAddressStartupFilter.Header, address));

        for (var request = 0; request < 3; request++)
        {
            using var allowed = await FromAddress("203.0.113.10");
            allowed.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using var refused = await FromAddress("203.0.113.10");
        using var other = await FromAddress("203.0.113.11");

        refused.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        other.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Requirement", "REQ-API-007")]
    public async Task Bad_tokens_from_one_address_are_limited_so_a_flood_of_them_costs_the_server_little()
    {
        Task<HttpResponseMessage> Bad() => api.SendAsync(HttpMethod.Get, "/test/read", "not-a-token", request => request.Headers.Add(RemoteAddressStartupFilter.Header, "198.51.100.77"));

        for (var request = 0; request < 3; request++)
        {
            using var refused = await Bad();
            refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        using var limited = await Bad();

        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}
