using Catalog.Application.Idempotency;
using Catalog.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Catalog.Infrastructure.Idempotency;

/// <summary>
/// Implements <see cref="IIdempotencyStore"/> over the table <c>catalog.IdempotencyRecords</c> (plan section 8.4, ADR-0036). A key belongs to one caller, and a record holds the fingerprint of the request, the status,
/// and the first result, and expires after 24 hours. <see cref="BeginAsync"/> and <see cref="ReleaseAsync"/> run outside the transaction of the command, as single statements that commit on their own, so a reservation is
/// visible to a second request at once. <see cref="CompleteAsync"/> runs inside the transaction of the command, through the hook of the unit of work, so the record of the first result exists exactly when the effect does.
/// The statements are parameterized and bypass the change tracker, so the store never saves the pending changes of the aggregates.
/// </summary>
/// <param name="context">The write context of the scope, which the unit of work shares.</param>
/// <param name="clock">The source of the time of the records.</param>
/// <param name="options">The settings.</param>
/// <param name="metrics">The metrics that count each decision, or null to count nothing.</param>
public sealed class IdempotencyStore(CatalogDbContext context, TimeProvider clock, IOptions<IdempotencyOptions> options, IdempotencyMetrics? metrics = null) : IIdempotencyStore
{
    // A violation of the primary key or of a unique index, which a second request with the same key causes.
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    // The loop covers a record that disappears between the insert and the read, which only the cleanup or a release of another request can cause. Three rounds are far more than that needs.
    private const int Rounds = 3;

    private readonly CatalogDbContext _context = context ?? throw new ArgumentNullException(nameof(context));
    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly IdempotencyOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public async Task<IdempotencyDecision> BeginAsync(IdempotencyRequest request, CancellationToken cancellationToken)
    {
        var decision = await DecideAsync(request, cancellationToken);
        metrics?.Decided(decision.Outcome);

        return decision;
    }

    private async Task<IdempotencyDecision> DecideAsync(IdempotencyRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var inProgress = nameof(IdempotencyStatus.InProgress);
        var completed = nameof(IdempotencyStatus.Completed);

        for (var round = 0; round < Rounds; round++)
        {
            var now = _clock.GetUtcNow();
            var expires = now + _options.Retention;
            var key = request.Key.Value;

            // A record that expired no longer counts, so it makes room for a new reservation.
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM catalog.IdempotencyRecords WHERE CallerId = {request.CallerId} AND [Key] = {key} AND ExpiresAt <= {now}",
                cancellationToken);

            try
            {
                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT INTO catalog.IdempotencyRecords (CallerId, [Key], Fingerprint, [Status], Response, CreatedAt, ExpiresAt)
                    VALUES ({request.CallerId}, {key}, {request.Fingerprint}, {inProgress}, NULL, {now}, {expires})
                    """,
                    cancellationToken);

                return new IdempotencyDecision(IdempotencyOutcome.Reserved);
            }
            catch (SqlException exception) when (exception.Number is UniqueIndexViolation or UniqueConstraintViolation)
            {
                // Another request holds the key. The record decides what this one is.
            }

            var found = (await _context.Database
                .SqlQuery<StoredRequest>($"SELECT Fingerprint, [Status] AS Status, Response, CreatedAt FROM catalog.IdempotencyRecords WHERE CallerId = {request.CallerId} AND [Key] = {key}")
                .ToListAsync(cancellationToken)).SingleOrDefault();

            if (found is null)
            {
                continue;
            }

            if (!string.Equals(found.Fingerprint, request.Fingerprint, StringComparison.Ordinal))
            {
                return new IdempotencyDecision(IdempotencyOutcome.Reused);
            }

            if (string.Equals(found.Status, completed, StringComparison.Ordinal))
            {
                return new IdempotencyDecision(IdempotencyOutcome.Replay, found.Response);
            }

            if (found.CreatedAt + _options.InProgressTimeout > now)
            {
                return new IdempotencyDecision(IdempotencyOutcome.InProgress);
            }

            // The first request never finished, so the reservation is taken over. The condition on the old creation time makes the takeover a compare-and-swap: of two requests that try it, one wins.
            var taken = await _context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE catalog.IdempotencyRecords SET CreatedAt = {now}, ExpiresAt = {expires}
                WHERE CallerId = {request.CallerId} AND [Key] = {key} AND [Status] = {inProgress} AND CreatedAt = {found.CreatedAt}
                """,
                cancellationToken);

            if (taken == 1)
            {
                return new IdempotencyDecision(IdempotencyOutcome.Reserved);
            }
        }

        return new IdempotencyDecision(IdempotencyOutcome.InProgress);
    }

    /// <inheritdoc />
    public async Task CompleteAsync(IdempotencyRequest request, string response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);

        var inProgress = nameof(IdempotencyStatus.InProgress);
        var completed = nameof(IdempotencyStatus.Completed);
        var key = request.Key.Value;

        var changed = await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE catalog.IdempotencyRecords SET [Status] = {completed}, Response = {response}
            WHERE CallerId = {request.CallerId} AND [Key] = {key} AND Fingerprint = {request.Fingerprint} AND [Status] = {inProgress}
            """,
            cancellationToken);

        if (changed != 1)
        {
            // The reservation is gone, because another request took it over or the cleanup deleted it. Writing the effect without a record would let a retry run the command again, so the transaction rolls back.
            throw new InvalidOperationException("The idempotency reservation of this request no longer exists, so its result cannot be recorded in the transaction of the command.");
        }
    }

    /// <inheritdoc />
    public async Task ReleaseAsync(IdempotencyRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var inProgress = nameof(IdempotencyStatus.InProgress);
        var key = request.Key.Value;

        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM catalog.IdempotencyRecords WHERE CallerId = {request.CallerId} AND [Key] = {key} AND Fingerprint = {request.Fingerprint} AND [Status] = {inProgress}",
            cancellationToken);
    }
}

/// <summary>The part of a stored record that <see cref="IdempotencyStore"/> reads to decide what a request is.</summary>
public sealed class StoredRequest
{
    /// <summary>Gets or sets the fingerprint of the request that reserved the key.</summary>
    public string Fingerprint { get; set; } = string.Empty;

    /// <summary>Gets or sets the name of the status.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Gets or sets the first result, or null while the request runs.</summary>
    public string? Response { get; set; }

    /// <summary>Gets or sets when the key was reserved.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
