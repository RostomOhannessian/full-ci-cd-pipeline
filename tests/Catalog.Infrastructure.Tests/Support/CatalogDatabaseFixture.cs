using Catalog.Infrastructure.Persistence;
using Catalog.Infrastructure.Persistence.Reading;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Catalog.Infrastructure.Tests.Support;

/// <summary>
/// A database of its own for one test class, created on the shared container and migrated to the current model with the migrations that ship (ADR-0034). A class that needs a clean slate calls
/// <see cref="ResetAsync"/> at the start of a test, and a class that does not builds data that no other test of the class can see (identifiers come from counters).
/// </summary>
/// <param name="server">The container of the assembly.</param>
public sealed class CatalogDatabaseFixture(SqlServerFixture server) : IAsyncLifetime
{
    private string _name = string.Empty;

    /// <summary>Gets the connection string of the <c>sa</c> login for the database.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>Gets the container that hosts the database.</summary>
    public SqlServerFixture Server { get; } = server;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        (_name, var connectionString) = await Server.CreateDatabaseAsync("catalog");
        ConnectionString = connectionString;

        await using var context = NewContext();
        await context.Database.MigrateAsync();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await Server.DropDatabaseAsync(_name);
    }

    /// <summary>Creates a write context for the database, with the options that production uses.</summary>
    public CatalogDbContext NewContext()
    {
        var builder = new DbContextOptionsBuilder<CatalogDbContext>();
        builder.UseCatalogSqlServer(ConnectionString);

        return new CatalogDbContext(builder.Options);
    }

    /// <summary>Creates a read context for the database.</summary>
    public CatalogReadContext NewReadContext()
    {
        var builder = new DbContextOptionsBuilder<CatalogReadContext>();
        builder.UseCatalogSqlServer(ConnectionString).UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);

        return new CatalogReadContext(builder.Options);
    }

    /// <summary>Deletes every row of the catalog, children first, so a test starts from an empty database.</summary>
    public Task ResetAsync() =>
        SqlServerFixture.ExecuteAsync(
            ConnectionString,
            """
            DELETE FROM catalog.OutboxReceipts;
            DELETE FROM catalog.OutboxMessages;
            DELETE FROM catalog.IdempotencyRecords;
            DELETE FROM catalog.Products;
            DELETE FROM catalog.Categories;
            """);
}
