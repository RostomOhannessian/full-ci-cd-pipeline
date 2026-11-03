namespace Catalog.Application.Events;

/// <summary>
/// One domain event as the outbox delivers it to a handler. The payload is the JSON that the persistence layer wrote in the transaction of the change, in the shape that its snapshot tests hold, and
/// <see cref="SchemaVersion"/> says which version of the shape it is (plan sections 8.3 and 8.4).
/// </summary>
/// <param name="MessageId">The identifier of the message. It is the same on every delivery of the message, so a handler can tell a repeat.</param>
/// <param name="EventType">The stable name of the event type, such as <c>ProductPriceChanged</c>.</param>
/// <param name="SchemaVersion">The version of the shape of the payload.</param>
/// <param name="OccurredAt">When the event happened, in UTC.</param>
/// <param name="Payload">The event as JSON.</param>
public sealed record OutboxMessageEnvelope(Guid MessageId, string EventType, int SchemaVersion, DateTimeOffset OccurredAt, string Payload);

/// <summary>
/// Takes the messages of the outbox after a commit, for example to remove the cache entries that an event made stale (WP1.9). The outbox processor delivers each message at least once, so a handler must be
/// idempotent: doing its work twice for one message must give the result of doing it once. The processor records a receipt for each handler and message after the handler returns, and it skips a handler that has a
/// receipt, so a message that is delivered again runs only the handlers that did not finish (plan section 8.4, ADR-0012).
/// </summary>
public interface IOutboxMessageHandler
{
    /// <summary>Gets the stable name of the handler. It is the key of the receipt, so renaming the class never changes it, and it holds no more than 200 ASCII characters.</summary>
    string Name { get; }

    /// <summary>Handles one message. A thrown exception fails the delivery, and the processor tries again later with a longer backoff and moves the message to the dead-letter state after too many attempts.</summary>
    /// <param name="message">The message.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    Task HandleAsync(OutboxMessageEnvelope message, CancellationToken cancellationToken);
}
