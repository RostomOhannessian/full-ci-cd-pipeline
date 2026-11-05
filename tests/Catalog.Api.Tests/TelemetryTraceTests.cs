using System.Diagnostics;
using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Catalog.Api.Tests.Support;
using Catalog.Infrastructure.Tests.Support;
using Xunit;

namespace Catalog.Api.Tests;

/// <summary>
/// REQ-OBS-001: a request is one trace that covers the HTTP request, the use case, the cache, the database, and the outbox, with the parent and child links that show which call made which (plan section 8.11, ADR-0043).
/// The host is the real composition root over a real SQL Server 2022 and a real Valkey 8.1, and the exporter is the in-memory exporter that the test attaches after the redaction of the host. A test finds its spans by the
/// trace that it started, because a listener sees the spans of every host of the process. The spans of the Redis commands reach the exporter after the call returns, so a test polls until they are there.
/// </summary>
public sealed class TelemetryTraceTests(CatalogApiFixture api) : IClassFixture<CatalogApiFixture>
{
    private const string AspNetCore = "Microsoft.AspNetCore";
    private const string UseCases = "Catalog.UseCases";
    private const string Outbox = "Catalog.Outbox";
    private const string EntityFramework = "OpenTelemetry.Instrumentation.EntityFrameworkCore";
    private const string SqlClient = "OpenTelemetry.Instrumentation.SqlClient";
    private const string Redis = "OpenTelemetry.Instrumentation.StackExchangeRedis";
    private const string Fusion = "ZiggyCreatures.Caching.Fusion";
    private const string FusionDistributed = "ZiggyCreatures.Caching.Fusion.Distributed";

    private static Activity Single(IEnumerable<Activity> spans, string source, string? name = null)
    {
        var found = spans.ToList();
        var matches = found.Where(span => span.Source.Name == source && (name is null || span.DisplayName == name)).ToList();

        return matches is [var only] ? only : throw new InvalidOperationException($"Expected one span of {source} {name} and found {matches.Count} among: {string.Join("; ", found.Select(span => $"{span.Source.Name} {span.DisplayName}"))}");
    }

    // The server span of the API. The trace of a request that needs a token also holds the spans of the host that serves the keys, because the instrumentation of the HTTP client passes the trace on to it.
    private static Activity Server(IEnumerable<Activity> spans) =>
        Single(spans.Where(span => span.Kind == ActivityKind.Server && ((string?)span.GetTagItem("url.path"))?.StartsWith("/api/", StringComparison.Ordinal) == true), AspNetCore);

    private static bool IsDescendant(Activity span, Activity ancestor, IReadOnlyList<Activity> trace)
    {
        for (var current = span; current.ParentSpanId != default;)
        {
            if (current.ParentSpanId == ancestor.SpanId)
            {
                return true;
            }

            var parent = trace.FirstOrDefault(candidate => candidate.SpanId == current.ParentSpanId);

            if (parent is null)
            {
                return false;
            }

            current = parent;
        }

        return false;
    }

