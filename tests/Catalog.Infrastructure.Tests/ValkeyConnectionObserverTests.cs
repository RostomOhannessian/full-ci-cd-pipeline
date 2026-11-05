using AwesomeAssertions;
using Catalog.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;
using Xunit;

namespace Catalog.Infrastructure.Tests;

/// <summary>
/// REQ-OBS-001: every connection to Valkey that the cache or the backplane opens is passed to whoever instruments connections, whichever comes first, the connection or the instrumentation, and none is passed twice
/// (plan section 8.11, ADR-0043). The tracing needs each connection object, because the instrumentation of StackExchange.Redis profiles one connection at a time. These tests need no server: the connection never aborts when
/// the server is down, so it opens without one.
/// </summary>
public sealed class ValkeyConnectionObserverTests
{
    private static ValkeyOptions Unreachable() => new()
    {
        Endpoint = "127.0.0.1:1",
        UserName = "catalog-test",
        Password = "Pw-Secret-test",
        ConnectTimeout = TimeSpan.FromMilliseconds(200),
        CommandTimeout = TimeSpan.FromMilliseconds(300),
    };

    private static async Task<IConnectionMultiplexer> OpenAsync() => await ConnectionMultiplexer.ConnectAsync(ValkeyConnection.Configure(Unreachable()));

    [Fact]
    [Trait("Requirement", "REQ-OBS-001")]
    public async Task A_connection_that_opens_after_the_sink_arrived_is_passed_at_once()
    {
        var observer = new ValkeyConnectionObserver();
        List<IConnectionMultiplexer> received = [];
        observer.Attach(received.Add);
        await using var connection = (ConnectionMultiplexer)await OpenAsync();

        observer.Connected(connection);

        received.Should().ContainSingle().Which.Should().BeSameAs(connection);
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-001")]
    public async Task A_connection_that_opened_before_the_sink_arrived_is_passed_when_it_arrives_and_then_only_once()
    {
        var observer = new ValkeyConnectionObserver();
        await using var first = (ConnectionMultiplexer)await OpenAsync();
        await using var second = (ConnectionMultiplexer)await OpenAsync();
        observer.Connected(first);
        List<IConnectionMultiplexer> received = [];

        observer.Attach(received.Add);
        observer.Connected(second);

        received.Should().HaveCount(2).And.ContainInOrder(first, second);
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-001")]
    public async Task The_shared_cache_passes_its_own_connection_to_the_observer_when_it_opens()
    {
        var observer = new ValkeyConnectionObserver();
        List<IConnectionMultiplexer> received = [];
        observer.Attach(received.Add);
        using var cache = (IDisposable)ValkeyConnection.CreateSharedCache(Unreachable(), observer);

        // The server is not there, so the command fails. The connection opened before it did, and that is the point.
        var read = async () => await ((IDistributedCache)cache).GetAsync("key", TestContext.Current.CancellationToken);
        await read.Should().ThrowAsync<Exception>();

        received.Should().ContainSingle();
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-001")]
    public void The_observer_and_its_sink_must_exist()
    {
        var observer = new ValkeyConnectionObserver();

        ((Action)(() => observer.Connected(null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => observer.Attach(null!))).Should().Throw<ArgumentNullException>();
    }
}
