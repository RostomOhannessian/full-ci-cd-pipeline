using AwesomeAssertions;
using Catalog.Application.Caching;
using Catalog.Infrastructure.Caching;
using Catalog.Infrastructure.Tests.Support;
using Xunit;
using static Catalog.Infrastructure.Tests.Support.CacheData;

namespace Catalog.Infrastructure.Tests;

/// <summary>
/// REQ-CACHE-001, REQ-CACHE-002, and REQ-CACHE-003: two nodes of the API share one Valkey and nothing else (plan section 8.5, ADR-0016, ADR-0039). A removal by tag on one node reaches the memory of the other through
/// the backplane, and the test waits for the signal of an observing backplane that says that the other node applied the message, so nothing sleeps. A node that the backplane does not reach is stale for a bounded time,
/// which the tests measure and the consistency model documents.
/// </summary>
public sealed class CacheAcrossNodesTests(ValkeyFixture valkey)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private ValkeyServer Server => valkey.Server;

    [Fact]
    [Trait("Requirement", "REQ-CACHE-001")]
    [Trait("Requirement", "REQ-CACHE-002")]
    public async Task A_removal_on_one_node_drops_the_entry_in_the_memory_of_the_other_through_the_backplane()
    {
        var observed = new ObservingBackplane(ValkeyConnection.CreateBackplane(Server.DefaultUserOptions()));
        using var writer = new CacheNode(Server.DefaultUserOptions());
        using var reader = new CacheNode(Server.DefaultUserOptions(), writer.Options, observed);
        var id = Product(1);
        var tag = CacheTags.Product(id);
        var version = 0;

        await writer.Cache.GetOrLoadAsync(ProductKey(id), Loads(View(id, "before"), () => version++), Token);
        var warmed = await reader.Cache.GetOrLoadAsync(ProductKey(id), Loads(View(id, "not used"), () => version++), Token);
        warmed.Value!.Name.Should().Be("before", "the reader took the entry of the shared cache, so its memory holds it now");

        await writer.Cache.RemoveByTagAsync(tag, Token);
        await observed.TagRemovalAppliedAsync(tag).WaitAsync(Patience, Token);
        var after = await reader.Cache.GetOrLoadAsync(ProductKey(id), Loads(View(id, "after"), () => version++), Token);

        after.Value!.Name.Should().Be("after", "the backplane message made the reader drop the entry in its memory, and the shared cache holds none newer than the removal");
        version.Should().Be(2);
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-002")]
    [Trait("Requirement", "REQ-CACHE-003")]
    public async Task A_node_that_the_backplane_does_not_reach_keeps_serving_its_memory_after_a_removal()
    {
        var silent = new SilentBackplane();
        using var writer = new CacheNode(Server.DefaultUserOptions());
        using var reader = new CacheNode(Server.DefaultUserOptions(), writer.Options, silent);
        var id = Product(2);
        var tag = CacheTags.Product(id);

        await writer.Cache.GetOrLoadAsync(ProductKey(id), Loads(View(id, "before")), Token);
        await reader.Cache.GetOrLoadAsync(ProductKey(id), Loads(View(id, "not used")), Token);
        await writer.Cache.RemoveByTagAsync(tag, Token);
        var after = await reader.Cache.GetOrLoadAsync(ProductKey(id), Loads(View(id, "after")), Token);

        after.Value!.Name.Should().Be("before", "without the backplane the reader serves its memory until the entry expires, which is the staleness that the plan bounds by the memory time to live");
    }
}
