using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Catalog.Infrastructure.Caching;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using StackExchange.Redis;
using Xunit;

[assembly: AssemblyFixture(typeof(Catalog.Infrastructure.Tests.Support.ValkeyFixture))]

namespace Catalog.Infrastructure.Tests.Support;

/// <summary>
/// One Valkey server in a container, from the image that <c>compose.yaml</c> pins by digest, so the local stack and the tests run the same server (ADR-0034, ADR-0038). The default user needs a generated password,
/// so no port that Docker publishes serves an open server, and the password exists only for the life of the container. A test that stops and starts the server asks for a fixed host port, because a published port
/// that the daemon picks may change when the container starts again. The server keeps nothing on disk, so a restart returns an empty server, which is what an outage that loses the data looks like.
/// </summary>
public sealed class ValkeyServer : IAsyncDisposable
{
    private const int ServerPort = 6379;

    private readonly IContainer _container;
    private readonly int? _fixedPort;

    private ValkeyServer(IContainer container, string adminPassword, int? fixedPort)
    {
        _container = container;
        AdminPassword = adminPassword;
        _fixedPort = fixedPort;
    }

    /// <summary>Gets the password of the default user, which only the tests use to prepare and inspect the server.</summary>
    public string AdminPassword { get; }

    /// <summary>Gets the host name or the address that the tests use to reach the server.</summary>
    public string Host => _container.Hostname;

    /// <summary>Gets the port of the server on the host.</summary>
    public int Port => _fixedPort ?? _container.GetMappedPublicPort(ServerPort);

    /// <summary>Gets the address of the server as <c>host:port</c>.</summary>
    public string Endpoint => $"{Host}:{Port}";

    /// <summary>Starts a server.</summary>
    /// <param name="fixedPort">Whether the server keeps its host port when it is stopped and started again.</param>
    public static async Task<ValkeyServer> StartAsync(bool fixedPort = false)
    {
        // The settings are the ones of the SQL Server fixture, because Testcontainers keeps them for the process and a fixture may start first.
        TestcontainersSettings.ResourceReaperImage = new DockerImage(SqlServerFixture.ReaperImage);
        TestcontainersSettings.ResourceReaperPrivilegedModeEnabled = false;

        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        int? port = fixedPort ? FreePort() : null;

        var builder = new ContainerBuilder(ComposeImage.Valkey())
            .WithCommand("valkey-server", "--requirepass", password, "--save", string.Empty, "--appendonly", "no", "--maxmemory", "128mb", "--maxmemory-policy", "allkeys-lru")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Ready to accept connections"));
        builder = port is { } fixedHostPort ? builder.WithPortBinding(fixedHostPort, ServerPort) : builder.WithPortBinding(ServerPort, true);

        var container = builder.Build();
        await container.StartAsync();

        return new ValkeyServer(container, password, port);
    }

    /// <summary>Stops the server, so every connection to it fails and a new one is refused.</summary>
    public Task StopAsync() => _container.StopAsync();

    /// <summary>Starts the server again after <see cref="StopAsync"/>. It comes back empty.</summary>
    public Task RestartAsync() => _container.StartAsync();

    /// <summary>Opens a connection as the default user, to prepare the server and to read what the code under test left in it.</summary>
    public async Task<ConnectionMultiplexer> ConnectAdminAsync()
    {
        var options = new ConfigurationOptions { Password = AdminPassword, AbortOnConnectFail = true, ConnectTimeout = 10_000, SyncTimeout = 10_000, AsyncTimeout = 10_000, AllowAdmin = true };
        options.EndPoints.Add(Host, Port);

        return await ConnectionMultiplexer.ConnectAsync(options);
    }

    /// <summary>Makes the settings that connect to this server as a user.</summary>
    /// <param name="userName">The name of the user.</param>
    /// <param name="password">The password of the user.</param>
    public ValkeyOptions OptionsFor(string userName, string password) => new() { Endpoint = Endpoint, UserName = userName, Password = password };

    /// <summary>Makes the settings that connect as the default user, which the tests that do not test the access control list use.</summary>
    public ValkeyOptions DefaultUserOptions() => OptionsFor("default", AdminPassword);

    /// <summary>
    /// Creates the application user with the rules that <c>compose.yaml</c> gives it, and returns the settings that connect as that user.
    /// </summary>
    /// <param name="userName">The name of the user. A test uses a name of its own, so tests that run in parallel never change the rules of each other.</param>
    public async Task<(ValkeyOptions Options, string Password)> CreateApplicationUserAsync(string userName)
    {
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var rules = ComposeImage.ValkeyApplicationRules(password);

        await using var admin = await ConnectAdminAsync();
        await admin.GetDatabase().ExecuteAsync("ACL", ["SETUSER", userName, "reset", .. rules]);

        return (OptionsFor(userName, password), password);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    // The port is free when it is chosen, and a container binds it a moment later. This is the same approach that spike 1.b used for its outage test.
    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}

/// <summary>One Valkey server for the whole test assembly (ADR-0034, ADR-0038). A test that stops the server starts one of its own with <see cref="ValkeyServer.StartAsync"/>.</summary>
public sealed class ValkeyFixture : IAsyncLifetime
{
    private ValkeyServer? _server;

    /// <summary>Gets the server.</summary>
    public ValkeyServer Server => _server ?? throw new InvalidOperationException("The container has not started.");

    /// <inheritdoc />
    public async ValueTask InitializeAsync() => _server = await ValkeyServer.StartAsync();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_server is not null)
        {
            await _server.DisposeAsync();
        }
    }
}
