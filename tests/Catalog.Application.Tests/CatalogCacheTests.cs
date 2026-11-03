using AwesomeAssertions;
using Catalog.Application.Caching;
using Catalog.Application.Paging;
using Catalog.Application.Products;
using Catalog.Application.Results;
using Catalog.Application.Tests.Support;
using Catalog.CrossCutting.Caching;
using Catalog.Domain.Common;
using Catalog.Domain.Tests.Support;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;

namespace Catalog.Application.Tests;

/// <summary>
/// REQ-CACHE-001, REQ-CACHE-002, and REQ-CACHE-003: the FusionCache adapter keeps a success in memory and in the shared cache, never keeps a refusal, shares one load between concurrent reads, builds its keys from the
/// environment and the version of the contract, removes by tag, and never fails a read because of the cache. These tests need no Docker: the shared cache is the in-process one or a broken one. The tests against a
/// real Valkey are in <c>Catalog.Infrastructure.Tests</c>.
/// </summary>
public sealed class CatalogCacheTests
{
    private static readonly Guid ProductA = TestIds.Product(1).Value;
    private static readonly Guid ProductB = TestIds.Product(2).Value;

    private static CacheKey ProductKey(Guid id) => new($"product:{id:D}", [$"product:{id:D}"]);

    private static CacheKey ListKey(string hash, params string[] extra) => new($"products:list:{hash}", ["products:list", .. extra]);

    private static ProductView View(Guid id, string name = "Mechanical keyboard") =>
        new(id, "KB-100", name, null, 89.90m, "USD", TestIds.Category(1).Value, LifecycleStatus.Active, new DateTimeOffset(2026, 1, 15, 9, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 1, 15, 9, 30, 0, TimeSpan.Zero), "AAAAAAAAB9E");

    private static Func<CancellationToken, Task<Result<ProductView>>> Loads(ProductView view, Action? onCall = null) =>
        _ =>
        {
            onCall?.Invoke();

            return Task.FromResult(Result.Success(view));
        };

    private static MemoryDistributedCache SharedMemory() => new(Options.Create(new MemoryDistributedCacheOptions()));

