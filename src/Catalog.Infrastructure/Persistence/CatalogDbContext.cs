using Catalog.Application.Events;
using Catalog.Domain.Categories;
using Catalog.Domain.Products;
using Catalog.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Infrastructure.Persistence;

/// <summary>
/// The write model of the catalog: the two aggregates and the three tables that keep the outbox, its receipts, and the idempotency records, all in the schema <c>catalog</c> (plan section 8.4). The Domain has no
/// persistence attributes, so <see cref="OnModelCreating"/> holds every mapping through the configuration classes of <c>Catalog.Infrastructure.Persistence.Mapping</c>. The migrations are generated from this
/// context, and the history table lives in the same schema. Queries for the query side go through <see cref="Reading.CatalogReadContext"/>.
/// </summary>
/// <param name="options">The options, which choose the provider, the connection, and the interceptors.</param>
public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    /// <summary>The name of the schema that holds every table of the catalog and the history of the migrations.</summary>
    public const string Schema = "catalog";

    private readonly List<OutboxMessageEnvelope> _committed = [];

    /// <summary>Gets the products.</summary>
    public DbSet<Product> Products => Set<Product>();

    /// <summary>Gets the categories.</summary>
    public DbSet<Category> Categories => Set<Category>();

    /// <summary>Gets the messages of the outbox.</summary>
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <summary>Gets the receipts of the handlers.</summary>
    public DbSet<OutboxReceipt> OutboxReceipts => Set<OutboxReceipt>();

    /// <summary>Gets the idempotency records.</summary>
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    /// <summary>Keeps the messages that a save wrote, until the dispatcher of the scope takes them after the commit (ADR-0039). A rolled-back run discards them.</summary>
    /// <param name="messages">The messages that the save wrote.</param>
    internal void Stage(IEnumerable<OutboxMessageEnvelope> messages) => _committed.AddRange(messages);

    /// <summary>Returns the messages that the saves of this scope wrote, oldest first, and forgets them, so a second call returns none.</summary>
    internal IReadOnlyList<OutboxMessageEnvelope> TakeStaged()
    {
        var taken = _committed.ToArray();
        _committed.Clear();

        return taken;
    }

    /// <summary>Forgets the messages that a run staged, because its transaction did not commit.</summary>
    internal void DiscardStaged() => _committed.Clear();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.ApplyConfiguration(new CategoryConfiguration());
        modelBuilder.ApplyConfiguration(new ProductConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxReceiptConfiguration());
        modelBuilder.ApplyConfiguration(new IdempotencyRecordConfiguration());
    }
}
