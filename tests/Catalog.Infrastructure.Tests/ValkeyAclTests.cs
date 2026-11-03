using AwesomeAssertions;
using Catalog.Application.Results;
using Catalog.CrossCutting.Caching;
using Catalog.Infrastructure.Caching;
using Catalog.Infrastructure.Tests.Support;
using StackExchange.Redis;
using Xunit;
using static Catalog.Infrastructure.Tests.Support.CacheData;

namespace Catalog.Infrastructure.Tests;

/// <summary>
/// REQ-CACHE-001 and REQ-CACHE-003 (the trust boundary of plan section 9 and ADR-0038): the application user of Valkey has the rules that <c>compose.yaml</c> gives it, read from the file, and the whole cache scenario
/// passes with them and leaves no entry in the log of denied commands, so no command, key pattern, or channel that the code needs is missing, and the user reaches nothing else. The environment is <c>dev</c>, the one
/// that the file names, so only the tests of this class use it.
/// </summary>
public sealed class ValkeyAclTests(ValkeyFixture valkey)
{
    private ValkeyServer Server => valkey.Server;

    private static async Task<List<string>> DeniedAsync(ConnectionMultiplexer admin, string userName)
    {
        var log = await admin.GetDatabase().ExecuteAsync("ACL", "LOG");
        List<string> denied = [];

        foreach (var entry in (RedisResult[])log!)
        {
            var fields = (RedisResult[])entry!;
            var values = Enumerable.Range(0, fields.Length / 2).ToDictionary(index => fields[index * 2].ToString()!, index => fields[(index * 2) + 1].ToString() ?? string.Empty);

            if (values.GetValueOrDefault("username") == userName)
            {
                denied.Add($"{values.GetValueOrDefault("reason")} {values.GetValueOrDefault("context")} {values.GetValueOrDefault("object")}");
            }
        }

        return denied;
    }