    [Fact]
    [Trait("Requirement", "REQ-CACHE-001")]
    public async Task A_read_runs_the_loader_once_and_serves_the_next_reads_from_memory()
    {
        using var rig = new CacheRig();
        var calls = 0;

        var first = await rig.Cache.GetOrLoadAsync(ProductKey(ProductA), Loads(View(ProductA), () => calls++), CancellationToken.None);
        var second = await rig.Cache.GetOrLoadAsync(ProductKey(ProductA), Loads(View(ProductA, "Changed"), () => calls++), CancellationToken.None);

        first.Value.Should().Be(View(ProductA));
        second.Value.Should().Be(View(ProductA));
        calls.Should().Be(1);
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-001")]
    public async Task A_refusal_is_returned_to_the_caller_and_never_kept()
    {
        var shared = SharedMemory();
        using var rig = new CacheRig(shared: shared);
        var calls = 0;

        Task<Result<ProductView>> Refuse(CancellationToken token)
        {
            calls++;

            return Task.FromResult(Result.Failure<ProductView>(new NotFound("product")));
        }

        var first = await rig.Cache.GetOrLoadAsync(ProductKey(ProductA), Refuse, CancellationToken.None);
        var second = await rig.Cache.GetOrLoadAsync(ProductKey(ProductA), Refuse, CancellationToken.None);

        shared.Get($"v2:catalog:dev:v1:product:{ProductA:D}").Should().BeNull("a refusal reaches neither level");
        rig.Memory.TryGetValue($"catalog:dev:v1:product:{ProductA:D}", out _).Should().BeFalse();

        var fixedUp = await rig.Cache.GetOrLoadAsync(ProductKey(ProductA), Loads(View(ProductA)), CancellationToken.None);

        first.Error.Should().Be(new NotFound("product"));
        second.Error.Should().Be(new NotFound("product"));
        fixedUp.IsSuccess.Should().BeTrue("the product exists now, and the refusal did not outlive the change that fixed it");
        calls.Should().Be(2);
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-001")]
    public async Task Concurrent_reads_that_are_refused_each_get_the_refusal_and_never_a_value_that_belongs_to_another_caller()
    {
        using var rig = new CacheRig(shared: SharedMemory());
        var calls = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<Result<ProductView>> Refuse(CancellationToken token)
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            await release.Task;

            return Result.Failure<ProductView>(new NotFound("product"));
        }

        var reads = Enumerable.Range(0, 8).Select(_ => rig.Cache.GetOrLoadAsync(ProductKey(ProductA), Refuse, CancellationToken.None)).ToList();
        await entered.Task;
        release.SetResult();
        var results = await Task.WhenAll(reads);

        results.Should().OnlyContain(result => !result.IsSuccess && result.Error == new NotFound("product"));
        calls.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-001")]
    public async Task Concurrent_reads_of_one_key_on_one_node_share_one_load()
    {
        using var rig = new CacheRig();
        var calls = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<Result<ProductView>> Slow(CancellationToken token)
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            await release.Task;

            return Result.Success(View(ProductA));
        }

        var reads = Enumerable.Range(0, 8).Select(_ => rig.Cache.GetOrLoadAsync(ProductKey(ProductA), Slow, CancellationToken.None)).ToList();
        await entered.Task;
        release.SetResult();
        var results = await Task.WhenAll(reads);

        calls.Should().Be(1);
        results.Should().OnlyContain(result => result.IsSuccess && result.Value == View(ProductA));
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-001")]
    public async Task A_fault_of_the_loader_reaches_the_caller_unchanged_and_is_not_kept()
    {
        using var rig = new CacheRig();
        var calls = 0;

        Task<Result<ProductView>> Fails(CancellationToken token)
        {
            calls++;

            throw new InvalidOperationException("The database is down.");
        }

        var first = () => rig.Cache.GetOrLoadAsync(ProductKey(ProductA), Fails, CancellationToken.None);

        (await first.Should().ThrowAsync<InvalidOperationException>()).WithMessage("The database is down.");

        var second = await rig.Cache.GetOrLoadAsync(ProductKey(ProductA), Loads(View(ProductA), () => calls++), CancellationToken.None);

        second.IsSuccess.Should().BeTrue();
        calls.Should().Be(2);
        rig.Measurements.Total("catalog.cache.faults").Should().Be(0, "a fault of the loader is not a fault of the cache");
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-001")]
    public async Task A_cancelled_read_is_cancelled_and_never_absorbed()
    {
        using var rig = new CacheRig();
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        var act = () => rig.Cache.GetOrLoadAsync(ProductKey(ProductA), Loads(View(ProductA)), source.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-001")]
    public async Task The_key_of_an_entry_holds_the_environment_and_the_version_of_the_contract()
    {
        using var rig = new CacheRig(new CatalogCacheOptions { Environment = "staging" });

        await rig.Cache.GetOrLoadAsync(ProductKey(ProductA), Loads(View(ProductA)), CancellationToken.None);

        rig.Memory.TryGetValue($"catalog:staging:v{CacheContract.Version}:product:{ProductA:D}", out _).Should().BeTrue();
        CacheContract.KeyOf("product:x").Should().Be("v1:product:x");
        rig.Options.KeyPrefix.Should().Be("catalog:staging:");
        rig.Options.ChannelPrefix.Should().Be("catalog:staging");
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-001")]
    public async Task A_second_node_reads_what_the_first_node_stored_in_the_shared_cache_and_never_runs_the_loader()
    {
        var shared = SharedMemory();
        using var first = new CacheRig(shared: shared);
        using var second = new CacheRig(shared: shared);
        var calls = 0;

        await first.Cache.GetOrLoadAsync(ProductKey(ProductA), Loads(View(ProductA), () => calls++), CancellationToken.None);
        var read = await second.Cache.GetOrLoadAsync(ProductKey(ProductA), Loads(View(ProductA, "Not used"), () => calls++), CancellationToken.None);

        read.Value.Should().Be(View(ProductA), "the shared cache holds the entry in the format of the contract");
        calls.Should().Be(1);
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-001")]
    public async Task A_page_of_products_goes_through_the_shared_cache_in_the_format_of_the_contract()
    {
        var shared = SharedMemory();
        using var first = new CacheRig(shared: shared);
        using var second = new CacheRig(shared: shared);
        var page = new Page<ProductView>([View(ProductA), View(ProductB, "Other")], "cursor-1");

        await first.Cache.GetOrLoadAsync(ListKey("abc"), _ => Task.FromResult(Result.Success(page)), CancellationToken.None);
        var read = await second.Cache.GetOrLoadAsync<Page<ProductView>>(ListKey("abc"), _ => throw new InvalidOperationException("The shared cache holds the page."), CancellationToken.None);

        read.Value.Items.Should().Equal(page.Items);
        read.Value.NextCursor.Should().Be("cursor-1");
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-002")]
    public async Task Removing_a_tag_drops_the_entries_that_have_it_and_keeps_the_others()
    {
        using var rig = new CacheRig();
        var loads = new Dictionary<string, int>();

        Func<CancellationToken, Task<Result<ProductView>>> Counted(string name, ProductView view) =>
            _ =>
            {
                loads[name] = loads.GetValueOrDefault(name) + 1;

                return Task.FromResult(Result.Success(view));
            };

        await rig.Cache.GetOrLoadAsync(ProductKey(ProductA), Counted("a", View(ProductA)), CancellationToken.None);
        await rig.Cache.GetOrLoadAsync(ProductKey(ProductB), Counted("b", View(ProductB)), CancellationToken.None);
        await rig.Cache.GetOrLoadAsync(ListKey("all"), Counted("list", View(ProductA)), CancellationToken.None);

        await rig.Cache.RemoveByTagAsync($"product:{ProductA:D}", CancellationToken.None);

        await rig.Cache.GetOrLoadAsync(ProductKey(ProductA), Counted("a", View(ProductA)), CancellationToken.None);
        await rig.Cache.GetOrLoadAsync(ProductKey(ProductB), Counted("b", View(ProductB)), CancellationToken.None);
        await rig.Cache.GetOrLoadAsync(ListKey("all"), Counted("list", View(ProductA)), CancellationToken.None);

        loads["a"].Should().Be(2, "the tag of product A was removed");
        loads["b"].Should().Be(1, "product B has another tag");
        loads["list"].Should().Be(1, "the list does not carry the tag of product A");

        await rig.Cache.RemoveByTagAsync("products:list", CancellationToken.None);
        await rig.Cache.GetOrLoadAsync(ListKey("all"), Counted("list", View(ProductA)), CancellationToken.None);

        loads["list"].Should().Be(2, "every list carries the tag products:list");
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-002")]
    public async Task Removing_a_tag_also_drops_the_entry_from_the_shared_cache_so_a_new_node_loads_again()
    {
        var shared = SharedMemory();
        using var writer = new CacheRig(shared: shared);
        await writer.Cache.GetOrLoadAsync(ProductKey(ProductA), Loads(View(ProductA)), CancellationToken.None);

        await writer.Cache.RemoveByTagAsync($"product:{ProductA:D}", CancellationToken.None);

        using var fresh = new CacheRig(shared: shared);
        var calls = 0;
        await fresh.Cache.GetOrLoadAsync(ProductKey(ProductA), Loads(View(ProductA, "After the write"), () => calls++), CancellationToken.None);

        calls.Should().Be(1, "the marker of the removal is in the shared cache, so the old entry is not served");
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-002")]
    public async Task Removing_a_tag_that_no_entry_has_completes()
    {
        using var rig = new CacheRig();

        var act = () => rig.Cache.RemoveByTagAsync("product:unknown", CancellationToken.None);

        await act.Should().NotThrowAsync();
        rig.Measurements.Total("catalog.cache.invalidations", ("outcome", "complete")).Should().Be(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [Trait("Requirement", "REQ-CACHE-002")]
    public async Task A_removal_needs_a_tag(string? tag)
    {
        using var rig = new CacheRig();

        var act = () => rig.Cache.RemoveByTagAsync(tag!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-002")]
    public async Task A_product_lives_in_memory_for_the_memory_duration_and_a_list_for_the_shorter_list_duration()
    {
        using var rig = new CacheRig(new CatalogCacheOptions { MemoryDuration = TimeSpan.FromSeconds(30), ListDuration = TimeSpan.FromSeconds(10) });
        var page = new Page<ProductView>([View(ProductA)], null);

        await rig.Cache.GetOrLoadAsync(ProductKey(ProductA), Loads(View(ProductA)), CancellationToken.None);
        await rig.Cache.GetOrLoadAsync(ListKey("all"), _ => Task.FromResult(Result.Success(page)), CancellationToken.None);

        // FusionCache computes the expiration from the wall clock, so the test reads the expiration that it set and never waits for it. The margins are wide, so a slow host cannot make a test fail.
        var now = DateTimeOffset.UtcNow;
        var product = rig.Memory.Entries.Single(entry => entry.Key.EndsWith($":v1:product:{ProductA:D}", StringComparison.Ordinal));
        var list = rig.Memory.Entries.Single(entry => entry.Key.EndsWith(":v1:products:list:all", StringComparison.Ordinal));

        (product.Expires!.Value - now).Should().BeCloseTo(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5), "the bound of the staleness of a read is the memory duration of 30 seconds");
        (list.Expires!.Value - now).Should().BeCloseTo(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5), "a list expires after the list duration of 10 seconds");
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-001")]
    public async Task The_memory_of_a_node_holds_at_most_the_configured_number_of_entries()
    {
        using var memory = new MemoryCache(new MemoryCacheOptions { SizeLimit = 4 });
        using var rig = new CacheRig(new CatalogCacheOptions { MemoryMaxEntries = 4 }, memory: memory);
        var loads = 0;

        for (var index = 1; index <= 40; index++)
        {
            var id = TestIds.Product(index).Value;
            await rig.Cache.GetOrLoadAsync(ProductKey(id), Loads(View(id), () => loads++), CancellationToken.None);
        }

        loads.Should().Be(40);
        memory.Count.Should().BeLessThanOrEqualTo(4, "the limit counts every entry as one, and the markers of tags count too");
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-003")]
    public async Task A_broken_memory_cache_never_fails_a_read_because_the_loader_runs_without_it()
    {
        using var rig = new CacheRig(memory: new BrokenMemoryCache());
        var calls = 0;

        var result = await rig.Cache.GetOrLoadAsync(ProductKey(ProductA), Loads(View(ProductA), () => calls++), CancellationToken.None);

        result.Value.Should().Be(View(ProductA));
        calls.Should().Be(1);
        rig.Measurements.Total("catalog.cache.faults").Should().Be(1);
        rig.Log.Entries.Should().ContainSingle(entry => entry.StartsWith("Warning: The cache failed on a read", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-003")]
    public async Task A_shared_cache_that_cannot_be_reached_never_fails_a_read_and_opens_the_circuit_breaker()
    {
        var broken = new BrokenDistributedCache();
        using var rig = new CacheRig(shared: broken);
        var calls = 0;

        var first = await rig.Cache.GetOrLoadAsync(ProductKey(ProductA), Loads(View(ProductA), () => calls++), CancellationToken.None);
        var callsToTheSharedCache = broken.Calls;
        var second = await rig.Cache.GetOrLoadAsync(ProductKey(ProductB), Loads(View(ProductB), () => calls++), CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        calls.Should().Be(2);
        broken.Calls.Should().Be(callsToTheSharedCache, "the circuit breaker is open, so the second read never calls the shared cache");
        rig.Metrics.Degradation.SharedCacheDown.Should().BeTrue();
        rig.Measurements.Gauge("catalog.cache.shared.down").Should().Be(1);
        rig.Measurements.Total("catalog.cache.circuit_changes", ("level", "shared"), ("state", "open")).Should().Be(1);
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-003")]
    public async Task A_removal_throws_when_the_shared_cache_cannot_be_reached_and_still_drops_the_entry_of_the_node()
    {
        using var rig = new CacheRig(shared: new BrokenDistributedCache());
        var calls = 0;
        await rig.Cache.GetOrLoadAsync(ProductKey(ProductA), Loads(View(ProductA), () => calls++), CancellationToken.None);

        var removal = () => rig.Cache.RemoveByTagAsync($"product:{ProductA:D}", CancellationToken.None);

        var thrown = await removal.Should().ThrowAsync<CacheInvalidationIncompleteException>();
        thrown.Which.Message.Should().Contain($"product:{ProductA:D}").And.Contain("shared cache is down");

        await rig.Cache.GetOrLoadAsync(ProductKey(ProductA), Loads(View(ProductA), () => calls++), CancellationToken.None);

        calls.Should().Be(2, "the node dropped its own entry even though the removal is not confirmed elsewhere");
        rig.Measurements.Total("catalog.cache.invalidations", ("outcome", "incomplete")).Should().Be(1);
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-003")]
    public async Task A_removal_while_the_circuit_breaker_is_already_open_is_reported_as_incomplete_too()
    {
        using var rig = new CacheRig(shared: new BrokenDistributedCache());
        await rig.Cache.GetOrLoadAsync(ProductKey(ProductA), Loads(View(ProductA)), CancellationToken.None);

        var removal = () => rig.Cache.RemoveByTagAsync("products:list", CancellationToken.None);

        await removal.Should().ThrowAsync<CacheInvalidationIncompleteException>();
        await removal.Should().ThrowAsync<CacheInvalidationIncompleteException>("the second call finds the circuit breaker open and skips the shared cache, which is not a confirmation either");
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-003")]
    public async Task Reads_count_hits_and_misses_and_say_whether_the_node_was_degraded_without_a_key_in_any_tag()
    {
        using var rig = new CacheRig(shared: SharedMemory());

        await rig.Cache.GetOrLoadAsync(ProductKey(ProductA), Loads(View(ProductA)), CancellationToken.None);
        await rig.Cache.GetOrLoadAsync(ProductKey(ProductA), Loads(View(ProductA)), CancellationToken.None);
        await rig.Cache.GetOrLoadAsync(ProductKey(ProductA), Loads(View(ProductA)), CancellationToken.None);

        rig.Measurements.Total("catalog.cache.requests", ("result", "miss"), ("degraded", "false")).Should().Be(1);
        rig.Measurements.Total("catalog.cache.requests", ("result", "hit"), ("degraded", "false")).Should().Be(2);
        rig.Measurements.Total("catalog.cache.requests", ("degraded", "true")).Should().Be(0);
        rig.Measurements.All.SelectMany(measurement => measurement.Tags.Values).Should().NotContain(value => value != null && value.Contains("product", StringComparison.Ordinal), "no tag of a metric names a key or an identifier");
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-003")]
    public async Task A_hit_that_a_degraded_node_serves_is_counted_as_a_read_with_the_risk_of_staleness()
    {
        using var rig = new CacheRig(shared: new BrokenDistributedCache());
        await rig.Cache.GetOrLoadAsync(ProductKey(ProductA), Loads(View(ProductA)), CancellationToken.None);

        await rig.Cache.GetOrLoadAsync(ProductKey(ProductA), Loads(View(ProductA)), CancellationToken.None);

        rig.Measurements.Total("catalog.cache.requests", ("result", "hit"), ("degraded", "true")).Should().Be(1);
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-001")]
    public async Task The_log_of_the_cache_never_holds_a_value_of_an_entry()
    {
        var broken = new BrokenDistributedCache();
        using var rig = new CacheRig(shared: broken);
        var secret = View(ProductA, "Name-that-must-not-be-logged");

        await rig.Cache.GetOrLoadAsync(ProductKey(ProductA), Loads(secret), CancellationToken.None);
        await rig.Cache.GetOrLoadAsync(ProductKey(ProductA), Loads(secret), CancellationToken.None);

        rig.Log.Entries.Should().NotBeEmpty();
        rig.Log.Entries.Should().NotContain(entry => entry.Contains("Name-that-must-not-be-logged", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-001")]
    public void Settings_that_the_plan_names_are_the_defaults()
    {
        var options = new CatalogCacheOptions();

        options.Environment.Should().Be("dev");
        options.MemoryDuration.Should().Be(TimeSpan.FromSeconds(30), "plan section 8.5 sets the L1 time to live to 30 seconds");
        options.Validate().Should().BeEmpty();
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-001")]
    public void A_cache_cannot_be_built_from_settings_that_are_not_valid()
    {
        using var metrics = new CacheMetrics();

        var act = () => CatalogCacheFactory.Create(new CatalogCacheOptions { Environment = "Not Valid" }, metrics);

        act.Should().Throw<ArgumentException>().WithMessage("*Environment*").Which.Message.Should().NotContain("Not Valid");
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-001")]
    public async Task A_cache_without_a_shared_cache_or_a_backplane_runs_on_the_memory_of_the_node_and_owns_it()
    {
        using var metrics = new CacheMetrics();
        using var cache = CatalogCacheFactory.Create(new CatalogCacheOptions(), metrics);

        var result = await cache.GetOrLoadAsync(ProductKey(ProductA), Loads(View(ProductA)), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}
