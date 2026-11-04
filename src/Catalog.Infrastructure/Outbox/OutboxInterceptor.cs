using Catalog.Application.Events;
using Catalog.Domain.Common;
using Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Catalog.Infrastructure.Outbox;

/// <summary>
/// Turns the domain events of the tracked aggregates into outbox rows inside the transaction of the save (plan section 8.4, ADR-0012, ADR-0035). Before the changes are written, it adds one
/// <see cref="OutboxMessage"/> for each recorded event, so the rows and the aggregate changes commit or roll back together. After a successful save it clears the events, so a second save does not write them
/// again, and after a failed save it removes the rows that it added, so the aggregates keep their events and the tracker holds no row that was never written.
/// </summary>
/// <param name="clock">The source of the time of the rows.</param>
/// <param name="newId">Makes the identifier of a message. The default is a UUID version 7 of the time of the clock, and a test passes a counter.</param>
public sealed class OutboxInterceptor(TimeProvider clock, Func<Guid>? newId = null) : SaveChangesInterceptor
{
    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly Func<Guid>? _newId = newId;

    /// <summary>Adds the outbox rows of the recorded events before a synchronous save writes the changes.</summary>
    /// <param name="eventData">The context that is about to save.</param>
    /// <param name="result">The result that the save would use.</param>
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Collect(eventData.Context);

        return result;
    }

    /// <summary>Adds the outbox rows of the recorded events before an asynchronous save writes the changes.</summary>
    /// <param name="eventData">The context that is about to save.</param>
    /// <param name="result">The result that the save would use.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Collect(eventData.Context);

        return ValueTask.FromResult(result);
    }

    /// <summary>Clears the events of the aggregates after a synchronous save wrote them.</summary>
    /// <param name="eventData">The context that saved.</param>
    /// <param name="result">The number of rows that the save changed.</param>
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        ClearEvents(eventData.Context);

        return result;
    }

    /// <summary>Clears the events of the aggregates after an asynchronous save wrote them.</summary>
    /// <param name="eventData">The context that saved.</param>
    /// <param name="result">The number of rows that the save changed.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        ClearEvents(eventData.Context);

        return ValueTask.FromResult(result);
    }

    /// <summary>Removes the rows that this interceptor added when a synchronous save failed.</summary>
    /// <param name="eventData">The context and the error of the failed save.</param>
    public override void SaveChangesFailed(DbContextErrorEventData eventData) => Forget(eventData?.Context);

    /// <summary>Removes the rows that this interceptor added when an asynchronous save failed.</summary>
    /// <param name="eventData">The context and the error of the failed save.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Forget(eventData?.Context);

        return Task.CompletedTask;
    }

    private void Collect(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = _clock.GetUtcNow();

        foreach (var aggregate in context.ChangeTracker.Entries<IHasDomainEvents>().Where(entry => entry.State != EntityState.Detached).Select(entry => entry.Entity).ToList())
        {
            foreach (var domainEvent in aggregate.DomainEvents)
            {
                context.Add(new OutboxMessage
                {
                    Id = _newId?.Invoke() ?? Guid.CreateVersion7(now),
                    EventType = domainEvent.EventType,
                    SchemaVersion = domainEvent.SchemaVersion,
                    OccurredAt = domainEvent.OccurredAt,
                    Payload = OutboxPayloads.Serialize(domainEvent),
                    Status = OutboxStatus.Pending,
                    Attempts = 0,
                    CreatedAt = now,
                    NextAttemptAt = now,
                });
            }
        }
    }

    private static void ClearEvents(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var aggregate in context.ChangeTracker.Entries<IHasDomainEvents>().Select(entry => entry.Entity).ToList())
        {
            aggregate.ClearDomainEvents();
        }

        // The rows are written and nothing reads them back through this context, so the tracker lets them go. The scope keeps what they say for the dispatcher that runs after the commit (ADR-0039).
        var written = context.ChangeTracker.Entries<OutboxMessage>().ToList();

        if (context is CatalogDbContext catalog)
        {
            catalog.Stage(written.Select(entry => entry.Entity).OrderBy(message => message.CreatedAt).ThenBy(message => message.Id).Select(message => new OutboxMessageEnvelope(message.Id, message.EventType, message.SchemaVersion, message.OccurredAt, message.Payload)));
        }

        foreach (var entry in written)
        {
            entry.State = EntityState.Detached;
        }
    }

    // The save failed and its transaction rolls back, so the rows that this interceptor added never existed. The events stay on the aggregates, which a caller that retries the save needs.
    private static void Forget(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries<OutboxMessage>().Where(entry => entry.State == EntityState.Added).ToList())
        {
            entry.State = EntityState.Detached;
        }
    }
}
