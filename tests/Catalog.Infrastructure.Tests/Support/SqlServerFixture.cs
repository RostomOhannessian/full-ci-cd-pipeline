using System.Security.Cryptography;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Images;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Xunit;

[assembly: AssemblyFixture(typeof(Catalog.Infrastructure.Tests.Support.SqlServerFixture))]

namespace Catalog.Infrastructure.Tests.Support;

/// <summary>
/// One SQL Server 2022 container for the whole test assembly (ADR-0034, plan section 10.1). Starting it takes about six seconds (spike 1.d), so the container starts once, and each test class gets a database of its own
/// from <see cref="CreateDatabaseAsync"/>. The image is the digest that <c>compose.yaml</c> pins, the password is generated for each run, and the resource reaper of Testcontainers is pinned by digest too, so no image of
/// the suite floats (plan section 9). The reaper runs without the privileged flag, which it needs only on hosts that confine the Docker socket with SELinux.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    // The image of the resource reaper that Testcontainers 4.15.0 starts by default, written down so that a newer package cannot move it without a change here (checked 2026-11-01).
    internal const string ReaperImage = "testcontainers/ryuk:0.14.0@sha256:7c1a8a9a47c780ed0f983770a662f80deb115d95cce3e2daa3d12115b8cd28f0";

    private MsSqlContainer? _container;
    private int _databases;

    /// <summary>Gets the connection string of the <c>master</c> database with the <c>sa</c> login, which only the fixtures use.</summary>
    public string ServerConnectionString => _container?.GetConnectionString() ?? throw new InvalidOperationException("The container has not started.");

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        TestcontainersSettings.ResourceReaperImage = new DockerImage(ReaperImage);
        TestcontainersSettings.ResourceReaperPrivilegedModeEnabled = false;

        // SQL Server needs a password with upper case, lower case, digits, and a symbol. The prefix guarantees the classes, and the random tail is the secret.
        var password = "Aa1!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

        _container = new MsSqlBuilder(ComposeImage.SqlServer())
            .WithPassword(password)
            .Build();

        await _container.StartAsync();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        SqlConnection.ClearAllPools();

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>Creates an empty database with a name that no other class of the run uses, and returns the connection string of the <c>sa</c> login for it.</summary>
    /// <param name="prefix">A short word that names the class that owns the database.</param>
    public async Task<(string Name, string ConnectionString)> CreateDatabaseAsync(string prefix)
    {
        var name = $"{prefix}_{Interlocked.Increment(ref _databases)}";

        await ExecuteAsync(ServerConnectionString, $"CREATE DATABASE [{name}]");

        return (name, new SqlConnectionStringBuilder(ServerConnectionString) { InitialCatalog = name }.ConnectionString);
    }

    /// <summary>Drops a database, and the sessions that still use it.</summary>
    /// <param name="name">The name that <see cref="CreateDatabaseAsync"/> returned.</param>
    public Task DropDatabaseAsync(string name) =>
        ExecuteAsync(ServerConnectionString, $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]");

    /// <summary>Runs one batch of SQL.</summary>
    /// <param name="connectionString">The connection string.</param>
    /// <param name="sql">The statements, with no <c>GO</c> separator.</param>
    public static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
