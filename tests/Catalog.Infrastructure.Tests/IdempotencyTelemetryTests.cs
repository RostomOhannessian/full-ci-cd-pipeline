using AwesomeAssertions;
using Catalog.Application.Idempotency;
using Catalog.Domain.Tests.Support;
using Catalog.Infrastructure.Idempotency;
using Catalog.Infrastructure.Tests.Support;
using Xunit;

namespace Catalog.Infrastructure.Tests;

/// <summary>
/// REQ-OBS-002: the idempotency store counts what it decided for each request that carried a key, by outcome, and never labels a measurement with the key or the caller (plan sections 8.4 and 8.11, ADR-0036, ADR-0043).
/// The plan names the replays, and the other three outcomes tell a client that retries from a client that reuses a key.
/// </summary>
public sealed class IdempotencyTelemetryTests(CatalogDatabaseFixture database) : IClassFixture<CatalogDatabaseFixture>
{
    private static readonly string FingerprintA = new('a', 64);
    private static readonly string FingerprintB = new('b', 64);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static IdempotencyRequest Request(string key, string? fingerprint = null) =>
        IdempotencyKey.TryCreate(key, out var created)
            ? new IdempotencyRequest(created, "caller-a", fingerprint ?? FingerprintA)
            : throw new InvalidOperationException($"'{key}' is not a valid idempotency key.");

    [Fact]
    [Trait("Requirement", "REQ-OBS-002")]
    [Trait("Requirement", "REQ-DATA-004")]
    public async Task Each_decision_of_the_store_is_counted_by_its_outcome_and_by_nothing_else()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        using var metrics = new IdempotencyMetrics();
        using var probe = new TelemetryProbe(metrics);
        await using var first = new CatalogScope(database);
        await using var second = new CatalogScope(database);
        var store = new IdempotencyStore(first.Context, clock, Microsoft.Extensions.Options.Options.Create(new IdempotencyOptions()), metrics);
        var other = new IdempotencyStore(second.Context, clock, Microsoft.Extensions.Options.Options.Create(new IdempotencyOptions()), metrics);

        await store.BeginAsync(Request("key-1"), Token);
        await other.BeginAsync(Request("key-1"), Token);
        await other.BeginAsync(Request("key-1", FingerprintB), Token);
        first.UnitOfWork.BeforeCommit((_, token) => store.CompleteAsync(Request("key-1"), "{}", token));
        await first.UnitOfWork.ExecuteAsync(_ => Task.FromResult(Application.Results.Result.Success(1)), Token);
        await other.BeginAsync(Request("key-1"), Token);

        probe.Total("catalog.idempotency.decisions", ("outcome", "reserved")).Should().Be(1);
        probe.Total("catalog.idempotency.decisions", ("outcome", "in_progress")).Should().Be(1);
        probe.Total("catalog.idempotency.decisions", ("outcome", "reused")).Should().Be(1);
        probe.Total("catalog.idempotency.decisions", ("outcome", "replayed")).Should().Be(1);
        probe.Of("catalog.idempotency.decisions").SelectMany(sample => sample.Tags.Keys).Should().OnlyContain(key => key == "outcome");
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-002")]
    public async Task A_store_without_metrics_decides_as_before()
    {
        await database.ResetAsync();
        await using var scope = new CatalogScope(database);
        var store = new IdempotencyStore(scope.Context, Make.Clock(), Microsoft.Extensions.Options.Options.Create(new IdempotencyOptions()));

        var decision = await store.BeginAsync(Request("key-1"), Token);

        decision.Outcome.Should().Be(IdempotencyOutcome.Reserved);
    }
}
