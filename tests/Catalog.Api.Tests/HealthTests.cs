using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Catalog.Api.Tests.Support;
using Catalog.Application.Caching;
using Catalog.Application.Products;
using Catalog.Application.Results;
using Catalog.Domain.Common;
using Catalog.Infrastructure.Persistence;
using Catalog.Infrastructure.Tests.Support;
using Xunit;

namespace Catalog.Api.Tests;

/// <summary>
/// The health endpoints of plan section 8.9, served on the management port and never on the public one (ADR-0040). Liveness checks nothing, startup checks that the credential is loaded, and readiness checks the
/// database, the outbox, and the cache. A degraded cache or outbox is a 200 that says so, only an unhealthy check is a 503, and no answer and no log line holds a credential or the text of an exception. These tests run
/// the host on real sockets, because only a socket knows which port a request came in on, and they read a real SQL Server 2022 container (REQ-QUA-001).
/// </summary>
public sealed class HealthTests(CatalogDatabaseFixture database, SqlServerFixture server) : IClassFixture<CatalogDatabaseFixture>, IAsyncLifetime
{
    private TestIssuer _issuer = null!;

    public async ValueTask InitializeAsync() => _issuer = await TestIssuer.StartAsync();

    public async ValueTask DisposeAsync() => await _issuer.DisposeAsync();

    private static Task<ApiTestHost> StartAsync(HostSettings settings) => ApiTestHost.StartAsync(settings, TestEndpoints.Map, sockets: true);

    private HostSettings Healthy() => new HostSettings(_issuer).WithDatabase(database.ConnectionString);

