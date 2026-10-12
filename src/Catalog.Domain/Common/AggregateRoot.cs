namespace Catalog.Domain.Common;

/// <summary>
/// The root of an aggregate: the one object that the rest of the system loads, changes, and saves as a unit. It records the events of the changes
/// that it accepts, and the persistence layer reads and clears them when it saves (plan section 8.4).
/// </summary>
/// <typeparam name="TId">The type of the identifier.</typeparam>
public abstract class AggregateRoot<TId>
    where TId : notnull
{
    private readonly List<IDomainEvent> _events = [];

    /// <summary>Initializes a new instance of the <see cref="AggregateRoot{TId}"/> class.</summary>
    /// <param name="id">The identifier.</param>
    protected AggregateRoot(TId id)
    {
        ArgumentNullException.ThrowIfNull(id);

        Id = id;
    }

    /// <summary>Gets the identifier, which never changes.</summary>
    public TId Id { get; }

    /// <summary>Gets the events that happened since the last call to <see cref="ClearDomainEvents"/>, oldest first.</summary>
    public IReadOnlyList<IDomainEvent> DomainEvents => _events;

    /// <summary>Forgets the recorded events. The persistence layer calls it after it has saved them.</summary>
    public void ClearDomainEvents() => _events.Clear();

    /// <summary>Records an event.</summary>
    /// <param name="domainEvent">The event.</param>
    protected void Raise(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        _events.Add(domainEvent);
    }
}
