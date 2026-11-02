using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Catalog.Infrastructure.Persistence;

/// <summary>The one place that says how the catalog uses SQL Server, so the application, the design-time tool, and the tests configure a context in the same way.</summary>
public static class CatalogSqlServerOptions
{
    /// <summary>Configures a context to use SQL Server with the history of the migrations in the schema of the catalog.</summary>
    /// <param name="builder">The options of the context.</param>
    /// <param name="connectionString">The connection string.</param>
    public static DbContextOptionsBuilder UseCatalogSqlServer(this DbContextOptionsBuilder builder, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return builder.UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable(HistoryRepository.DefaultTableName, CatalogDbContext.Schema));
    }
}