    private static async Task<(HttpStatusCode Status, JsonElement Body, string Text)> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        return (response.StatusCode, JsonDocument.Parse(text).RootElement.Clone(), text);
    }

    private static string StatusOf(JsonElement report, string check) =>
        report.GetProperty("checks").EnumerateArray().Single(entry => entry.GetProperty("name").GetString() == check).GetProperty("status").GetString()!;

    [Fact]
    [Trait("Requirement", "REQ-QUA-001")]
    [Trait("Requirement", "REQ-CACHE-003")]
    public async Task Readiness_is_healthy_when_the_database_answers_with_a_current_schema_and_lists_every_check()
    {
        await using var host = await StartAsync(Healthy());
        using var client = host.ManagementClient;

        var (status, body, text) = await GetAsync(client, "/health/ready");

        status.Should().Be(HttpStatusCode.OK);
        body.GetProperty("status").GetString().Should().Be("Healthy");
        body.GetProperty("checks").EnumerateArray().Select(entry => entry.GetProperty("name").GetString()).Should().BeEquivalentTo("credentials", "database", "outbox", "cache");
        StatusOf(body, "database").Should().Be("Healthy");
        text.Should().NotContain(HostSettings.DatabasePassword).And.NotContain(HostSettings.ValkeyPassword);
    }

    [Fact]
    [Trait("Requirement", "REQ-ARC-002")]
    public async Task Liveness_checks_nothing_so_it_answers_even_when_the_database_is_down()
    {
        await using var host = await StartAsync(new HostSettings(_issuer));
        using var client = host.ManagementClient;

        var (status, body, _) = await GetAsync(client, "/health/live");

        status.Should().Be(HttpStatusCode.OK);
        body.GetProperty("status").GetString().Should().Be("Healthy");
        body.GetProperty("checks").GetArrayLength().Should().Be(0);
    }

    [Fact]
    [Trait("Requirement", "REQ-ARC-002")]
    public async Task Startup_checks_that_the_credential_is_loaded_and_never_names_it()
    {
        await using var host = await StartAsync(new HostSettings(_issuer));
        using var client = host.ManagementClient;

        var (status, body, text) = await GetAsync(client, "/health/startup");

        status.Should().Be(HttpStatusCode.OK);
        body.GetProperty("checks").EnumerateArray().Select(entry => entry.GetProperty("name").GetString()).Should().Equal("credentials");
        text.Should().NotContain(HostSettings.DatabasePassword).And.NotContain("\"sa\"");
    }

    [Fact]
    [Trait("Requirement", "REQ-QUA-001")]
    public async Task Readiness_is_unhealthy_when_the_database_cannot_be_reached_and_says_nothing_about_the_server_or_the_login()
    {
        await using var host = await StartAsync(new HostSettings(_issuer));
        using var client = host.ManagementClient;

        var (status, body, text) = await GetAsync(client, "/health/ready");

        status.Should().Be(HttpStatusCode.ServiceUnavailable);
        body.GetProperty("status").GetString().Should().Be("Unhealthy");
        StatusOf(body, "database").Should().Be("Unhealthy");
        StatusOf(body, "outbox").Should().Be("Unhealthy");
        text.Should().NotContain(HostSettings.DatabasePassword).And.NotContain("127.0.0.1").And.NotContain("SqlException");
        host.Log.Lines.Should().NotContain(line => line.Contains(HostSettings.DatabasePassword, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Requirement", "REQ-QUA-001")]
    public async Task Readiness_is_unhealthy_when_the_schema_is_behind_the_code()
    {
        var (name, connectionString) = await server.CreateDatabaseAsync("empty");

        try
        {
            await using var host = await StartAsync(new HostSettings(_issuer).WithDatabase(connectionString));
            using var client = host.ManagementClient;

            var (status, body, text) = await GetAsync(client, "/health/ready");

            status.Should().Be(HttpStatusCode.ServiceUnavailable);
            StatusOf(body, "database").Should().Be("Unhealthy");
            text.Should().Contain("The schema is behind the code");
        }
        finally
        {
            await server.DropDatabaseAsync(name);
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-QUA-001")]
    public async Task A_dead_letter_message_makes_readiness_degraded_and_the_probe_stays_green()
    {
        await using (var context = database.NewContext())
        {
            context.OutboxMessages.Add(new OutboxMessage
            {
                Id = Guid.CreateVersion7(),
                EventType = "ProductCreated",
                SchemaVersion = 1,
                OccurredAt = DateTimeOffset.UtcNow,
                Payload = "{}",
                Status = OutboxStatus.DeadLetter,
                Attempts = 8,
                CreatedAt = DateTimeOffset.UtcNow,
                NextAttemptAt = DateTimeOffset.UtcNow,
            });
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        try
        {
            await using var host = await StartAsync(Healthy());
            using var client = host.ManagementClient;

            var (status, body, _) = await GetAsync(client, "/health/ready");

            status.Should().Be(HttpStatusCode.OK);
            body.GetProperty("status").GetString().Should().Be("Degraded");
            StatusOf(body, "outbox").Should().Be("Degraded");
            StatusOf(body, "database").Should().Be("Healthy");
        }
        finally
        {
            await database.ResetOutboxAsync();
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-003")]
    public async Task A_degraded_cache_is_reported_and_readiness_stays_green()
    {
        await using var host = await StartAsync(Healthy());
        using var client = host.ManagementClient;
        var cache = host.Services.GetRequiredService<ICatalogCache>();
        var view = new ProductView(Guid.CreateVersion7(), "KB-100", "Keyboard", null, 89.90m, "USD", Guid.CreateVersion7(), LifecycleStatus.Active, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "AAAAAAAAB9E");

        // The Valkey server of this host does not exist, so the first call that reaches for it opens the circuit breaker, and the read is still answered from the loader.
        var read = await cache.GetOrLoadAsync(new CacheKey($"product:{view.Id:D}", [$"product:{view.Id:D}"]), _ => Task.FromResult(Result.Success(view)), TestContext.Current.CancellationToken);

        read.IsSuccess.Should().BeTrue();

        await Poll.UntilAsync(
            async () => StatusOf((await GetAsync(client, "/health/ready")).Body, "cache") == "Degraded",
            "the readiness check reports the cache as degraded after the shared cache failed",
            TestContext.Current.CancellationToken);

        var (status, body, text) = await GetAsync(client, "/health/ready");

        status.Should().Be(HttpStatusCode.OK);
        body.GetProperty("status").GetString().Should().Be("Degraded");
        StatusOf(body, "database").Should().Be("Healthy");
        text.Should().NotContain(HostSettings.ValkeyPassword);
    }

    [Fact]
    [Trait("Requirement", "REQ-ARC-002")]
    public async Task The_health_endpoints_exist_on_the_management_port_only_and_a_caller_cannot_reach_them_by_naming_that_port_in_the_host_header()
    {
        await using var host = await StartAsync(new HostSettings(_issuer));
        using var publicClient = host.PublicClient;

        using var plain = await publicClient.GetAsync("/health/live", TestContext.Current.CancellationToken);
        using var spoofed = await publicClient.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/health/live") { Headers = { Host = $"localhost:{host.ManagementPort}" } },
            TestContext.Current.CancellationToken);

        plain.StatusCode.Should().Be(HttpStatusCode.NotFound);
        spoofed.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Requirement", "REQ-ARC-002")]
    public async Task The_api_is_not_served_on_the_management_port()
    {
        var issuerToken = _issuer.Token(new TokenSpec());
        await using var host = await StartAsync(new HostSettings(_issuer));
        using var managementClient = host.ManagementClient;
        using var publicClient = host.PublicClient;
        using var request = new HttpRequestMessage(HttpMethod.Get, "/test/read");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", issuerToken);
        using var sameRequestPublic = new HttpRequestMessage(HttpMethod.Get, "/test/read");
        sameRequestPublic.Headers.Authorization = request.Headers.Authorization;

        using var onManagement = await managementClient.SendAsync(request, TestContext.Current.CancellationToken);
        using var onPublic = await publicClient.SendAsync(sameRequestPublic, TestContext.Current.CancellationToken);

        onManagement.StatusCode.Should().Be(HttpStatusCode.NotFound);
        onPublic.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Requirement", "REQ-ARC-002")]
    public async Task The_management_port_is_not_rate_limited_and_does_not_check_the_host()
    {
        await using var host = await StartAsync(new HostSettings(_issuer).With("Catalog:Limits:ReadPermits", "1"));
        using var client = host.ManagementClient;

        for (var probe = 0; probe < 5; probe++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live") { Headers = { Host = "10.1.2.3:8081" } };
            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.OK, $"probe {probe + 1} must be answered");
            response.Headers.Contains("X-Content-Type-Options").Should().BeTrue();
        }
    }
}
