using AwesomeAssertions;
using Catalog.Application.Caching;
using Catalog.Application.Paging;
using Catalog.Application.Products;
using Catalog.Application.Results;
using Catalog.CrossCutting.Caching;
using Catalog.Infrastructure.Caching;
using Catalog.Infrastructure.Tests.Support;
using StackExchange.Redis;
using Xunit;
using static Catalog.Infrastructure.Tests.Support.CacheData;

namespace Catalog.Infrastructure.Tests;

/// <summary>
/// REQ-CACHE-001 and REQ-CACHE-003: the FusionCache adapter over a real Valkey, with the digest-pinned image of the Compose stack. A second node reads what the first stored, the keys follow the scheme of the contract,
/// the times to live reach the server, a refusal never does, and no key, value, or log line holds a secret (plan section 8.5, ADR-0038). The two nodes of a test share a server and nothing else.
/// </summary>
public sealed class CacheOverValkeyTests(ValkeyFixture valkey)
{
    private ValkeyServer Server => valkey.Server;

    private static async Task<string[]> KeysAsync(ConnectionMultiplexer admin, string environment)
    {
        var server = admin.GetServer(admin.GetEndPoints().Single());

        return [.. (await server.KeysAsync(pattern: $"*catalog:{environment}:*").ToListAsync()).Select(key => key.ToString())];
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-001")]
    public async Task A_value_that_one_node_stored_is_served_to_a_second_node_by_the_shared_cache_without_running_the_loader()
    {
        using var first = new CacheNode(Server.DefaultUserOptions());
        using var second = new CacheNode(Server.DefaultUserOptions(), first.Options);
        var id = Product(1);
        var loads = 0;

        await first.Cache.GetOrLoadAsync(ProductKey(id), Loads(View(id), () => loads++), CancellationToken.None);
        var read = await second.Cache.GetOrLoadAsync(ProductKey(id), Loads(View(id, "Not used"), () => loads++), CancellationToken.None);

        read.Value.Should().Be(View(id));
        loads.Should().Be(1);
        second.Measurements.Total("catalog.cache.requests", ("result", "hit")).Should().Be(1, "the second node found the entry in the shared cache");
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-001")]
    public async Task The_keys_in_valkey_hold_the_prefix_of_the_environment_and_the_version_of_the_contract_and_nothing_else()
    {
        using var node = new CacheNode(Server.DefaultUserOptions());
        var id = Product(2);
        var page = new Page<ProductView>([View(id)], null);

        await node.Cache.GetOrLoadAsync(ProductKey(id), Loads(View(id)), CancellationToken.None);
        await node.Cache.GetOrLoadAsync(ListKey("all"), _ => Task.FromResult(Result.Success(page)), CancellationToken.None);
        await using var admin = await Server.ConnectAdminAsync();

        var keys = await KeysAsync(admin, node.Options.Environment);

        keys.Should().BeEquivalentTo(
            [
                $"v2:catalog:{node.Options.Environment}:v1:product:{id:D}",
                $"v2:catalog:{node.Options.Environment}:v1:products:list:all",
            ],
            "FusionCache adds the marker v2 of its wire format, then the prefix of the environment, then the key with the version of the contract (spike 1.b)");
        (await admin.GetDatabase().KeyTypeAsync(keys[0])).Should().Be(RedisType.Hash, "the distributed cache of Microsoft stores an entry as a hash");
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-001")]
    public async Task Two_environments_on_one_server_never_see_the_entries_of_each_other()
    {
        using var development = new CacheNode(Server.DefaultUserOptions());
        using var staging = new CacheNode(Server.DefaultUserOptions());
        var id = Product(3);
        var loads = 0;

        await development.Cache.GetOrLoadAsync(ProductKey(id), Loads(View(id), () => loads++), CancellationToken.None);
        await staging.Cache.GetOrLoadAsync(ProductKey(id), Loads(View(id), () => loads++), CancellationToken.None);

        loads.Should().Be(2, "the entry of one environment is not in the other");
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-001")]
    public async Task The_time_to_live_of_an_entry_and_of_a_list_reaches_the_server()
    {
        using var node = new CacheNode(Server.DefaultUserOptions());
        var id = Product(4);
        var page = new Page<ProductView>([View(id)], null);

        await node.Cache.GetOrLoadAsync(ProductKey(id), Loads(View(id)), CancellationToken.None);
        await node.Cache.GetOrLoadAsync(ListKey("all"), _ => Task.FromResult(Result.Success(page)), CancellationToken.None);
        await using var admin = await Server.ConnectAdminAsync();
        var database = admin.GetDatabase();

        var product = await database.KeyTimeToLiveAsync($"v2:catalog:{node.Options.Environment}:v1:product:{id:D}");
        var list = await database.KeyTimeToLiveAsync($"v2:catalog:{node.Options.Environment}:v1:products:list:all");

        product.Should().NotBeNull().And.BeLessThanOrEqualTo(node.Options.SharedDuration).And.BeGreaterThan(node.Options.SharedDuration - TimeSpan.FromSeconds(30));
        list.Should().NotBeNull().And.BeLessThanOrEqualTo(node.Options.ListDuration).And.BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-001")]
    public async Task A_refusal_reaches_neither_the_memory_nor_valkey()
    {
        using var node = new CacheNode(Server.DefaultUserOptions());
        var id = Product(5);

        var refused = await node.Cache.GetOrLoadAsync<ProductView>(ProductKey(id), _ => Task.FromResult(Result.Failure<ProductView>(new NotFound("product"))), CancellationToken.None);
        await using var admin = await Server.ConnectAdminAsync();

        refused.Error.Should().Be(new NotFound("product"));
        (await KeysAsync(admin, node.Options.Environment)).Should().BeEmpty();
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-001")]
    public async Task Removing_a_tag_writes_a_marker_for_the_tag_to_the_shared_cache_so_a_node_without_the_entry_in_memory_loads_again()
    {
        using var writer = new CacheNode(Server.DefaultUserOptions());
        using var fresh = new CacheNode(Server.DefaultUserOptions(), writer.Options);
        var id = Product(6);
        var loads = 0;
        await writer.Cache.GetOrLoadAsync(ProductKey(id), Loads(View(id), () => loads++), CancellationToken.None);

        await writer.Cache.RemoveByTagAsync($"product:{id:D}", CancellationToken.None);
        var read = await fresh.Cache.GetOrLoadAsync(ProductKey(id), Loads(View(id, "After the write"), () => loads++), CancellationToken.None);
        await using var admin = await Server.ConnectAdminAsync();

        read.Value.Name.Should().Be("After the write", "the entry in the shared cache is older than the marker of the removal, so it is not served");
        loads.Should().Be(2);
        (await KeysAsync(admin, writer.Options.Environment)).Should().Contain($"v2:catalog:{writer.Options.Environment}:__fc:t:product:{id:D}");
        writer.Measurements.Total("catalog.cache.invalidations", ("outcome", "complete")).Should().Be(1);
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-001")]
    public async Task No_key_value_or_log_line_holds_the_password_or_the_name_of_the_user_of_the_connection()
    {
        var (options, password) = await Server.CreateApplicationUserAsync($"catalog-secret-{Counters.Next()}");
        using var node = new CacheNode(options, new CatalogCacheOptions { Environment = "dev" });
        var id = Product(7);
        await node.Cache.GetOrLoadAsync(ProductKey(id), Loads(View(id)), CancellationToken.None);
        await node.Cache.RemoveByTagAsync($"product:{id:D}", CancellationToken.None);
        await using var admin = await Server.ConnectAdminAsync();
        var database = admin.GetDatabase();

        var texts = new List<string>(node.Log.Entries);
        foreach (var key in await KeysAsync(admin, "dev"))
        {
            texts.Add(key);
            foreach (var entry in await database.HashGetAllAsync(key))
            {
                texts.Add(entry.Name.ToString());
                texts.Add(entry.Value.ToString());
            }
        }

        texts.Should().Contain(text => text.Contains($"v2:catalog:dev:v1:product:{id:D}", StringComparison.Ordinal), "the entry reached the server through the user");
        texts.Should().NotContain(text => text.Contains(Server.AdminPassword, StringComparison.Ordinal) || text.Contains(password, StringComparison.Ordinal));
        texts.Should().NotContain(text => text.Contains(options.UserName, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-003")]
    public async Task A_failed_connection_never_writes_the_password_to_the_log_or_to_an_exception()
    {
        var closedPort = Server.Port == 1 ? 2 : 1;
        var options = new ValkeyOptions
        {
            Endpoint = $"127.0.0.1:{closedPort}",
            UserName = "catalog-dev",
            Password = "Pw-Secret-test",
            ConnectTimeout = TimeSpan.FromMilliseconds(300),
            CommandTimeout = TimeSpan.FromMilliseconds(600),
        };
        using var node = new CacheNode(options, new CatalogCacheOptions { Environment = $"t{Counters.Next()}", SharedTimeout = TimeSpan.FromMilliseconds(300) });
        var id = Product(8);

        var read = await node.Cache.GetOrLoadAsync(ProductKey(id), Loads(View(id)), CancellationToken.None);
        var removal = () => node.Cache.RemoveByTagAsync($"product:{id:D}", CancellationToken.None);
        var thrown = await removal.Should().ThrowAsync<CacheInvalidationIncompleteException>();

        read.IsSuccess.Should().BeTrue("the cache degrades and the loader runs");
        node.Log.Entries.Should().NotBeEmpty();
        node.Log.Entries.Should().NotContain(entry => entry.Contains("Pw-Secret-test", StringComparison.Ordinal));
        thrown.Which.ToString().Should().NotContain("Pw-Secret-test");
        node.Metrics.Degradation.SharedCacheDown.Should().BeTrue();
    }
}
