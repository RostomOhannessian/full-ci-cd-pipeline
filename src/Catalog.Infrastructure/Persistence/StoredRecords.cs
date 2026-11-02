namespace Catalog.Infrastructure.Persistence;

/// <summary>Where a message of the outbox is in its life. The column holds the name, so a person who reads the table sees the state.</summary>
public enum OutboxStatus
{
    /// <summary>The message waits for delivery, or for the next attempt after a failure.</summary>
    Pending = 0,

    /// <summary>Every handler took the message, and the processor marked it done.</summary>
    Done = 1,

    /// <summary>The message failed too often and moved out of the line of delivery. It is a metric and an alert, and a person decides what to do with it.</summary>
    DeadLetter = 2,
}

/// <summary>
/// One domain event that waits for delivery (plan section 8.4, ADR-0012). The interceptor of the unit of work writes the row in the transaction of the change that raised the event, so the row exists exactly
/// when the change does. The processor leases rows, delivers them at least once, and marks them done.
/// </summary>
public sealed class OutboxMessage
{
    /// <summary>Gets or sets the identifier of the message, which a handler uses to recognize a delivery that it has seen.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the stable name of the event type (<c>IDomainEvent.EventType</c>).</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>Gets or sets the version of the shape of the event.</summary>
    public int SchemaVersion { get; set; }

    /// <summary>Gets or sets when the event happened, in UTC.</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Gets or sets the event as JSON, in the shape that <c>OutboxPayloads</c> writes.</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>Gets or sets where the message is in its life.</summary>
    public OutboxStatus Status { get; set; }

    /// <summary>Gets or sets the number of deliveries that were started, which counts a delivery that never finished, so a message that crashes its processor reaches the dead-letter state.</summary>
    public int Attempts { get; set; }

    /// <summary>Gets or sets when the row was written, in UTC.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets the earliest moment of the next delivery, in UTC. A failure moves it forward by the backoff.</summary>
    public DateTimeOffset NextAttemptAt { get; set; }

    /// <summary>Gets or sets the moment when the lease of a processor ends, or null when no processor holds the message.</summary>
    public DateTimeOffset? LeasedUntil { get; set; }

    /// <summary>Gets or sets the identifier of the lease that holds the message, so a processor whose lease ran out cannot overwrite the work of the one that took over.</summary>
    public Guid? LeaseId { get; set; }

    /// <summary>Gets or sets when the message was marked done, in UTC.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Gets or sets a short description of the last failure: the type of the exception and a bounded message, never a payload or a credential.</summary>
    public string? LastError { get; set; }
}

/// <summary>
/// The proof that a handler took a message, so a delivery that comes twice runs the handler once (plan section 8.4). The key is the message and the name of the handler, and a handler writes the row
/// after it succeeded.
/// </summary>
public sealed class OutboxReceipt
{
    /// <summary>Gets or sets the identifier of the message.</summary>
    public Guid MessageId { get; set; }

    /// <summary>Gets or sets the name of the handler, which <c>IOutboxMessageHandler.Name</c> gives.</summary>
    public string Handler { get; set; } = string.Empty;

    /// <summary>Gets or sets when the handler finished, in UTC.</summary>
    public DateTimeOffset ProcessedAt { get; set; }
}

/// <summary>Where a request with an idempotency key is: it runs, or it finished and its first result is kept.</summary>
public enum IdempotencyStatus
{
    /// <summary>The first request with the key runs.</summary>
    InProgress = 0,

    /// <summary>The first request finished, and the row holds its result.</summary>
    Completed = 1,
}

/// <summary>
/// One request with an idempotency key (plan section 8.4). The row stores the key, the caller, a fingerprint of the request, the status, and the first result, and it expires after 24 hours.
/// The key belongs to the caller, so two callers can use the same text.
/// </summary>
public sealed class IdempotencyRecord
{
    /// <summary>Gets or sets the identifier of the caller (<c>ICurrentPrincipal.CallerId</c>).</summary>
    public string CallerId { get; set; } = string.Empty;

    /// <summary>Gets or sets the key that the caller sent.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Gets or sets the hash of the request, so a key that comes back with another request is told apart from a replay.</summary>
    public string Fingerprint { get; set; } = string.Empty;

    /// <summary>Gets or sets where the request is.</summary>
    public IdempotencyStatus Status { get; set; }

    /// <summary>Gets or sets the serialized first result of a completed request, or null while it runs.</summary>
    public string? Response { get; set; }

    /// <summary>Gets or sets when the key was reserved, in UTC.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets when the record stops counting, in UTC. A cleanup job deletes the row after that.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}
