using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane;

namespace Catalog.Infrastructure.Tests.Support;

/// <summary>A backplane that connects and delivers nothing and receives nothing, so a node behaves as if pub/sub did not reach it while the shared cache works. It counts what it was asked to publish.</summary>
internal sealed class SilentBackplane : IFusionCacheBackplane
{
    private int _published;

    /// <summary>Gets the number of messages that the node asked to publish.</summary>
    public int Published => Volatile.Read(ref _published);

    /// <inheritdoc />
    public void Subscribe(BackplaneSubscriptionOptions options)
    {
    }

    /// <inheritdoc />
    public ValueTask SubscribeAsync(BackplaneSubscriptionOptions options) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public void Unsubscribe()
    {
    }

    /// <inheritdoc />
    public ValueTask UnsubscribeAsync() => ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask PublishAsync(BackplaneMessage message, FusionCacheEntryOptions options, CancellationToken token = default)
    {
        Interlocked.Increment(ref _published);

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public void Publish(BackplaneMessage message, FusionCacheEntryOptions options, CancellationToken token = default) => Interlocked.Increment(ref _published);
}
