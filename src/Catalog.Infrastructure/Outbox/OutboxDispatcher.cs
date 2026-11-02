using Catalog.Application.Events;

namespace Catalog.Infrastructure.Outbox;

/// <summary>
/// Implements <see cref="IOutboxDispatcher"/>: after a commit it wakes the background service of this node, so a message is delivered within milliseconds and not at the next poll. The wake-up is best-effort. The
/// processor delivers every message at least once even when this call never happens, because it also looks for due messages at each poll (plan section 8.4, ADR-0012).
/// </summary>
/// <param name="signal">The signal that the background service waits on.</param>
public sealed class OutboxDispatcher(OutboxSignal signal) : IOutboxDispatcher
{
    private readonly OutboxSignal _signal = signal ?? throw new ArgumentNullException(nameof(signal));

    /// <inheritdoc />
    public Task DispatchCommittedAsync(CancellationToken cancellationToken)
    {
        _signal.Notify();

        return Task.CompletedTask;
    }
}
