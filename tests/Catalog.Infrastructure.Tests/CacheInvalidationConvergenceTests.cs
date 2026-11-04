using AwesomeAssertions;
using Catalog.Application.Caching;
using Catalog.CrossCutting.Caching;
using Catalog.Domain.Tests.Support;
using Catalog.Infrastructure.Caching;
using Catalog.Infrastructure.Outbox;
using Catalog.Infrastructure.Tests.Support;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Xunit;

namespace Catalog.Infrastructure.Tests;

/// <summary>
/// REQ-CACHE-001, REQ-CACHE-002, and REQ-CACHE-003: the whole invalidation path of a write, across two hosts, one SQL Server, and one Valkey (plan sections 8.4 and 8.5, ADR-0012, ADR-0039). A write removes the
/// entries on the writing host at once and on the other host through the backplane. When the immediate removal is lost, because the host died or Valkey was down, the outbox handler finishes it, which is the proof of
/// the last confirmation item of ADR-0012: cache invalidation converges when the outbox is the fallback path. A message that is delivered again does nothing twice, because the receipt of the handler says that it ran.
/// The tests wait on signals of the system (the observing backplane, the state of the outbox row, the subscribers that Valkey counts) and move a fake clock for the backoff of the outbox, so no test sleeps to pass.
/// </summary>
public sealed class CacheInvalidationConvergenceTests(CatalogDatabaseFixture database, ValkeyFixture valkey) : IClassFixture<CatalogDatabaseFixture>
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string Connection => database.ConnectionString;

    private ValkeyServer Server => valkey.Server;

    private static CatalogCacheOptions Options(bool quickBreakers = false) =>
        quickBreakers
            ? new CatalogCacheOptions { Environment = $"t{Counters.Next()}", SharedCircuitBreakerDuration = TimeSpan.FromMilliseconds(200), BackplaneCircuitBreakerDuration = TimeSpan.FromMilliseconds(200) }
            : new CatalogCacheOptions { Environment = $"t{Counters.Next()}" };

    private async Task<(string Status, int Attempts, string LastError)> OnlyRowAsync()
    {
        var row = (await SqlProbe.RowsAsync(Connection, "SELECT [Status], Attempts, LastError FROM catalog.OutboxMessages")).Single();

        return (row[0], int.Parse(row[1], System.Globalization.CultureInfo.InvariantCulture), row[2]);
    }

    private Task<int> ReceiptsAsync(string handler) =>
        SqlProbe.ScalarAsync<int>(Connection, $"SELECT COUNT(*) FROM catalog.OutboxReceipts WHERE Handler = '{handler}'");

    [Fact]
    [Trait("Requirement", "REQ-CACHE-001")]
    [Trait("Requirement", "REQ-CACHE-002")]
    public async Task A_write_removes_the_entries_on_the_writing_host_at_once_and_on_the_other_host_through_the_backplane_and_the_outbox_repeats_the_removal_once()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        var options = Options();
        var observed = new ObservingBackplane(ValkeyConnection.CreateBackplane(Server.DefaultUserOptions()));
        using var writer = new CacheHost(database, Server.DefaultUserOptions(), options, clock);
        using var reader = new CacheHost(database, Server.DefaultUserOptions(), options, clock, observed);
        var id = await writer.SeedProductAsync(10m);
        var tags = new[] { CacheTags.Product(id.Value), CacheTags.ProductList };
        (await writer.ReadAsync(id, Token)).Amount.Should().Be(10m);
        (await writer.ReadListAsync(id, Token)).Items.Single().Amount.Should().Be(10m);
        (await reader.ReadAsync(id, Token)).Amount.Should().Be(10m, "the reader takes the entry of the shared cache and keeps it in its memory");
        (await reader.ReadListAsync(id, Token)).Items.Single().Amount.Should().Be(10m);
        reader.Loads.Should().Be(0, "the reader never loaded anything, because the shared cache answered");

        await writer.ChangePriceAsync(id, 20m, dispatch: true, Token);
        var own = await writer.ReadAsync(id, Token);
        var ownList = await writer.ReadListAsync(id, Token);
        await Task.WhenAll(tags.Select(tag => observed.TagRemovalAppliedAsync(tag).WaitAsync(Patience, Token)));
        var other = await reader.ReadAsync(id, Token);
        var otherList = await reader.ReadListAsync(id, Token);

        own.Amount.Should().Be(20m, "the writer reads its own write, because it removed the entries before it answered");
        ownList.Items.Single().Amount.Should().Be(20m);
        other.Amount.Should().Be(20m, "the backplane told the reader to drop what it held");
        otherList.Items.Single().Amount.Should().Be(20m);
        writer.Cache.Removed.Should().Equal(tags, "the immediate path removed the entry of the product and the lists once");

        var pass = await writer.Processor.ProcessBatchAsync(Token);

        pass.Delivered.Should().Be(1);
        writer.Cache.Removed.Should().Equal([.. tags, .. tags], "the outbox repeated the removal, which is the second delete that closes the race of a reader that loaded the old row before the commit");
        (await OnlyRowAsync()).Status.Should().Be("Done");
        (await ReceiptsAsync(CacheInvalidationSubscriber.HandlerName)).Should().Be(1);
        writer.Node.Measurements.Total("catalog.cache.invalidations", ("outcome", "complete")).Should().Be(4);
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-001")]
    [Trait("Requirement", "REQ-CACHE-002")]
    public async Task When_the_immediate_removal_never_happens_the_outbox_handler_of_another_host_converges_the_hosts_which_closes_the_last_item_of_ADR_0012()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        var options = Options();
        var observed = new ObservingBackplane(ValkeyConnection.CreateBackplane(Server.DefaultUserOptions()));
        using var writer = new CacheHost(database, Server.DefaultUserOptions(), options, clock, observed);
        using var survivor = new CacheHost(database, Server.DefaultUserOptions(), options, clock);
        var id = await writer.SeedProductAsync(10m);
        (await writer.ReadAsync(id, Token)).Amount.Should().Be(10m);
        (await survivor.ReadAsync(id, Token)).Amount.Should().Be(10m);

        // The host committed the price and died before it dispatched, or the dispatch was lost. Nothing removed an entry.
        await writer.ChangePriceAsync(id, 20m, dispatch: false, Token);

        writer.Cache.Removed.Should().BeEmpty();
        (await writer.ReadAsync(id, Token)).Amount.Should().Be(10m, "without a removal the host serves what it holds, which is the staleness that the outbox exists to end");
        (await survivor.ReadAsync(id, Token)).Amount.Should().Be(10m);

        var pass = await survivor.Processor.ProcessBatchAsync(Token);
        await observed.TagRemovalAppliedAsync(CacheTags.Product(id.Value)).WaitAsync(Patience, Token);

        pass.Delivered.Should().Be(1, "the processor of a host that did not write the message delivered it");
        survivor.Cache.Removed.Should().Equal(CacheTags.Product(id.Value), CacheTags.ProductList);
        (await writer.ReadAsync(id, Token)).Amount.Should().Be(20m, "the host that wrote and lost its dispatch converged through the backplane message of the outbox handler");
        (await survivor.ReadAsync(id, Token)).Amount.Should().Be(20m);
        (await OnlyRowAsync()).Status.Should().Be("Done");
        (await ReceiptsAsync(CacheInvalidationSubscriber.HandlerName)).Should().Be(1);
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-002")]
    [Trait("Requirement", "REQ-CACHE-003")]
    public async Task A_removal_that_valkey_could_not_confirm_is_retried_by_the_outbox_until_the_server_returns_and_then_the_other_host_converges()
    {
        await database.ResetAsync();
        await using var server = await ValkeyServer.StartAsync(fixedPort: true);
        var clock = Make.Clock();
        var options = Options(quickBreakers: true);
        var outbox = new OutboxOptions { JitterRatio = 0, BaseBackoff = TimeSpan.FromSeconds(1), MaxBackoff = TimeSpan.FromSeconds(1), MaxAttempts = 1000 };
        var observed = new ObservingBackplane(ValkeyConnection.CreateBackplane(server.DefaultUserOptions()));
        using var writer = new CacheHost(database, server.DefaultUserOptions(), options, clock, null, outbox);
        using var reader = new CacheHost(database, server.DefaultUserOptions(), options, clock, observed, outbox);
        var id = await writer.SeedProductAsync(10m);
        (await writer.ReadAsync(id, Token)).Amount.Should().Be(10m);
        (await reader.ReadAsync(id, Token)).Amount.Should().Be(10m);
        await using var admin = await server.ConnectAdminAsync();
        var channel = $"catalog:{options.Environment}.Backplane:v2";
        await Poll.UntilAsync(async () => await SubscribersAsync(admin, channel) == 2, "both hosts subscribe to the backplane channel", Token);

        await server.StopAsync();
        var write = () => writer.ChangePriceAsync(id, 20m, dispatch: true, Token);

        await write.Should().NotThrowAsync("the commit succeeded, and a failed removal never fails the use case");
        writer.DispatchLog.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Warning, "the immediate delivery failed once for the one message");
        (await writer.ReadAsync(id, Token)).Amount.Should().Be(20m, "the writer reads its own write even while Valkey is down, because its memory lost the entry");
        var failed = await writer.Processor.ProcessBatchAsync(Token);
        failed.Failed.Should().Be(1, "the handler could not confirm the removal, so the outbox keeps the message and backs off");
        var waiting = await OnlyRowAsync();
        waiting.Status.Should().Be("Pending");
        waiting.LastError.Should().Be(nameof(CacheInvalidationIncompleteException));
        (await reader.ReadAsync(id, Token)).Amount.Should().Be(10m, "the reader is stale for the time that Valkey is down, bounded by its memory time to live");

        await server.RestartAsync();
        await Poll.UntilAsync(async () => await SubscribersAsync(admin, channel) == 2, "both hosts subscribe again after the server returned", Token);

        // The circuit breaker of FusionCache measures the wall clock, so a retry can still meet an open breaker. Each pass moves the fake clock past the backoff, like the loop of the processor does after a wait.
        await Poll.UntilAsync(
            async () =>
            {
                clock.Advance(TimeSpan.FromSeconds(2));

                return (await writer.Processor.ProcessBatchAsync(Token)).Delivered == 1;
            },
            "the outbox delivers the message after the server returned",
            Token);
        await observed.TagRemovalAppliedAsync(CacheTags.Product(id.Value)).WaitAsync(Patience, Token);

        (await reader.ReadAsync(id, Token)).Amount.Should().Be(20m, "the retry of the outbox removed the entry on the other host through the backplane");
        (await OnlyRowAsync()).Status.Should().Be("Done");
        (await OnlyRowAsync()).Attempts.Should().BeGreaterThan(1);
        (await ReceiptsAsync(CacheInvalidationSubscriber.HandlerName)).Should().Be(1);
        writer.Node.Measurements.Total("catalog.cache.invalidations", ("outcome", "incomplete")).Should().BeGreaterThan(0);
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-002")]
    [Trait("Requirement", "REQ-DATA-003")]
    public async Task A_message_that_the_outbox_delivers_again_after_the_removal_finished_removes_nothing_twice_because_the_receipt_says_that_it_ran()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        var attempts = 0;
        var flaky = new RecordingHandler("flaky-follower") { Behavior = (_, _) => Interlocked.Increment(ref attempts) == 1 ? throw new InvalidOperationException("The follower failed once.") : Task.CompletedTask };
        using var host = new CacheHost(database, Server.DefaultUserOptions(), Options(), clock, null, null, flaky);
        var id = await host.SeedProductAsync(10m);
        await host.ChangePriceAsync(id, 20m, dispatch: false, Token);
        var tags = new[] { CacheTags.Product(id.Value), CacheTags.ProductList };

        var first = await host.Processor.ProcessBatchAsync(Token);
        clock.Advance(TimeSpan.FromHours(1));
        var second = await host.Processor.ProcessBatchAsync(Token);
        var third = await host.Processor.ProcessBatchAsync(Token);

        first.Failed.Should().Be(1, "the second handler failed, so the message goes back to the outbox");
        second.Delivered.Should().Be(1);
        third.Claimed.Should().Be(0, "a finished message is never leased again");
        host.Cache.Removed.Should().Equal(tags, "the receipt of the first delivery made the processor skip the handler on the second, so the tags were removed once");
        flaky.Deliveries.Should().HaveCount(2);
        (await ReceiptsAsync(CacheInvalidationSubscriber.HandlerName)).Should().Be(1);
        (await ReceiptsAsync("flaky-follower")).Should().Be(1);
        (await OnlyRowAsync()).Status.Should().Be("Done");
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-002")]
    public async Task An_event_that_the_policy_does_not_know_and_a_payload_that_is_not_in_its_shape_dead_letter_instead_of_leaving_a_stale_entry_unnoticed()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        using var host = new CacheHost(database, Server.DefaultUserOptions(), Options(), clock, null, new OutboxOptions { JitterRatio = 0, MaxAttempts = 1 });
        var now = clock.GetUtcNow().ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        var unknown = TestIds.V7(Counters.Next());
        var malformed = TestIds.V7(Counters.Next());
        await SqlServerFixture.ExecuteAsync(
            Connection,
            $$"""
            INSERT INTO catalog.OutboxMessages (Id, EventType, SchemaVersion, OccurredAt, Payload, [Status], Attempts, CreatedAt, NextAttemptAt)
            VALUES ('{{unknown}}', 'ProductTeleported', 1, '{{now}}', '{"productId":"x"}', 'Pending', 0, '{{now}}', '{{now}}'),
                   ('{{malformed}}', 'ProductDiscontinued', 1, '{{now}}', 'not json', 'Pending', 0, '{{now}}', '{{now}}')
            """);

        var pass = await host.Processor.ProcessBatchAsync(Token);

        pass.DeadLettered.Should().Be(2);
        host.Cache.Removed.Should().BeEmpty("a message that the policy refuses removes nothing");
        (await SqlProbe.RowsAsync(Connection, "SELECT [Status], LastError FROM catalog.OutboxMessages")).Should().OnlyContain(row => row[0] == "DeadLetter" && row[1] == nameof(InvalidOperationException));
    }

    // Valkey counts the subscribers of a channel, which is what tells a test that a host has joined the backplane again.
    private static async Task<long> SubscribersAsync(ConnectionMultiplexer admin, string channel)
    {
        var reply = (RedisResult[])(await admin.GetDatabase().ExecuteAsync("PUBSUB", "NUMSUB", channel))!;

        return (long)reply[1];
    }
}
