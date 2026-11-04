using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane;

namespace Catalog.Infrastructure.Tests.Support;

/// <summary>
/// A backplane that wraps another one and tells a test when an incoming message has been applied by the cache of this node. FusionCache calls the handler of the subscription for each message that another node
/// published, and the handler returns after it dropped the entry, so the end of the handler is the moment that the node has converged. The test waits for that signal and never sleeps (plan section 10.3). The
/// <c>MessageReceived</c> event of FusionCache fires before the message is applied, which is why it is not used here. Publishing and unsubscribing pass through unchanged.
/// </summary>
/// <param name="inner">The backplane that does the work.</param>
internal sealed class ObservingBackplane(IFusionCacheBackplane inner) : IFusionCacheBackplane, IDisposable
{
    private readonly Lock _gate = new();
    private readonly List<BackplaneMessage> _applied = [];
    private readonly List<(Func<BackplaneMessage, bool> Predicate, TaskCompletionSource Done)> _waiters = [];

    /// <summary>Gets the messages that the cache of this node has applied, in the order of their arrival.</summary>
    public IReadOnlyList<BackplaneMessage> Applied
    {
        get
        {
            lock (_gate)
            {
                return [.. _applied];
            }
        }
    }

    /// <inheritdoc />
    public void Subscribe(BackplaneSubscriptionOptions options) => inner.Subscribe(Observe(options));

    /// <inheritdoc />
    public ValueTask SubscribeAsync(BackplaneSubscriptionOptions options) => inner.SubscribeAsync(Observe(options));

    /// <inheritdoc />
    public void Unsubscribe() => inner.Unsubscribe();

    /// <inheritdoc />
    public ValueTask UnsubscribeAsync() => inner.UnsubscribeAsync();

    /// <inheritdoc />
    public ValueTask PublishAsync(BackplaneMessage message, FusionCacheEntryOptions options, CancellationToken token = default) => inner.PublishAsync(message, options, token);

    /// <inheritdoc />
    public void Publish(BackplaneMessage message, FusionCacheEntryOptions options, CancellationToken token = default) => inner.Publish(message, options, token);

    /// <summary>Returns a task that completes when a message that satisfies the condition has been applied, which may already be the case.</summary>
    /// <param name="predicate">The condition.</param>
    public Task AppliedAsync(Func<BackplaneMessage, bool> predicate)
    {
        lock (_gate)
        {
            if (_applied.Any(predicate))
            {
                return Task.CompletedTask;
            }

            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((predicate, done));

            return done.Task;
        }
    }

    /// <summary>Returns a task that completes when a message that removes a tag, or any other tag marker, has been applied.</summary>
    /// <param name="tag">The tag.</param>
    public Task TagRemovalAppliedAsync(string tag) => AppliedAsync(message => message.CacheKey is { } key && key.EndsWith($"__fc:t:{tag}", StringComparison.Ordinal));

    /// <inheritdoc />
    public void Dispose() => (inner as IDisposable)?.Dispose();

    private BackplaneSubscriptionOptions Observe(BackplaneSubscriptionOptions options) =>
        new(
            options.CacheName,
            options.CacheInstanceId,
            options.ChannelName,
            options.ConnectHandler,
            message =>
            {
                options.IncomingMessageHandler?.Invoke(message);
                Record(message);
            },
            options.ConnectHandlerAsync,
            async message =>
            {
                if (options.IncomingMessageHandlerAsync is { } handler)
                {
                    await handler(message);
                }

                Record(message);
            });

    private void Record(BackplaneMessage message)
    {
        List<TaskCompletionSource> ready = [];

        lock (_gate)
        {
            _applied.Add(message);

            foreach (var waiter in _waiters.Where(waiter => waiter.Predicate(message)).ToList())
            {
                _waiters.Remove(waiter);
                ready.Add(waiter.Done);
            }
        }

        foreach (var done in ready)
        {
            done.TrySetResult();
        }
    }
}
