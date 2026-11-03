namespace Catalog.Domain.Common;

/// <summary>
/// Anything that records domain events and lets the persistence layer read and clear them. It is the non-generic face of <see cref="AggregateRoot{TId}"/>, so the code that saves
/// aggregates (the outbox interceptor of the persistence layer) can find the events of every aggregate without knowing its identifier type.
/// </summary>
public interface IHasDomainEvents
{
    /// <summary>Gets the events that happened since the last call to <see cref="ClearDomainEvents"/>, oldest first.</summary>
    IReadOnlyList<IDomainEvent> DomainEvents { get; }

    /// <summary>Forgets the recorded events. The persistence layer calls it after it has saved them.</summary>
    void ClearDomainEvents();
}
