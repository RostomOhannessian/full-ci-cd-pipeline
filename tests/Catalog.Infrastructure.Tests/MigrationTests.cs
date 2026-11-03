using System.Text.RegularExpressions;
using AwesomeAssertions;
using Catalog.Domain.Tests.Support;
using Catalog.Infrastructure.Persistence;
using Catalog.Infrastructure.Tests.Support;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Catalog.Infrastructure.Tests;

/// <summary>
/// REQ-DATA-006 and REQ-DATA-001: the migration is forward-only SQL that a reviewer can read, it can be applied twice, and only the identity of the migrator can change the schema. The idempotent script is what
/// <c>dotnet ef migrations script --idempotent</c> writes, and the bundle runs the same migrations (docs/reference/persistence.md). Application replicas never migrate: an architecture test holds that rule.
/// </summary>
public sealed partial class MigrationTests(SqlServerFixture server) : IAsyncLifetime
{
    private readonly List<string> _databases = [];

    /// <inheritdoc />
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        foreach (var database in _databases)
        {
            await server.DropDatabaseAsync(database);
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-006")]
    public async Task The_idempotent_script_builds_the_schema_and_runs_again_without_an_error()
    {
        var (name, connection) = await NewDatabaseAsync("script");
        var script = IdempotentScript(connection);

        await RunScriptAsync(connection, script);
        await RunScriptAsync(connection, script);

        var tables = await SqlProbe.RowsAsync(connection, "SELECT name FROM sys.tables WHERE schema_id = SCHEMA_ID('catalog') ORDER BY name");
        tables.Select(row => row[0]).Should().Equal("__EFMigrationsHistory", "Categories", "IdempotencyRecords", "OutboxMessages", "OutboxReceipts", "Products");
        (await SqlProbe.ScalarAsync<int>(connection, "SELECT COUNT(*) FROM catalog.__EFMigrationsHistory")).Should().BeGreaterThan(0);
        name.Should().NotBeEmpty();
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-006")]
    public async Task The_script_gives_the_same_schema_as_the_migrations_and_leaves_nothing_for_the_migrator_to_do()
    {
        var (_, scripted) = await NewDatabaseAsync("scripted");
        var (_, migrated) = await NewDatabaseAsync("migrated");
        await RunScriptAsync(scripted, IdempotentScript(scripted));
        await using (var context = Context(migrated))
        {
            await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        }

        const string columns = "SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE, IS_NULLABLE, CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'catalog' ORDER BY TABLE_NAME, COLUMN_NAME";
        const string indexes = "SELECT OBJECT_NAME(object_id), name, is_unique, filter_definition FROM sys.indexes WHERE object_id IN (SELECT object_id FROM sys.tables WHERE schema_id = SCHEMA_ID('catalog')) AND name IS NOT NULL ORDER BY 1, 2";

        (await SqlProbe.RowsAsync(scripted, columns)).Select(row => string.Join('|', row)).Should().Equal((await SqlProbe.RowsAsync(migrated, columns)).Select(row => string.Join('|', row)));
        (await SqlProbe.RowsAsync(scripted, indexes)).Select(row => string.Join('|', row)).Should().Equal((await SqlProbe.RowsAsync(migrated, indexes)).Select(row => string.Join('|', row)));

        await using var after = Context(scripted);
        (await after.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken)).Should().BeEmpty();
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-006")]
    public async Task The_script_is_the_same_text_every_time_it_is_generated_so_a_reviewer_can_compare_it()
    {
        var (_, connection) = await NewDatabaseAsync("reproducible");

        IdempotentScript(connection).Should().Be(IdempotentScript(connection));
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-006")]
    public async Task Only_the_migrator_identity_can_change_the_schema_and_the_application_identity_can_only_use_it()
    {
        var (name, admin) = await NewDatabaseAsync("identities");
        var migrator = await CreateLoginAsync(name, admin, "migrator", "db_ddladmin", "db_datareader", "db_datawriter");
        var application = await CreateLoginAsync(name, admin, "application", "db_datareader", "db_datawriter");

        // The application identity cannot create the schema, so it cannot migrate.
        await using (var denied = Context(application))
        {
            await denied.Invoking(context => context.Database.MigrateAsync(TestContext.Current.CancellationToken)).Should().ThrowAsync<SqlException>();
        }

        (await SqlProbe.ScalarAsync<int>(admin, "SELECT COUNT(*) FROM sys.schemas WHERE name = 'catalog'")).Should().Be(0);

        // The migrator identity can.
        await using (var allowed = Context(migrator))
        {
            await allowed.Database.MigrateAsync(TestContext.Current.CancellationToken);
        }

        // After the migration the application identity reads and writes rows, and it still cannot change a table or create one.
        await using (var context = Context(application))
        {
            context.Categories.Add(Make.Category(Counters.Next()));
            (await context.SaveChangesAsync(TestContext.Current.CancellationToken)).Should().Be(1);
        }

        var alter = () => SqlServerFixture.ExecuteAsync(application, "ALTER TABLE catalog.Categories ADD Scratch int NULL");
        var create = () => SqlServerFixture.ExecuteAsync(application, "CREATE TABLE catalog.Scratch (Id int NOT NULL)");
        // SQL Server reports a missing permission to alter an object as 1088, which hides whether the object exists, and a missing permission to create one as 262.
        (await alter.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(1088);
        (await create.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(262);
    }

    private async Task<(string Name, string ConnectionString)> NewDatabaseAsync(string prefix)
    {
        var database = await server.CreateDatabaseAsync(prefix);
        _databases.Add(database.Name);

        return database;
    }

    private static CatalogDbContext Context(string connectionString)
    {
        var builder = new DbContextOptionsBuilder<CatalogDbContext>();
        builder.UseCatalogSqlServer(connectionString);

        return new CatalogDbContext(builder.Options);
    }

    private static string IdempotentScript(string connectionString)
    {
        using var context = Context(connectionString);

        return context.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
    }

    // The script separates batches with GO, which sqlcmd understands and the SqlClient does not, so each batch runs on its own.
    private static async Task RunScriptAsync(string connectionString, string script)
    {
        foreach (var batch in BatchSeparator().Split(script).Where(batch => !string.IsNullOrWhiteSpace(batch)))
        {
            await SqlServerFixture.ExecuteAsync(connectionString, batch);
        }
    }

    private async Task<string> CreateLoginAsync(string database, string adminConnectionString, string role, params string[] roles)
    {
        var login = $"{database}_{role}";
        var password = TestPasswords.Next();

        await SqlServerFixture.ExecuteAsync(server.ServerConnectionString, $"CREATE LOGIN [{login}] WITH PASSWORD = '{password}', CHECK_POLICY = OFF");
        await SqlServerFixture.ExecuteAsync(adminConnectionString, $"CREATE USER [{login}] FOR LOGIN [{login}]");
        foreach (var databaseRole in roles)
        {
            await SqlServerFixture.ExecuteAsync(adminConnectionString, $"ALTER ROLE [{databaseRole}] ADD MEMBER [{login}]");
        }

        return new SqlConnectionStringBuilder(adminConnectionString) { UserID = login, Password = password }.ConnectionString;
    }

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex BatchSeparator();
}
