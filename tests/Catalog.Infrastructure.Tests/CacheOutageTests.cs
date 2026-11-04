using System.Diagnostics;
using AwesomeAssertions;
using Catalog.Application.Caching;
using Catalog.CrossCutting.Caching;
using Catalog.Infrastructure.Tests.Support;
using Xunit;
using static Catalog.Infrastructure.Tests.Support.CacheData;

namespace Catalog.Infrastructure.Tests;

/// <summary>
/// REQ-CACHE-003: when Valkey stops, the cache degrades to the memory of the node and the loader, and the node never throws, reports the state in a gauge, and recovers when the server returns (plan section 8.5,
/// ADR-0016, ADR-0038, ADR-0039). Each test starts a Valkey server of its own, with a fixed host port so that it can stop and start the same server, and the cache of a test belongs to one environment. The circuit
/// breaker of FusionCache measures the wall clock, which no test can fake, so the breaker is set to a short time and a recovery is a bounded poll of what the server holds.
/// </summary>
public sealed class CacheOutageTests
{
    // A breaker that closes quickly keeps a recovery test short. The default of 5 seconds is a setting for production, and the validator accepts any positive time.
    private static CatalogCacheOptions Options() => new() { Environment = $"t{Counters.Next()}", SharedCircuitBreakerDuration = TimeSpan.FromMilliseconds(200), BackplaneCircuitBreakerDuration = TimeSpan.FromMilliseconds(200) };

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Requirement", "REQ-CACHE-003")]
    public async Task When_valkey_stops_reads_are_served_from_memory_or_the_loader_and_writes_stay_in_memory_without_an_exception()
    {
        await using var server = await ValkeyServer.StartAsync(fixedPort: true);
        using var node = new CacheNode(server.DefaultUserOptions(), Options());
        var warm = Product(1);
        var fresh = Product(2);
        var loads = 0;
        await node.Cache.GetOrLoadAsync(ProductKey(warm), Loads(View(warm), () => loads++), Token);
        node.Metrics.Degradation.Degraded.Should().BeFalse("nothing has failed yet");

        await server.StopAsync();
        var stopwatch = Stopwatch.StartNew();
        var memoryHit = await node.Cache.GetOrLoadAsync(ProductKey(warm), Loads(View(warm, "Not used"), () => loads++), Token);
        var miss = await node.Cache.GetOrLoadAsync(ProductKey(fresh), Loads(View(fresh), () => loads++), Token);
        var stored = await node.Cache.GetOrLoadAsync(ProductKey(fresh), Loads(View(fresh, "Not used"), () => loads++), Token);
        stopwatch.Stop();

        memoryHit.Value.Should().Be(View(warm), "the memory of the node still holds the entry");
        miss.Value.Should().Be(View(fresh), "the loader answered, because the shared cache is down");
        stored.Value.Should().Be(View(fresh), "the entry that the loader produced stays in memory, so a write degrades to the memory level");
        loads.Should().Be(2, "one load for the entry that was warm and one for the new entry, and none for the repeats");
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "a stopped server must not make the node wait for it on every call, because the circuit breaker opens after the first failure");
        node.Metrics.Degradation.SharedCacheDown.Should().BeTrue();
        node.Measurements.Gauge("catalog.cache.shared.down").Should().Be(1);
        node.Measurements.Total("catalog.cache.requests", ("degraded", "true")).Should().BeGreaterThan(0);
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-003")]
    public async Task The_first_read_after_the_server_stops_is_bounded_by_the_timeout_of_the_shared_cache()
    {
        await using var server = await ValkeyServer.StartAsync(fixedPort: true);
        var options = Options();
        using var node = new CacheNode(server.DefaultUserOptions(), options);
        await node.Cache.GetOrLoadAsync(ProductKey(Product(3)), Loads(View(Product(3))), Token);

        await server.StopAsync();
        var stopwatch = Stopwatch.StartNew();
        await node.Cache.GetOrLoadAsync(ProductKey(Product(4)), Loads(View(Product(4))), Token);
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(options.SharedTimeout + TimeSpan.FromSeconds(5), "the first failure costs at most the timeout of the shared cache and a margin for a slow machine, and the breaker answers the next calls at once");
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-003")]
    public async Task A_removal_while_valkey_is_down_clears_the_memory_of_the_node_and_reports_that_it_is_not_confirmed()
    {
        await using var server = await ValkeyServer.StartAsync(fixedPort: true);
        using var node = new CacheNode(server.DefaultUserOptions(), Options());
        var id = Product(5);
        var loads = 0;
        await node.Cache.GetOrLoadAsync(ProductKey(id), Loads(View(id, "before"), () => loads++), Token);
        await server.StopAsync();

        var removal = () => node.Cache.RemoveByTagAsync(CacheTags.Product(id), Token);

        await removal.Should().ThrowAsync<CacheInvalidationIncompleteException>("the outbox must try again, because the other nodes may still hold the entry");
        var after = await node.Cache.GetOrLoadAsync(ProductKey(id), Loads(View(id, "after"), () => loads++), Token);
        after.Value!.Name.Should().Be("after", "the memory of this node lost the entry, so the writer reads its own write even while Valkey is down");
        node.Measurements.Total("catalog.cache.invalidations", ("outcome", "incomplete")).Should().Be(1);
        node.Measurements.Gauge("catalog.cache.shared.down").Should().Be(1, "the shared cache is the level that failed first");
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-003")]
    public async Task A_backplane_that_refuses_the_message_while_the_shared_cache_works_is_reported_and_leaves_the_other_node_stale()
    {
        await using var server = await ValkeyServer.StartAsync(fixedPort: false);
        var (writerUser, _) = await server.CreateApplicationUserAsync($"writer{Counters.Next()}");

        // The access control list of the application user names the environment dev, and this server belongs to the test alone.
        var options = Options();
        options.Environment = "dev";
        using var writer = new CacheNode(writerUser, options);
        using var reader = new CacheNode(server.DefaultUserOptions(), options);
        var id = Product(9);
        await writer.Cache.GetOrLoadAsync(ProductKey(id), Loads(View(id, "before")), Token);
        await reader.Cache.GetOrLoadAsync(ProductKey(id), Loads(View(id, "Not used")), Token);

        // The server keeps the user and refuses its channel from now on, so publishing fails and the shared cache keeps working. That is a backplane that is down while Valkey is up.
        await using (var admin = await server.ConnectAdminAsync())
        {
            await admin.GetDatabase().ExecuteAsync("ACL", ["SETUSER", writerUser.UserName, "resetchannels"]);
        }

        var removal = () => writer.Cache.RemoveByTagAsync(CacheTags.Product(id), Token);

        await removal.Should().ThrowAsync<CacheInvalidationIncompleteException>("the message was not published, so the other node may keep the entry");
        writer.Metrics.Degradation.BackplaneDown.Should().BeTrue();
        writer.Metrics.Degradation.SharedCacheDown.Should().BeFalse("the shared cache accepted the marker");
        writer.Measurements.Gauge("catalog.cache.backplane.down").Should().Be(1, "the staleness of the other nodes widens to the memory time to live, and the gauge is what the alert watches");
        writer.Measurements.Gauge("catalog.cache.shared.down").Should().Be(0);
        var stale = await reader.Cache.GetOrLoadAsync(ProductKey(id), Loads(View(id, "after")), Token);
        stale.Value!.Name.Should().Be("before", "without the message the reader serves its memory until the entry expires, which the memory time to live bounds");
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-003")]
    public async Task The_node_recovers_by_itself_when_valkey_returns_and_stores_in_the_shared_cache_and_confirms_removals_again()
    {
        await using var server = await ValkeyServer.StartAsync(fixedPort: true);
        var options = Options();
        using var node = new CacheNode(server.DefaultUserOptions(), options);
        await node.Cache.GetOrLoadAsync(ProductKey(Product(6)), Loads(View(Product(6))), Token);
        await server.StopAsync();
        await node.Cache.GetOrLoadAsync(ProductKey(Product(7)), Loads(View(Product(7))), Token);
        node.Metrics.Degradation.Degraded.Should().BeTrue();

        await server.RestartAsync();
        await using var admin = await server.ConnectAdminAsync();
        var number = 100;

        // Every read of a new key tries the shared cache once the breaker has closed. The breaker measures the wall clock, so the test repeats the read until the server holds a key of this environment.
        await Poll.UntilAsync(
            async () =>
            {
                var id = Product(number++);
                await node.Cache.GetOrLoadAsync(ProductKey(id), Loads(View(id)), Token);

                return await admin.GetDatabase().KeyExistsAsync($"v2:catalog:{options.Environment}:v1:product:{id:D}");
            },
            "a new entry reaches the shared cache after the server returned",
            Token);
        node.Metrics.Degradation.SharedCacheDown.Should().BeFalse();
        node.Measurements.Gauge("catalog.cache.shared.down").Should().Be(0);

        await Poll.UntilAsync(
            async () =>
            {
                try
                {
                    await node.Cache.RemoveByTagAsync(CacheTags.Product(Product(6)), Token);

                    return true;
                }
                catch (CacheInvalidationIncompleteException)
                {
                    return false;
                }
            },
            "a removal is confirmed again after the server returned",
            Token);
        node.Metrics.Degradation.Degraded.Should().BeFalse("the backplane recovered as well");
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-003")]
    public async Task A_node_that_starts_while_valkey_is_down_serves_every_read_from_the_loader_and_joins_the_cache_when_the_server_appears()
    {
        await using var server = await ValkeyServer.StartAsync(fixedPort: true);
        var options = Options();
        await server.StopAsync();
        using var node = new CacheNode(server.DefaultUserOptions(), options);
        var id = Product(8);
        var loads = 0;

        var first = await node.Cache.GetOrLoadAsync(ProductKey(id), Loads(View(id), () => loads++), Token);
        var second = await node.Cache.GetOrLoadAsync(ProductKey(id), Loads(View(id, "Not used"), () => loads++), Token);

        first.Value.Should().Be(View(id), "the host starts and serves without Valkey, and the readiness of the host stays green because the cache never fails a read");
        second.Value.Should().Be(View(id));
        loads.Should().Be(1, "the memory level works without the shared cache");
        node.Metrics.Degradation.Degraded.Should().BeTrue();

        await server.RestartAsync();
        await using var admin = await server.ConnectAdminAsync();
        var number = 200;
        await Poll.UntilAsync(
            async () =>
            {
                var next = Product(number++);
                await node.Cache.GetOrLoadAsync(ProductKey(next), Loads(View(next)), Token);

                return await admin.GetDatabase().KeyExistsAsync($"v2:catalog:{options.Environment}:v1:product:{next:D}");
            },
            "the node that started without the server joins it",
            Token);
    }
}