    // The span of the request ends after the answer is written, the spans of the Redis commands reach the exporter after the call returns, and FusionCache writes the second level and the backplane in the background, so a test waits
    // for the spans that it reads. An item is the name of a source, or a source and a span name in the form "source | name".
    private async Task<IReadOnlyList<Activity>> TraceWithAsync(string traceId, params string[] sources)
    {
        await Poll.UntilAsync(() => Task.FromResult(sources.All(item => api.Telemetry.SpansOf(traceId).Any(span => span.Source.Name == item || $"{span.Source.Name} | {span.DisplayName}" == item))), $"spans of {string.Join(", ", sources)} in the trace {traceId}", TestContext.Current.CancellationToken);

        return api.Telemetry.SpansOf(traceId);
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-001")]
    public async Task A_read_is_one_trace_from_the_request_through_the_use_case_and_the_cache_to_the_database_and_to_valkey()
    {
        await api.ResetAsync();
        var category = await api.CreateCategoryAsync("Keyboards");
        var created = await api.CreateProductAsync(category.Id, "TRACE-READ-1");
        var traceId = ApiCalls.NewTraceId();

        var response = await api.SendAsync(HttpMethod.Get, created.Location!, ApiCalls.Reader, null, ApiCalls.WithTrace(traceId));
        var trace = await TraceWithAsync(traceId, AspNetCore, Redis, FusionDistributed);

        response.Status.Should().Be(HttpStatusCode.OK);
        var server = Server(trace);
        server.Kind.Should().Be(ActivityKind.Server);
        server.GetTagItem("http.route").Should().Be("/api/v{version:apiVersion}/products/{productId}");
        server.GetTagItem("http.response.status_code").Should().Be(200);

        var useCase = Single(trace, UseCases, "GetProduct");
        useCase.ParentSpanId.Should().Be(server.SpanId);
        useCase.GetTagItem("catalog.outcome").Should().Be("success");

        var cache = trace.First(span => span.Source.Name == Fusion && span.DisplayName == "get or set from cache");
        IsDescendant(cache, useCase, trace).Should().BeTrue("the cache is called by the use case");

        var factory = trace.First(span => span.Source.Name == Fusion && span.DisplayName == "execute factory");
        var entity = trace.First(span => span.Source.Name == EntityFramework);
        var sql = trace.First(span => span.Source.Name == SqlClient && Equals(span.GetTagItem("db.system.name"), "microsoft.sql_server"));
        IsDescendant(entity, factory, trace).Should().BeTrue("a miss runs the loader, which reads the database");
        sql.ParentSpanId.Should().Be(entity.SpanId, "the driver is called by EF Core");

        var distributed = trace.Where(span => span.Source.Name == FusionDistributed).ToList();
        var redis = trace.Where(span => span.Source.Name == Redis).ToList();
        redis.Should().Contain(command => distributed.Any(level => IsDescendant(command, level, trace)), "a command of Valkey is called by the shared level of the cache");
        redis.Should().OnlyContain(command => Equals(command.GetTagItem("db.system"), "redis"));

        trace.Where(span => span.ParentSpanId != server.ParentSpanId || span == server).Should().OnlyContain(span => span == server || trace.Any(parent => parent.SpanId == span.ParentSpanId), "every span but the first has a parent in the trace");
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-001")]
    public async Task A_write_is_one_trace_that_reaches_the_transaction_and_the_immediate_delivery_of_the_outbox()
    {
        await api.ResetAsync();
        var category = await api.CreateCategoryAsync("Keyboards");
        var traceId = ApiCalls.NewTraceId();

        var response = await api.SendAsync(
            HttpMethod.Post,
            "/api/v1/products",
            ApiCalls.Editor,
            CatalogApiFixture.ProductJson(category.Id, "TRACE-WRITE-1", "Keyboard", "89.99", "USD"),
            ApiCalls.WithBoth(ApiCalls.WithKey(ApiCalls.NewKey()), ApiCalls.WithTrace(traceId)));
        var trace = await TraceWithAsync(traceId, AspNetCore, Redis);

        response.Status.Should().Be(HttpStatusCode.Created);
        var server = Server(trace);
        server.GetTagItem("http.route").Should().Be("/api/v{version:apiVersion}/products/");

        var useCase = Single(trace, UseCases, "CreateProduct");
        useCase.ParentSpanId.Should().Be(server.SpanId);
        useCase.GetTagItem("catalog.use_case.kind").Should().Be("command");

        trace.Where(span => span.Source.Name == EntityFramework).Should().NotBeEmpty().And.OnlyContain(span => IsDescendant(span, useCase, trace), "every statement of the command runs inside the use case");
        trace.Where(span => span.Source.Name == SqlClient).Should().NotBeEmpty();

        var handling = Single(trace, Outbox, "outbox.handle");
        handling.ParentSpanId.Should().Be(useCase.SpanId, "the writing node delivers in process, inside the request");
        handling.GetTagItem("catalog.outbox.handler").Should().Be("catalog-cache-invalidation");
        handling.GetTagItem("catalog.outbox.event_type").Should().Be("ProductCreated");

        var removal = trace.First(span => span.Source.Name == Fusion && span.DisplayName == "remove from cache by tag");
        IsDescendant(removal, handling, trace).Should().BeTrue("the handler removes the tag from the cache");
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-001")]
    [Trait("Requirement", "REQ-API-005")]
    public async Task The_trace_identifier_of_an_error_is_the_trace_of_the_request_so_a_failure_can_be_followed_from_the_answer_to_the_trace()
    {
        var traceId = ApiCalls.NewTraceId();

        var invalid = await api.SendAsync(HttpMethod.Get, $"/api/v1/products/{Guid.NewGuid()}", ApiCalls.Reader, null, ApiCalls.WithTrace(traceId));
        var anonymousTrace = ApiCalls.NewTraceId();
        var anonymous = await api.SendAsync(HttpMethod.Get, "/api/v1/products", null, null, ApiCalls.WithTrace(anonymousTrace));

        invalid.Status.Should().Be(HttpStatusCode.BadRequest);
        invalid.Json.GetProperty("traceId").GetString().Should().Be(traceId);
        anonymous.Status.Should().Be(HttpStatusCode.Unauthorized);
        anonymous.Json.GetProperty("traceId").GetString().Should().Be(anonymousTrace);
        Server(await TraceWithAsync(traceId, AspNetCore)).GetTagItem("http.response.status_code").Should().Be(400);
        Server(await TraceWithAsync(anonymousTrace, AspNetCore)).GetTagItem("http.response.status_code").Should().Be(401);
        api.Telemetry.SpansOf(traceId).Single(span => span.Source.Name == UseCases).GetTagItem("catalog.outcome").Should().Be("refused");
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-001")]
    public async Task A_request_without_a_trace_gets_a_new_one_and_its_error_names_that_trace()
    {
        var anonymous = await api.SendAsync(HttpMethod.Get, "/api/v1/products", null);

        var traceId = anonymous.Json.GetProperty("traceId").GetString()!;

        traceId.Should().MatchRegex("^[0-9a-f]{32}$");
        Server(await TraceWithAsync(traceId, AspNetCore)).ParentSpanId.Should().Be(default(ActivitySpanId), "the host started the trace");
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-001")]
    public async Task A_health_probe_leaves_no_trace_because_it_would_be_most_of_them()
    {
        var traceId = ApiCalls.NewTraceId();

        await api.SendAsync(HttpMethod.Get, "/health/live", null, null, ApiCalls.WithTrace(traceId));

        api.Telemetry.SpansOf(traceId).Should().BeEmpty();
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-001")]
    public async Task The_traces_of_a_read_and_of_a_write_have_the_shape_that_the_snapshot_holds()
    {
        await api.ResetAsync();
        var category = await api.CreateCategoryAsync("Keyboards");
        var writeTrace = ApiCalls.NewTraceId();
        var created = await api.SendAsync(
            HttpMethod.Post,
            "/api/v1/products",
            ApiCalls.Editor,
            CatalogApiFixture.ProductJson(category.Id, "TRACE-SHAPE-1", "Keyboard", "89.99", "USD"),
            ApiCalls.WithBoth(ApiCalls.WithKey(ApiCalls.NewKey()), ApiCalls.WithTrace(writeTrace)));
        var readTrace = ApiCalls.NewTraceId();
        await api.SendAsync(HttpMethod.Get, created.Location!, ApiCalls.Reader, null, ApiCalls.WithTrace(readTrace));
        var write = await TraceWithAsync(
            writeTrace,
            AspNetCore,
            Redis,
            "Catalog.Outbox | outbox.handle",
            "ZiggyCreatures.Caching.Fusion | remove from cache by tag",
            "ZiggyCreatures.Caching.Fusion | set to cache",
            "ZiggyCreatures.Caching.Fusion.Backplane | publish to backplane",
            "ZiggyCreatures.Caching.Fusion.Distributed | set to cache level");
        var read = await TraceWithAsync(
            readTrace,
            AspNetCore,
            Redis,
            "ZiggyCreatures.Caching.Fusion.Backplane | publish to backplane",
            "ZiggyCreatures.Caching.Fusion.Distributed | get from cache level",
            "ZiggyCreatures.Caching.Fusion.Distributed | set to cache level");

        var shape = new System.Text.Json.Nodes.JsonObject
        {
            ["write"] = Edges(write),
            ["read"] = Edges(read),
        };

        await Snapshot.MatchAsync(shape.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    // The edges of a trace: which kind of span called which, without the counts, the identifiers, the database names, and the commands, so the shape holds when the number of calls changes and breaks when the structure does.
    private static System.Text.Json.Nodes.JsonArray Edges(IReadOnlyList<Activity> trace)
    {
        static string Label(Activity span) => span.Source.Name is EntityFramework or SqlClient or Redis ? span.Source.Name : $"{span.Source.Name} | {span.DisplayName}";

        var server = Server(trace);

        // The commands of Valkey reach the exporter after the call returns, so their edges depend on the moment of the read and are left out. The tests of the traces prove that they are there.
        // The EF Core and SqlClient instrumentations listen to diagnostic sources of the whole process, so a test process with several hosts records a command twice, and the second span is the child of the first. A span below a span of its own kind is left out of the shape.
        var edges = trace
            .Where(span => span != server && span.Source.Name != Redis && trace.Any(parent => parent.SpanId == span.ParentSpanId))
            .Select(span => (Parent: Label(trace.First(parent => parent.SpanId == span.ParentSpanId)), Child: Label(span)))
            .Where(edge => edge.Parent != edge.Child)
            .Select(edge => $"{edge.Parent} -> {edge.Child}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

        return new System.Text.Json.Nodes.JsonArray([.. edges.Select(edge => (System.Text.Json.Nodes.JsonNode?)System.Text.Json.Nodes.JsonValue.Create(edge))]);
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-001")]
    [Trait("Requirement", "REQ-SEC-006")]
    public async Task No_value_of_a_request_and_no_token_reaches_a_span_and_a_statement_carries_its_placeholders_only()
    {
        await api.ResetAsync();
        var category = await api.CreateCategoryAsync("Keyboards");
        var traceId = ApiCalls.NewTraceId();
        var token = api.Issuer.Token(ApiCalls.Editor);

        var response = await api.Client.SendAsync(
            api.Issuer,
            HttpMethod.Post,
            "/api/v1/products",
            ApiCalls.Editor,
            CatalogApiFixture.ProductJson(category.Id, "SENT-SKU-777", "Sentinel keyboard name", "89.99", "USD", "Sentinel description text"),
            ApiCalls.WithBoth(ApiCalls.WithKey(ApiCalls.NewKey()), ApiCalls.WithTrace(traceId)));
        var trace = await TraceWithAsync(traceId, AspNetCore, Redis);

        response.Status.Should().Be(HttpStatusCode.Created);
        var text = string.Join('\n', trace.SelectMany(span => span.TagObjects.Select(tag => $"{tag.Key}={tag.Value}").Concat(span.Events.SelectMany(spanEvent => spanEvent.Tags.Select(tag => $"{tag.Key}={tag.Value}")))));
        text.Should().NotContain("SENT-SKU-777").And.NotContain("Sentinel keyboard name").And.NotContain("Sentinel description text").And.NotContain(api.Issuer.Token(ApiCalls.Editor)[..40]).And.NotContain(token[..40]);
        trace.SelectMany(span => span.TagObjects).Select(tag => tag.Key).Should().NotContain(key => key.StartsWith("db.query.parameter", StringComparison.Ordinal));
        trace.Where(span => span.Source.Name == SqlClient).Select(span => (string?)span.GetTagItem("db.query.text")).Where(statement => statement is not null).Should().Contain(statement => statement!.Contains("@p0", StringComparison.Ordinal));
        JsonDocument.Parse(response.Body).RootElement.GetProperty("sku").GetString().Should().Be("SENT-SKU-777");
    }
}
