using System.Net;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Catalog.Api.Tests.Support;
using Catalog.Infrastructure.Tests.Support;
using Xunit;

namespace Catalog.Api.Tests;

/// <summary>
/// REQ-OBS-002: the host exports the metrics that the plan names, which are the defaults of ASP.NET Core and of the runtime and the series of the catalog for the use cases, the cache, the outbox, and the idempotency
/// (plan section 8.11, ADR-0043). The labels of a series are fixed words and never an identifier, so a flood of different products makes no new series. The host is the real composition root over a real SQL Server 2022
/// and a real Valkey 8.1. A meter listener sees the meters of every host of the process, so these tests assert what holds for every host: the names, the labels, and the bounds, and the exact numbers are the work of the tests
/// of each component, which read the meter of their own instance.
/// </summary>
[Collection(ExportedMetricsAlone.Name)]
public sealed partial class TelemetryMetricsTests(CatalogApiFixture api) : IClassFixture<CatalogApiFixture>
{
    // The words that a label of an exported series may be named. A label that is not in this list is a decision that a person must take, because a new label can multiply the series (the metric catalog in
    // docs/reference/observability.md describes each one).
    private static readonly HashSet<string> AllowedLabels = new(StringComparer.Ordinal)
    {
        "kind", "outcome", "use_case", "result", "degraded", "event_type", "handler", "level", "state",
        "http.request.method", "http.response.status_code", "http.route", "network.protocol.version", "url.scheme", "error.type",
        "aspnetcore.authentication.result", "aspnetcore.authentication.scheme", "aspnetcore.authorization.result", "aspnetcore.user.is_authenticated",
        "aspnetcore.rate_limiting.result", "aspnetcore.memory_pool.owner", "aspnetcore.routing.is_fallback", "aspnetcore.routing.match_status",
        "network.transport", "network.type", "network.peer.address", "server.address", "server.port", "http.connection.state", "dns.question.name",
        "db.namespace", "db.query.summary", "db.system.name", "db.response.status_code",
        "cpu.mode", "gc.heap.generation",
        "fusioncache.cache.name", "fusioncache.stale", "fusioncache.operation.background", "fusioncache.backplane.message_action",
    };

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}|[0-9a-f]{24,}", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Identifier();

    private async Task<string> GenerateTrafficAsync(int products)
    {
        await api.ResetAsync();
        var category = await api.CreateCategoryAsync("Keyboards");
        var key = ApiCalls.NewKey();
        var body = CatalogApiFixture.ProductJson(category.Id, "METRIC-0", "Keyboard", "89.99", "USD");

        // The same key twice is one reservation and one replay, which the idempotency counter reports.
        await api.SendAsync(HttpMethod.Post, "/api/v1/products", ApiCalls.Editor, body, ApiCalls.WithKey(key));
        var replay = await api.SendAsync(HttpMethod.Post, "/api/v1/products", ApiCalls.Editor, body, ApiCalls.WithKey(key));
        replay.Status.Should().Be(HttpStatusCode.Created);

        var last = string.Empty;
        for (var index = 1; index <= products; index++)
        {
            var created = await api.CreateProductAsync(category.Id, $"METRIC-{index}");
            last = created.Location!;
            (await api.SendAsync(HttpMethod.Get, last, ApiCalls.Reader)).Status.Should().Be(HttpStatusCode.OK);
            (await api.SendAsync(HttpMethod.Get, last, ApiCalls.Reader)).Status.Should().Be(HttpStatusCode.OK);
        }

        (await api.SendAsync(HttpMethod.Get, "/api/v1/products", null)).Status.Should().Be(HttpStatusCode.Unauthorized);
        (await api.SendAsync(HttpMethod.Get, "/api/v1/products", ApiCalls.Reader)).Status.Should().Be(HttpStatusCode.OK);

        return last;
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-002")]
    public async Task The_host_exports_the_series_that_the_plan_names_for_requests_authentication_the_runtime_use_cases_the_cache_the_outbox_and_the_idempotency()
    {
        var last = await GenerateTrafficAsync(3);
        IReadOnlyDictionary<string, IReadOnlyList<Series>> metrics = new Dictionary<string, IReadOnlyList<Series>>();

        // The outbox removes the entry of a product a moment after its write, which can fall between two reads, and the gauges of the outbox report after their first reading. The test reads on until the cache has counted a hit
        // and the outbox has reported, and the wait is bounded.
        await Poll.UntilAsync(
            async () =>
            {
                (await api.SendAsync(HttpMethod.Get, last, ApiCalls.Reader)).Status.Should().Be(HttpStatusCode.OK);
                metrics = api.Telemetry.FlushMetrics(api.Host.Services);

                return metrics.TryGetValue("catalog.cache.requests", out var requests) && requests.Any(series => series.Tags["result"] == "hit" && series.Sum >= 1) && metrics.ContainsKey("catalog.outbox.observation_age");
            },
            "a counted cache hit and a reading of the outbox",
            TestContext.Current.CancellationToken);
        metrics.Keys.Should().Contain(
        [
            "http.server.request.duration",
            "aspnetcore.authentication.authenticate.duration",
            "aspnetcore.authorization.attempts",
            "dotnet.gc.collections",
            "dotnet.process.memory.working_set",
            "db.client.operation.duration",
            "catalog.usecase.duration",
            "catalog.cache.requests",
            "catalog.cache.invalidations",
            "catalog.cache.shared.down",
            "catalog.cache.backplane.down",
            "catalog.outbox.delivered",
            "catalog.outbox.delivery_age",
            "catalog.outbox.handler.duration",
            "catalog.outbox.backlog",
            "catalog.outbox.oldest_pending_age",
            "catalog.outbox.dead_letters",
            "catalog.outbox.observation_age",
            "catalog.idempotency.decisions",
            "fusioncache.cache.hit",
            "fusioncache.cache.miss",
        ]);

        metrics["catalog.idempotency.decisions"].Where(series => series.Tags["outcome"] == "replayed").Sum(series => series.Sum).Should().BeGreaterThanOrEqualTo(1);
        metrics["catalog.idempotency.decisions"].Where(series => series.Tags["outcome"] == "reserved").Sum(series => series.Sum).Should().BeGreaterThanOrEqualTo(1);
        metrics["catalog.usecase.duration"].Should().Contain(series => series.Tags["use_case"] == "CreateProduct" && series.Tags["kind"] == "command" && series.Tags["outcome"] == "success" && series.Count >= 1);
        metrics["catalog.usecase.duration"].Should().Contain(series => series.Tags["use_case"] == "GetProduct" && series.Tags["kind"] == "query" && series.Tags["outcome"] == "success" && series.Count >= 1);
        metrics["catalog.cache.requests"].Where(series => series.Tags["result"] == "hit").Sum(series => series.Sum).Should().BeGreaterThanOrEqualTo(1);
        metrics["catalog.cache.requests"].Where(series => series.Tags["result"] == "miss").Sum(series => series.Sum).Should().BeGreaterThanOrEqualTo(1);
        metrics["catalog.outbox.delivery_age"].Sum(series => series.Count).Should().BeGreaterThanOrEqualTo(1);
        metrics["http.server.request.duration"].Should().Contain(series => series.Tags["http.response.status_code"] == "401");
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-002")]
    [Trait("Requirement", "REQ-SEC-006")]
    public async Task Every_label_of_every_exported_series_is_a_documented_word_and_no_value_is_an_identifier()
    {
        await GenerateTrafficAsync(3);

        var metrics = api.Telemetry.FlushMetrics(api.Host.Services);

        foreach (var (name, series) in metrics)
        {
            foreach (var item in series)
            {
                item.Tags.Keys.Should().OnlyContain(key => AllowedLabels.Contains(key), $"the series {name} may name only documented labels");
                item.Tags.Where(tag => tag.Key != "network.peer.address" && tag.Key != "server.address").Select(tag => tag.Value ?? string.Empty).Should().NotContain(value => Identifier().IsMatch(value), $"the series {name} must not carry an identifier");
            }
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-002")]
    public async Task Writing_and_reading_many_products_makes_no_label_value_that_names_a_product()
    {
        // A listener sees the series of every host in the process, so counting series would count other tests. The property is that no label value holds what this test wrote.
        await GenerateTrafficAsync(2);

        var category = await api.CreateCategoryAsync("Monitors");
        var written = new List<string> { category.Id.ToString(), "MANY-" };
        for (var index = 0; index < 12; index++)
        {
            var created = await api.CreateProductAsync(category.Id, $"MANY-{index}");
            written.Add(created.Location!.Split('/')[^1]);
            await api.SendAsync(HttpMethod.Get, created.Location!, ApiCalls.Reader);
        }

        var metrics = api.Telemetry.FlushMetrics(api.Host.Services);

        foreach (var (name, series) in metrics)
        {
            var values = series.SelectMany(item => item.Tags.Values).Select(value => value ?? string.Empty).ToList();

            foreach (var item in written)
            {
                values.Should().NotContain(value => value.Contains(item, StringComparison.OrdinalIgnoreCase), $"the series {name} must not carry {item}");
            }
        }

        metrics["http.server.request.duration"].Select(series => series.Tags.GetValueOrDefault("http.route")).Where(route => route is not null).Should().OnlyContain(route => route![0] == '/' && !Identifier().IsMatch(route));
        metrics["catalog.usecase.duration"].Select(series => series.Tags["use_case"]).Distinct().Count().Should().BeLessThanOrEqualTo(12, "a use case is a name of the code, so there are as many as the code has");
    }
}
