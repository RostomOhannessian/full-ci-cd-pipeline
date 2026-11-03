using Catalog.Application.Categories;
using Catalog.Application.Events;
using Catalog.Application.Idempotency;
using Catalog.Application.Persistence;
using Catalog.Application.Products;
using Catalog.Infrastructure.Credentials;
using Catalog.Infrastructure.Idempotency;
using Catalog.Infrastructure.Outbox;
using Catalog.Infrastructure.Persistence;
using Catalog.Infrastructure.Persistence.Reading;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Catalog.Infrastructure.Tests.Support;

/// <summary>
/// Logins, probes, and contexts for the credential tests. Every login has a name that no other test uses, so the pool of a login belongs to one test (SqlClient keys a pool by the connection string, credential
/// included), and a test counts the sessions of its own login in <c>sys.dm_exec_sessions</c>, which is the proof that a pool was cleared (spike 1.e).
/// </summary>
internal static class SqlLogins
{
    /// <summary>Creates a login that owns the database of the test class, and returns its credential.</summary>
    public static async Task<DatabaseCredential> CreateAsync(CatalogDatabaseFixture database, string label)
    {
        var login = $"wp18_{label}_{Counters.Next()}";
        var password = TestPasswords.Next();
        await SqlServerFixture.ExecuteAsync(database.Server.ServerConnectionString, $"CREATE LOGIN [{login}] WITH PASSWORD = '{password}', CHECK_POLICY = OFF");
        await SqlServerFixture.ExecuteAsync(database.ConnectionString, $"CREATE USER [{login}] FOR LOGIN [{login}]; ALTER ROLE db_owner ADD MEMBER [{login}]");

        return new DatabaseCredential(login, password);
    }

    /// <summary>Drops the user and the login, which is what a revocation statement does. It fails with error 15434 while the login still has a session.</summary>
    public static async Task DropAsync(CatalogDatabaseFixture database, DatabaseCredential credential)
    {
        await SqlServerFixture.ExecuteAsync(database.ConnectionString, $"DROP USER [{credential.UserName}]");
        await SqlServerFixture.ExecuteAsync(database.Server.ServerConnectionString, $"DROP LOGIN [{credential.UserName}]");
    }

    /// <summary>Gives the connection string of the database without a credential and with pooling on.</summary>
    public static string BaseConnectionString(CatalogDatabaseFixture database) =>
        new SqlConnectionStringBuilder(database.ConnectionString) { UserID = string.Empty, Password = string.Empty, Pooling = true }.ConnectionString;

    /// <summary>Counts the sessions that a login holds on the server.</summary>
    public static async Task<int> SessionsAsync(CatalogDatabaseFixture database, DatabaseCredential credential) =>
        await SqlProbe.ScalarAsync<int>(database.Server.ServerConnectionString, "SELECT COUNT(*) FROM sys.dm_exec_sessions WHERE login_name = @login", ("@login", credential.UserName));

    /// <summary>Creates a write context whose connections get their credential from the source.</summary>
    public static CatalogDbContext NewContext(CatalogDatabaseFixture database, ICredentialSource source)
    {
        var baseConnection = BaseConnectionString(database);
        var builder = new DbContextOptionsBuilder<CatalogDbContext>();
        builder.UseCatalogSqlServer(baseConnection).AddInterceptors(new CredentialConnectionInterceptor(source, baseConnection));

        return new CatalogDbContext(builder.Options);
    }

    /// <summary>Asks the server which login the connection of the context runs as.</summary>
    public static async Task<string> LoginOfAsync(CatalogDbContext context, CancellationToken cancellationToken) =>
        (await context.Database.SqlQueryRaw<string>("SELECT SUSER_SNAME() AS [Value]").ToListAsync(cancellationToken)).Single();

    /// <summary>Asks the server for the session of the connection of the context.</summary>
    public static async Task<int> SessionOfAsync(CatalogDbContext context, CancellationToken cancellationToken) =>
        (await context.Database.SqlQueryRaw<int>("SELECT CAST(@@SPID AS int) AS [Value]").ToListAsync(cancellationToken)).Single();

    /// <summary>
    /// Waits until a condition about the server holds. The server ends the session of a closed connection a moment after the client closed it, so the tests poll its session table with a bound of 30 seconds
    /// instead of reading it once. This is a poll of an external system and never a wait that makes a test pass: a condition that never holds fails the test.
    /// </summary>
    public static async Task EventuallyAsync(Func<Task<bool>> condition, string because, CancellationToken cancellationToken)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            while (!await condition())
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), limit.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new Xunit.Sdk.XunitException($"The condition did not hold within 30 seconds: {because}.");
        }
    }
}

