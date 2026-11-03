using Catalog.Application.Events;
using Catalog.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Catalog.Infrastructure.Outbox;

/// <summary>One message that a pass of the processor leased, as the lease query returned it.</summary>
public sealed class ClaimedMessage
{
    /// <summary>Gets or sets the identifier of the message.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the stable name of the event type.</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>Gets or sets the version of the shape of the payload.</summary>
    public int SchemaVersion { get; set; }

    /// <summary>Gets or sets when the event happened, in UTC.</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Gets or sets the payload.</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>Gets or sets the number of deliveries that were started for the message, counting this one.</summary>
    public int Attempts { get; set; }
}

/// <summary>A batch of messages that one processor holds under one lease. Only the processor that holds the lease can finish a message of it.</summary>
/// <param name="LeaseId">The identifier of the lease.</param>
/// <param name="Messages">The messages, oldest first.</param>
public sealed record OutboxLease(Guid LeaseId, IReadOnlyList<ClaimedMessage> Messages);

/// <summary>What one pass of the processor did.</summary>
/// <param name="Claimed">The messages that the pass leased.</param>
/// <param name="Delivered">The messages that every handler took.</param>
/// <param name="Failed">The messages that failed and wait for a new attempt.</param>
/// <param name="DeadLettered">The messages that failed too often and moved to the dead-letter state.</param>
public sealed record OutboxBatchResult(int Claimed, int Delivered, int Failed, int DeadLettered);

/// <summary>
/// Delivers the messages of the outbox at least once (plan section 8.4, ADR-0012, ADR-0035). A pass leases a batch with one statement that takes the due rows with <c>UPDLOCK, READPAST, ROWLOCK</c>, so two
/// processors never hold one row and a processor never waits for the locks of another. It then gives each message to every handler that has no receipt for it, records a receipt after each handler, and marks the message
/// done. A handler that throws fails the delivery: the message waits for an exponential backoff with jitter, and after too many attempts it moves to the dead-letter state, which is a metric and an error in the log.
/// A processor that stops before it finishes a message leaves its lease in place, and the message is delivered again when the lease ends, so a handler must be idempotent. The pass needs no timer, so a test drives
/// it with a fake clock (<see cref="OutboxBackgroundService"/> runs it in a loop).
/// </summary>
public sealed partial class OutboxProcessor
{
    private readonly IDbContextFactory<CatalogDbContext> _contexts;
    private readonly IReadOnlyList<IOutboxMessageHandler> _handlers;
    private readonly OutboxOptions _options;
    private readonly TimeProvider _clock;
    private readonly OutboxMetrics _metrics;
    private readonly ILogger<OutboxProcessor> _logger;
    private readonly Random _random;
    private readonly Func<Guid> _newLeaseId;

    /// <summary>Initializes a new instance of the <see cref="OutboxProcessor"/> class.</summary>
    /// <param name="contexts">Makes a context for each pass, so the processor holds no tracker between passes.</param>
    /// <param name="handlers">The handlers that every message goes to.</param>
    /// <param name="options">The settings.</param>
    /// <param name="clock">The source of the time of leases, backoffs, and receipts.</param>
    /// <param name="metrics">The metrics.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="random">The source of jitter. A test passes a seeded one. The default is the shared one, because jitter needs no secrecy.</param>
    /// <param name="newLeaseId">Makes the identifier of a lease. The default is a UUID version 7, and a test passes a counter.</param>
    public OutboxProcessor(
        IDbContextFactory<CatalogDbContext> contexts,
        IEnumerable<IOutboxMessageHandler> handlers,
        IOptions<OutboxOptions> options,
        TimeProvider clock,
        OutboxMetrics metrics,
        ILogger<OutboxProcessor> logger,
        Random? random = null,
        Func<Guid>? newLeaseId = null)
    {
        _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
        _handlers = [.. handlers ?? throw new ArgumentNullException(nameof(handlers))];
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _random = random ?? Random.Shared;
        _newLeaseId = newLeaseId ?? (() => Guid.CreateVersion7(clock.GetUtcNow()));
    }

    /// <summary>Leases up to a batch of due messages and delivers them.</summary>
    /// <param name="cancellationToken">A token that stops the pass. A message that is not finished when it is cancelled keeps its lease.</param>
    public async Task<OutboxBatchResult> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        var lease = await ClaimAsync(cancellationToken);

