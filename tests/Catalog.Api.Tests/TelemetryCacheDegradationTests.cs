using System.Diagnostics.Metrics;
using System.Net;
using AwesomeAssertions;
using Catalog.Api.Tests.Support;
using Catalog.CrossCutting.Caching;
using Catalog.Infrastructure.Tests.Support;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Catalog.Api.Tests;

/// <summary>
/// REQ-CACHE-003 and REQ-OBS-002: when Valkey is down the host keeps answering from memory and the database, the readiness probe stays green with a degraded cache, and the metrics say so, which is the signal that the
/// alerts of the Compose stack watch (plan sections 8.5, 8.9, and 8.11, ADR-0038, ADR-0043). Nobody runs Valkey for these tests, so every call to the shared cache fails, as it does when the server stops. The host starts
/// with a real SQL Server 2022 container. The gauge is read from the meter of the cache of this host, because a meter listener of the SDK sees the caches of every host of the process.
/// </summary>
[Collection(ExportedMetricsAlone.Name)]
public sealed class TelemetryCacheDegradationTests(CatalogDatabaseFixture database) : IClassFixture<CatalogDatabaseFixture>, IAsyncLifetime
{
    private TestIssuer _issuer = null!;

    public async ValueTask InitializeAsync() => _issuer = await TestIssuer.StartAsync();

    public async ValueTask DisposeAsync() => await _issuer.DisposeAsync();

    [Fact]
    [Trait("Requirement", "REQ-CACHE-003")]
    [Trait("Requirement", "REQ-OBS-002")]
    public async Task While_valkey_is_down_the_requests_succeed_the_metrics_report_the_degradation_and_readiness_stays_green()
    {
        await database.ResetAsync();
        var capture = new TelemetryCapture();
        await using var host = await ApiTestHost.StartAsync(new HostSettings(_issuer).WithDatabase(database.ConnectionString), telemetry: capture.AddTo);
        var metrics = host.Services.GetRequiredService<CacheMetrics>();
        using var listener = new MeterListener();
        var gauges = new Dictionary<string, int>();
        listener.InstrumentPublished = (instrument, subscriber) =>
        {
            if (ReferenceEquals(instrument.Meter.Scope, metrics) && instrument.Name.EndsWith(".down", StringComparison.Ordinal))
            {
                subscriber.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<int>((instrument, value, _, _) => gauges[instrument.Name] = value);
        listener.Start();

        var created = await host.Client.SendAsync(_issuer, HttpMethod.Post, "/api/v1/categories", ApiCalls.Admin, """{"name":"Keyboards"}""", ApiCalls.WithKey(ApiCalls.NewKey()));
        created.Status.Should().Be(HttpStatusCode.Created, "a write never fails because the shared cache cannot be reached");
        var product = await host.Client.SendAsync(_issuer, HttpMethod.Post, "/api/v1/products", ApiCalls.Editor, CatalogApiFixture.ProductJson(created.Id, "DOWN-1", "Keyboard", "89.99", "USD"), ApiCalls.WithKey(ApiCalls.NewKey()));
        var read = await host.Client.SendAsync(_issuer, HttpMethod.Get, product.Location!, ApiCalls.Reader);

        product.Status.Should().Be(HttpStatusCode.Created);
        read.Status.Should().Be(HttpStatusCode.OK, "a read never fails because of the cache");

        listener.RecordObservableInstruments();
        gauges["catalog.cache.shared.down"].Should().Be(1, "the circuit breaker of the shared cache is open");

        var exported = capture.FlushMetrics(host.Services);
        exported["catalog.cache.requests"].Should().Contain(series => series.Tags["degraded"] == "true" && series.Sum >= 1);
        exported["catalog.cache.invalidations"].Should().Contain(series => series.Tags["outcome"] == "incomplete" && series.Sum >= 1);
        exported.Keys.Should().Contain(["catalog.cache.shared.down", "catalog.cache.backplane.down"]);

        var readiness = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync(check => check.Tags.Contains("ready"), TestContext.Current.CancellationToken);
        readiness.Status.Should().NotBe(HealthStatus.Unhealthy, "readiness excludes Valkey, because the application degrades gracefully without it");
        readiness.Entries["cache"].Status.Should().Be(HealthStatus.Degraded);
        readiness.Entries["database"].Status.Should().Be(HealthStatus.Healthy);
    }
}