/// <summary>A temporary directory with a credential file in it, for the tests of the rotating source. The files live in the temp folder of the machine and never in the repository.</summary>
internal sealed class TempCredentialFile : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("catalog-credential-").FullName;

    public TempCredentialFile() => Path = System.IO.Path.Combine(_directory, "sql.json");

    public string Path { get; }

    /// <summary>Writes the file in place.</summary>
    public void Write(string text) => File.WriteAllText(Path, text);

    /// <summary>Writes the credential as the agent renders it.</summary>
    public void Write(DatabaseCredential credential) => Write(Json(credential));

    /// <summary>Replaces the file the way an agent does: it writes a temporary file and renames it over the target.</summary>
    public void Replace(DatabaseCredential credential)
    {
        var temporary = System.IO.Path.Combine(_directory, "sql.json.tmp");
        File.WriteAllText(temporary, Json(credential));
        File.Move(temporary, Path, overwrite: true);
    }

    public static string Json(DatabaseCredential credential) => System.Text.Json.JsonSerializer.Serialize(new { username = credential.UserName, password = credential.Password });

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}

/// <summary>
/// The composition that the Api builds in WP1.10, written once here so a test can prove that the pieces of the persistence layer work together as a container wires them. The infrastructure project cannot hold
/// this code, because an architecture rule forbids <c>IServiceProvider</c> outside the composition root. The list is also the reference of the registrations in the persistence page.
/// </summary>
internal static class TestComposition
{
    public static IServiceCollection AddCatalogPersistence(this IServiceCollection services)
    {
        services.AddLogging();
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidator>();
        services.AddSingleton<IValidateOptions<OutboxOptions>, OutboxOptionsValidator>();
        services.AddSingleton<IValidateOptions<IdempotencyOptions>, IdempotencyOptionsValidator>();
        services.AddOptions<DatabaseOptions>().ValidateOnStart();
        services.AddOptions<OutboxOptions>().ValidateOnStart();
        services.AddOptions<IdempotencyOptions>().ValidateOnStart();

        services.AddSingleton<CredentialMetrics>();
        services.AddSingleton<ICredentialSource>(provider => CredentialSources.Create(Database(provider), provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<ILoggerFactory>()));
        services.AddSingleton(provider => new CredentialConnectionInterceptor(provider.GetRequiredService<ICredentialSource>(), Database(provider).ConnectionString));
        services.AddSingleton(provider => new OutboxInterceptor(provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton(provider => new CredentialPoolJanitor(
            provider.GetRequiredService<ICredentialSource>(),
            Database(provider).ConnectionString,
            Database(provider).PoolClearGrace,
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<CredentialMetrics>(),
            provider.GetRequiredService<ILogger<CredentialPoolJanitor>>()));
        services.AddHostedService(provider => provider.GetRequiredService<CredentialPoolJanitor>());

        services.AddDbContextFactory<CatalogDbContext>((provider, builder) => builder
            .UseCatalogSqlServer(Database(provider).ConnectionString)
            .AddInterceptors(provider.GetRequiredService<CredentialConnectionInterceptor>(), provider.GetRequiredService<OutboxInterceptor>()));
        services.AddDbContextFactory<CatalogReadContext>((provider, builder) => builder
            .UseCatalogSqlServer(Database(provider).ConnectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .AddInterceptors(provider.GetRequiredService<CredentialConnectionInterceptor>()));
        services.AddScoped(provider => provider.GetRequiredService<IDbContextFactory<CatalogDbContext>>().CreateDbContext());
        services.AddScoped(provider => provider.GetRequiredService<IDbContextFactory<CatalogReadContext>>().CreateDbContext());

        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IProductReadStore, ProductReadStore>();
        services.AddScoped<ICategoryReadStore, CategoryReadStore>();
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();

        services.AddSingleton<OutboxSignal>();
        services.AddSingleton<IOutboxDispatcher, OutboxDispatcher>();
        services.AddSingleton<OutboxMetrics>();
        services.AddSingleton<OutboxProcessor>();
        services.AddSingleton<CatalogMaintenance>();
        services.AddHostedService<OutboxBackgroundService>();

        return services;
    }

    private static DatabaseOptions Database(IServiceProvider provider) => provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
}