        return await DeliverAsync(lease, cancellationToken);
    }

    /// <summary>
    /// Leases up to a batch of messages that are pending, due, and not held by a lease that is still running, and counts one attempt for each. The lease statement takes the rows with <c>UPDLOCK, READPAST, ROWLOCK</c>,
    /// so a row that another processor holds is skipped and never waited for.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    public async Task<OutboxLease> ClaimAsync(CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var until = now + _options.LeaseDuration;
        var lease = _newLeaseId();
        var batch = _options.BatchSize;

        await using var context = await _contexts.CreateDbContextAsync(cancellationToken);
        var messages = await context.Database
            .SqlQuery<ClaimedMessage>(
                $"""
                WITH due AS (
                    SELECT TOP ({batch}) *
                    FROM catalog.OutboxMessages WITH (ROWLOCK, UPDLOCK, READPAST)
                    WHERE [Status] = 'Pending' AND NextAttemptAt <= {now} AND (LeasedUntil IS NULL OR LeasedUntil <= {now})
                    ORDER BY NextAttemptAt, Id)
                UPDATE due SET LeasedUntil = {until}, LeaseId = {lease}, Attempts = Attempts + 1
                OUTPUT inserted.Id, inserted.EventType, inserted.SchemaVersion, inserted.OccurredAt, inserted.Payload, inserted.Attempts
                """)
            .ToListAsync(cancellationToken);

        return new OutboxLease(lease, [.. messages.OrderBy(message => message.OccurredAt).ThenBy(message => message.Id)]);
    }

    /// <summary>Delivers the messages of a lease one after the other, and returns what happened to them.</summary>
    /// <param name="lease">The lease that <see cref="ClaimAsync"/> returned.</param>
    /// <param name="cancellationToken">A token that stops the delivery. The message in progress and the rest keep their lease.</param>
    public async Task<OutboxBatchResult> DeliverAsync(OutboxLease lease, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);

        int delivered = 0, failed = 0, deadLettered = 0;

        foreach (var message in lease.Messages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using var context = await _contexts.CreateDbContextAsync(cancellationToken);
            Exception? failure = null;

            try
            {
                await HandleAsync(context, message, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
#pragma warning disable CA1031 // A handler is outside code. Any fault of it fails the delivery of this message and never the pass.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                failure = exception;
            }

            if (failure is null)
            {
                if (await CompleteAsync(context, lease.LeaseId, message, cancellationToken))
                {
                    delivered++;
                }
            }
            else if (await FailAsync(context, lease.LeaseId, message, failure, cancellationToken))
            {
                if (message.Attempts >= _options.MaxAttempts)
                {
                    deadLettered++;
                }
                else
                {
                    failed++;
                }
            }
        }

        return new OutboxBatchResult(lease.Messages.Count, delivered, failed, deadLettered);
    }

    private async Task HandleAsync(CatalogDbContext context, ClaimedMessage message, CancellationToken cancellationToken)
    {
        var taken = await context.OutboxReceipts.AsNoTracking().Where(receipt => receipt.MessageId == message.Id).Select(receipt => receipt.Handler).ToListAsync(cancellationToken);
        var envelope = new OutboxMessageEnvelope(message.Id, message.EventType, message.SchemaVersion, message.OccurredAt, message.Payload);

        foreach (var handler in _handlers.Where(handler => !taken.Contains(handler.Name, StringComparer.Ordinal)))
        {
            await handler.HandleAsync(envelope, cancellationToken);
            await RecordReceiptAsync(context, message.Id, handler.Name, cancellationToken);
        }
    }

    private async Task RecordReceiptAsync(CatalogDbContext context, Guid messageId, string handler, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();

        try
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO catalog.OutboxReceipts (MessageId, Handler, ProcessedAt) VALUES ({messageId}, {handler}, {now})", cancellationToken);
        }
        catch (SqlException exception) when (exception.Number is 2627 or 2601)
        {
            // Another delivery of the same message recorded the receipt first, which is what this one wanted.
        }
    }

    private async Task<bool> CompleteAsync(CatalogDbContext context, Guid leaseId, ClaimedMessage message, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var done = nameof(OutboxStatus.Done);
        var changed = await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE catalog.OutboxMessages SET [Status] = {done}, CompletedAt = {now}, LeasedUntil = NULL, LeaseId = NULL, LastError = NULL WHERE Id = {message.Id} AND LeaseId = {leaseId}",
            cancellationToken);

        if (changed == 0)
        {
            LogLeaseLost(message.Id, message.EventType);

            return false;
        }

        _metrics.Delivered(message.EventType);
        LogDelivered(message.Id, message.EventType, message.Attempts);

        return true;
    }

    private async Task<bool> FailAsync(CatalogDbContext context, Guid leaseId, ClaimedMessage message, Exception failure, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var dead = message.Attempts >= _options.MaxAttempts;
        var status = dead ? nameof(OutboxStatus.DeadLetter) : nameof(OutboxStatus.Pending);

        // Jitter needs no secrecy, so the shared random source is the right tool.
#pragma warning disable CA5394
        var next = now + OutboxBackoff.Delay(message.Attempts, _options, _random.NextDouble());
#pragma warning restore CA5394

        // The stored text names the type of the exception and never its message, because the message of a handler can hold a value that must not reach a table.
        var error = failure.GetType().Name;
        var changed = await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE catalog.OutboxMessages SET [Status] = {status}, NextAttemptAt = {next}, LeasedUntil = NULL, LeaseId = NULL, LastError = {error} WHERE Id = {message.Id} AND LeaseId = {leaseId}",
            cancellationToken);

        if (changed == 0)
        {
            LogLeaseLost(message.Id, message.EventType);

            return false;
        }

        if (dead)
        {
            _metrics.DeadLettered(message.EventType);
            LogDeadLettered(failure, message.Id, message.EventType, message.Attempts);
        }
        else
        {
            _metrics.Failed(message.EventType);
            LogFailed(failure, message.Id, message.EventType, message.Attempts);
        }

        return true;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Outbox message {MessageId} of type {EventType} was delivered on attempt {Attempts}.")]
    private partial void LogDelivered(Guid messageId, string eventType, int attempts);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Outbox message {MessageId} of type {EventType} failed on attempt {Attempts} and will be tried again.")]
    private partial void LogFailed(Exception exception, Guid messageId, string eventType, int attempts);

    [LoggerMessage(EventId = 3, Level = LogLevel.Error, Message = "Outbox message {MessageId} of type {EventType} moved to the dead-letter state after {Attempts} attempts and needs a person.")]
    private partial void LogDeadLettered(Exception exception, Guid messageId, string eventType, int attempts);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "The lease of outbox message {MessageId} of type {EventType} ended before the delivery finished, so another processor holds it now.")]
    private partial void LogLeaseLost(Guid messageId, string eventType);
}