    private static async Task<ConnectionMultiplexer> ConnectAsUserAsync(ValkeyOptions options)
    {
        // A plain connection, with none of the switches of the cache, so a command that the cache never sends reaches the server and meets the rules of the user.
        var configuration = new ConfigurationOptions { User = options.UserName, Password = options.Password, AbortOnConnectFail = true, ConnectTimeout = 10_000, SyncTimeout = 10_000, AsyncTimeout = 10_000 };
        var separator = options.Endpoint.LastIndexOf(':');
        configuration.EndPoints.Add(options.Endpoint[..separator], int.Parse(options.Endpoint[(separator + 1)..], System.Globalization.CultureInfo.InvariantCulture));

        return await ConnectionMultiplexer.ConnectAsync(configuration);
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-001")]
    public async Task The_whole_cache_scenario_passes_with_the_rules_of_compose_and_no_command_key_or_channel_is_denied()
    {
        var userName = $"catalog-acl-{Counters.Next()}";
        var (options, _) = await Server.CreateApplicationUserAsync(userName);
        var cache = new CatalogCacheOptions { Environment = "dev" };
        using var first = new CacheNode(options, cache);
        using var second = new CacheNode(options, cache);
        await using var admin = await Server.ConnectAdminAsync();
        await admin.GetDatabase().ExecuteAsync("ACL", "LOG", "RESET");
        var id = Product(20);
        var loads = 0;

        await first.Cache.GetOrLoadAsync(ProductKey(id), Loads(View(id), () => loads++), CancellationToken.None);
        await second.Cache.GetOrLoadAsync(ProductKey(id), Loads(View(id), () => loads++), CancellationToken.None);
        await first.Cache.GetOrLoadAsync(ListKey("all"), _ => Task.FromResult(Result.Success(new Catalog.Application.Paging.Page<Catalog.Application.Products.ProductView>([View(id)], null))), CancellationToken.None);
        await first.Cache.RemoveByTagAsync($"product:{id:D}", CancellationToken.None);
        await first.Cache.RemoveByTagAsync("products:list", CancellationToken.None);

        var loadedAgainByTheSecondNode = false;
        await Poll.UntilAsync(
            async () =>
            {
                await second.Cache.GetOrLoadAsync(ProductKey(id), Loads(View(id, "After the write"), () => loadedAgainByTheSecondNode = true), CancellationToken.None);

                return loadedAgainByTheSecondNode;
            },
            "the second node learns of the removal through the backplane",
            TestContext.Current.CancellationToken);

        loads.Should().Be(1, "the second node read the entry from the shared cache the first time");
        (await DeniedAsync(admin, userName)).Should().BeEmpty("every command, key, and channel that the cache uses is in the rules of the file");
        first.Metrics.Degradation.Degraded.Should().BeFalse();
        second.Metrics.Degradation.Degraded.Should().BeFalse();
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-001")]
    public async Task The_application_user_reaches_its_own_keys_and_its_own_channel_and_nothing_else()
    {
        var userName = $"catalog-acl-{Counters.Next()}";
        var (options, _) = await Server.CreateApplicationUserAsync(userName);
        await using var connection = await ConnectAsUserAsync(options);
        var database = connection.GetDatabase();

        await database.ExecuteAsync("HMSET", "v2:catalog:dev:v1:acl-probe", "data", "ok");
        var reply = (RedisResult[])(await database.ExecuteAsync("HMGET", "v2:catalog:dev:v1:acl-probe", "data"))!;
        reply.Single().ToString().Should().Be("ok");
        await database.ExecuteAsync("UNLINK", "v2:catalog:dev:v1:acl-probe");
        (await connection.GetSubscriber().PublishAsync(RedisChannel.Literal("catalog:dev.Backplane:v2"), "probe")).Should().BeGreaterThanOrEqualTo(0);

        var denied = new Dictionary<string, Func<Task>>
        {
            ["a key of another environment"] = async () => await database.StringGetAsync("v2:catalog:prod:v1:product:x"),
            ["a key without the prefix of the wire format"] = async () => await database.StringGetAsync("catalog:dev:v1:product:x"),
            ["a key of no prefix"] = async () => await database.StringGetAsync("other"),
            ["a channel of another environment"] = async () => await connection.GetSubscriber().PublishAsync(RedisChannel.Literal("catalog:prod.Backplane:v2"), "x"),
            ["a channel that the backplane does not use"] = async () => await connection.GetSubscriber().PublishAsync(RedisChannel.Literal("catalog:dev:other"), "x"),
            ["the list of every key"] = async () => await database.ExecuteAsync("KEYS", "*"),
            ["the removal of every key"] = async () => await database.ExecuteAsync("FLUSHALL"),
            ["a change of the access control list"] = async () => await database.ExecuteAsync("ACL", "SETUSER", "intruder", "on", "nopass", "~*", "+@all"),
            ["a setting of the server"] = async () => await database.ExecuteAsync("CONFIG", "SET", "maxmemory", "1gb"),
            ["the shutdown of the server"] = async () => await database.ExecuteAsync("SHUTDOWN", "NOSAVE"),
        };

        foreach (var (what, attempt) in denied)
        {
            var act = () => attempt();

            (await act.Should().ThrowAsync<RedisServerException>(what)).Which.Message.Should().StartWith("NOPERM", what);
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-001")]
    public void The_rules_of_the_application_user_in_compose_grant_no_wildcard_and_no_dangerous_command()
    {
        var rules = ComposeImage.ValkeyApplicationRules("Pw-Secret-test");

        rules.Should().Contain("on").And.Contain("-@all", "the user starts from no command at all");
        rules.Should().NotContain(["~*", "&*", "+@all", "allkeys", "allchannels", "allcommands", "nopass", "+@dangerous", "+@admin"]);
        rules.Should().NotContain(["+config|get", "+cluster|nodes", "+get"], "the connection of the cache switches off the commands of the handshake that it does not need (ADR-0038)");
        rules.Should().Contain("~v2:catalog:dev:*").And.Contain("&catalog:dev.Backplane:v2");
        rules.Where(rule => rule.StartsWith('~') || rule.StartsWith('&')).Should().BeEquivalentTo(["~v2:catalog:dev:*", "&catalog:dev.Backplane:v2"], "the user names the keys and the channel that the tests verified, and nothing else");
        File.ReadAllText(Path.Combine(ComposeImage.RepositoryRoot(), "compose.yaml")).Should().Contain("user default off", "the default user of the stack is off");
    }
}
