using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Catalog.Infrastructure.Persistence;

/// <summary>
/// Gives the <c>dotnet ef</c> tool a context to read the model from, without a running application or a database (migrations, the bundle, and the idempotent script are all generated from the model).
/// The connection string is a placeholder that holds no credential, because the tool never opens a connection to generate them. The bundle takes the connection string of the database to migrate
/// as an argument of its own (docs/reference/persistence.md).
/// </summary>
public sealed class CatalogDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    private const string DesignTimeConnectionString = "Server=localhost;Database=catalog-design-time;Encrypt=true;TrustServerCertificate=true";

    /// <inheritdoc />
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<CatalogDbContext>();
        builder.UseCatalogSqlServer(DesignTimeConnectionString);

        return new CatalogDbContext(builder.Options);
    }
}
