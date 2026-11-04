using Catalog.Application.Events;
using Catalog.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace Catalog.Infrastructure.Outbox;

/// <summary>
/// Implements <see cref="IOutboxDispatcher"/>: right after a commit it gives each message that the commit wrote to every handler in process, and then it wakes the background service of this node (plan section 8.4,
/// ADR-0012, ADR-0035, ADR-0039). The immediate delivery is what makes a cache entry go within milliseconds on the writing node, and it is best-effort: a handler that fails is logged and counted and never fails
/// the use case, because the message is still in the outbox. The immediate delivery writes no receipt, so the processor delivers the same message to every handler again right after, and a handler runs at
/// least twice under normal operation, which is the second delete of the cache and is why a handler must be idempotent. The wake-up comes last, so the delivery of the outbox follows the immediate one. A message of
/// a scope that never committed is never staged, so nothing is delivered for a rolled-back run. One instance serves one scope, because it takes the messages of the write context of the scope.
/// </summary>
public sealed partial class OutboxDispatcher : IOutboxDispatcher
{
    private readonly CatalogDbContext _context;
    private readonly IReadOnlyList<IOutboxMessageHandler> _handlers;
    private readonly OutboxSignal _signal;
    private readonly OutboxMetrics _metrics;
    private readonly ILogger<OutboxDispatcher> _logger;

    /// <summary>Initializes a new instance of the <see cref="OutboxDispatcher"/> class.</summary>
    /// <param name="context">The write context of the scope, which holds the messages that its commit wrote.</param>
    /// <param name="handlers">The handlers that every message goes to.</param>
    /// <param name="signal">The signal that the background service waits on.</param>
    /// <param name="metrics">The metrics.</param>
    /// <param name="logger">The logger.</param>
    public OutboxDispatcher(CatalogDbContext context, IEnumerable<IOutboxMessageHandler> handlers, OutboxSignal signal, OutboxMetrics metrics, ILogger<OutboxDispatcher> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _handlers = [.. handlers ?? throw new ArgumentNullException(nameof(handlers))];
        _signal = signal ?? throw new ArgumentNullException(nameof(signal));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task DispatchCommittedAsync(CancellationToken cancellationToken)
    {
        var messages = _context.TakeStaged();

        try
        {
            foreach (var message in messages)
            {
                foreach (var handler in _handlers)
                {
                    await DeliverAsync(handler, message, cancellationToken);
                }
            }
        }
        finally
        {
            _signal.Notify();
        }
    }

    private async Task DeliverAsync(IOutboxMessageHandler handler, OutboxMessageEnvelope message, CancellationToken cancellationToken)
    {
        try
        {
            await handler.HandleAsync(message, cancellationToken);
            _metrics.Immediate(handler.Name, delivered: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // A handler is outside code, and the commit succeeded. Any fault of it leaves the message to the outbox processor and never fails the use case.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _metrics.Immediate(handler.Name, delivered: false);
            LogImmediateFailure(exception, message.MessageId, message.EventType, handler.Name);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "The immediate delivery of outbox message {MessageId} of type {EventType} to the handler {Handler} failed, so the outbox processor delivers it.")]
    private partial void LogImmediateFailure(Exception exception, Guid messageId, string eventType, string handler);
}
