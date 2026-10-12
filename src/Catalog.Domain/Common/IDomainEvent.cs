namespace Catalog.Domain.Common;

/// <summary>
/// A fact that happened to an aggregate. Every event carries the version of its own shape and the time it happened, so a message in the outbox
/// stays readable after the shape changes (plan section 8.3). The time comes from the <see cref="TimeProvider"/> that the caller passed in.
/// </summary>
public interface IDomainEvent
{
    /// <summary>Gets the stable name of the event type. It is written to the outbox, so renaming the class never changes it.</summary>
    string EventType { get; }

    /// <summary>Gets the version of the shape of this event. A change to the shape of an event raises it.</summary>
    int SchemaVersion { get; }

    /// <summary>Gets the moment when the event happened, in UTC.</summary>
    DateTimeOffset OccurredAt { get; }
}
