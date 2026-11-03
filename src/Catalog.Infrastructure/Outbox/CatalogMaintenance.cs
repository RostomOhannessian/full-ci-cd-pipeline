using Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Catalog.Infrastructure.Outbox;

/// <summary>
/// The cleanup job of the persistence layer (plan section 8.4): it deletes the idempotency records that expired and the outbox messages that finished more than the retention ago, and their receipts go with them. A
/// dead-letter message stays until a person acts on it. One pass deletes in batches of 1,000 rows, so a large backlog never holds a long lock, and it returns the number of rows that it deleted.
/// </summary>
/// <param name="contexts">Makes the context of a pass.</param>
/// <param name="options">The settings.</param>
/// <param name="clock">The source of the time.</param>
public sealed class CatalogMaintenance(IDbContextFactory<CatalogDbContext> contexts, IOptions<OutboxOptions> options, TimeProvider clock)
{
    private const int DeleteBatch = 1000;

    private readonly IDbContextFactory<CatalogDbContext> _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
    private readonly OutboxOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <summary>Deletes the expired idempotency records and the finished outbox messages that are past their retention.</summary>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    public async Task<int> PurgeAsync(CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var cutoff = now - _options.Retention;
        var done = nameof(OutboxStatus.Done);
        var total = 0;

        await using var context = await _contexts.CreateDbContextAsync(cancellationToken);

        int deleted;
        do
        {
            deleted = await context.Database.ExecuteSqlInterpolatedAsync($"DELETE TOP ({DeleteBatch}) FROM catalog.IdempotencyRecords WHERE ExpiresAt <= {now}", cancellationToken);
            total += deleted;
        }
        while (deleted == DeleteBatch);

        do
        {
            deleted = await context.Database.ExecuteSqlInterpolatedAsync($"DELETE TOP ({DeleteBatch}) FROM catalog.OutboxMessages WHERE [Status] = {done} AND CompletedAt < {cutoff}", cancellationToken);
            total += deleted;
        }
        while (deleted == DeleteBatch);

        return total;
    }
}
