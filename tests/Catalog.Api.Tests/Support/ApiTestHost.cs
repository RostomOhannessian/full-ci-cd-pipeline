using System.Net;
using System.Net.Sockets;
using Catalog.Api.Composition;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Time.Testing;

namespace Catalog.Api.Tests.Support;

/// <summary>The settings that a test host reads, written as the configuration keys of the API. A test starts from the defaults and changes the keys that it is about.</summary>
internal sealed class HostSettings
{
    /// <summary>Sentinel passwords for the tests. They are low-entropy on purpose, so the secret scan of the repository does not flag them, and a test searches every answer and log line for them.</summary>
    public const string DatabasePassword = "Pw-Secret-database";

    /// <summary>The sentinel password of the Valkey user.</summary>
    public const string ValkeyPassword = "Pw-Secret-valkey";

    private readonly Dictionary<string, string?> _values;

    public HostSettings(TestIssuer issuer)
        : this(issuer?.Issuer ?? throw new ArgumentNullException(nameof(issuer)))
    {
    }

    /// <summary>Starts from the defaults and names an issuer that is not the in-process one, such as a real Keycloak.</summary>
    public HostSettings(string issuer)
    {
        _values = new()
        {
            ["Catalog:Auth:Issuer"] = issuer,
            ["Catalog:Auth:RequireHttpsMetadata"] = "false",
            ["Catalog:Auth:MetadataRefreshInterval"] = "00:00:01",

            // No database is reachable by default. The outbox and the maintenance job wait an hour before they look, so a test that needs no database never starts a conversation with one.
            ["Catalog:Database:ConnectionString"] = "Server=127.0.0.1,1;Database=catalog;Encrypt=False;Connect Timeout=1",
            ["Catalog:Database:UserName"] = "sa",
            ["Catalog:Database:Password"] = DatabasePassword,
            ["Catalog:Outbox:PollInterval"] = "01:00:00",
            ["Catalog:Outbox:MaintenanceInterval"] = "01:00:00",

            // A Valkey server that nobody runs. The connection never aborts, so the host starts, and the cache degrades when it is used.
            ["Catalog:Valkey:Endpoint"] = "127.0.0.1:1",
            ["Catalog:Valkey:UserName"] = "catalog-test",
            ["Catalog:Valkey:Password"] = ValkeyPassword,
            ["Catalog:Valkey:ConnectTimeout"] = "00:00:00.200",
            ["Catalog:Valkey:CommandTimeout"] = "00:00:00.500",
            ["Catalog:Cache:SharedTimeout"] = "00:00:00.200",

            ["Catalog:Limits:ReadPermits"] = "100000",
            ["Catalog:Limits:WritePermits"] = "100000",
        };
    }

    public HostSettings With(string key, string? value)
    {
        _values[key] = value;

        return this;
    }

    /// <summary>Points the database settings at a database of a test class, with the credential split from the connection string as the API requires.</summary>
    public HostSettings WithDatabase(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var user = builder.UserID;
        var password = builder.Password;
        builder.Remove("User ID");
        builder.Remove("Password");

        _values["Catalog:Database:ConnectionString"] = builder.ConnectionString;
        _values["Catalog:Database:UserName"] = user;
        _values["Catalog:Database:Password"] = password;

        return this;
    }

    public IReadOnlyDictionary<string, string?> Values => _values;
}

/// <summary>A logger provider that keeps every line of every category, so a test can prove that no token and no credential reached a log.</summary>
internal sealed class LogCapture : ILoggerProvider
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _lines = new();

    public IReadOnlyList<string> Lines => [.. _lines];

    public ILogger CreateLogger(string categoryName) => new Capture(categoryName, _lines);

    public void Dispose()
    {
    }

    private sealed class Capture(string category, System.Collections.Concurrent.ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            lines.Enqueue($"{logLevel} {category}: {formatter(state, exception)}{(exception is null ? string.Empty : $" [{exception}]")}");
    }
}

/// <summary>Sets the address of the connection from a header, so a test chooses who the caller is. TestServer has no socket, and the address is what the forwarded headers and the rate limiter read.</summary>
internal sealed class RemoteAddressStartupFilter : IStartupFilter
{
    public const string Header = "X-Test-Remote-Address";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, following) =>
        {
            if (context.Request.Headers.TryGetValue(Header, out var address) && IPAddress.TryParse(address, out var parsed))
            {
                context.Connection.RemoteIpAddress = parsed;
            }

            return following(context);
        });
        next(app);
    };
}

/// <summary>
/// The real composition root of the API, started in process for a test (plan section 10.1). The host is built with <see cref="CatalogComposition"/> and <see cref="CatalogPipeline"/>, which is everything that
/// <c>Program</c> does, and it reads its configuration from memory only, so no file or environment variable of the machine changes what a test sees. By default the requests go through TestServer, and a test that needs
/// the two ports starts real sockets. A test adds endpoints of its own through the <c>map</c> argument of <see cref="StartAsync"/>, for the behavior that no endpoint of the catalog has yet or that the catalog must never have.
/// </summary>
internal sealed class ApiTestHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private ApiTestHost(WebApplication app, LogCapture log, FakeTimeProvider clock, int publicPort, int managementPort)
    {
        _app = app;
        Log = log;
        Clock = clock;
        PublicPort = publicPort;
        ManagementPort = managementPort;
    }

    public LogCapture Log { get; }

    public FakeTimeProvider Clock { get; }

    public int PublicPort { get; }

    public int ManagementPort { get; }

    public IServiceProvider Services => _app.Services;

    /// <summary>Gets a client that sends requests through TestServer. It works only when the host was started without sockets.</summary>
    public HttpClient Client => _app.GetTestClient();

    public HttpClient PublicClient => new() { BaseAddress = new Uri($"http://127.0.0.1:{PublicPort}") };

    public HttpClient ManagementClient => new() { BaseAddress = new Uri($"http://127.0.0.1:{ManagementPort}") };

    public static async Task<ApiTestHost> StartAsync(
        HostSettings settings,
        Action<WebApplication>? map = null,
        bool sockets = false,
        string environment = "Development",
        Action<IServiceCollection>? services = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero));
        var log = new LogCapture();
        var publicPort = 0;
        var managementPort = 0;

        if (sockets)
        {
            publicPort = FreePort();
            managementPort = FreePort();
            settings.With("Catalog:Hosting:PublicPort", publicPort.ToString(System.Globalization.CultureInfo.InvariantCulture));
            settings.With("Catalog:Hosting:ManagementPort", managementPort.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment, ContentRootPath = AppContext.BaseDirectory });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(settings.Values);

        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(log);
        builder.Logging.SetMinimumLevel(LogLevel.Trace);

        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddTransient<IStartupFilter, RemoteAddressStartupFilter>();
        services?.Invoke(builder.Services);

        if (!sockets)
        {
            builder.WebHost.UseTestServer();
        }

        builder.AddCatalogApi();

        var app = builder.Build();
        app.UseCatalogApi();
        map?.Invoke(app);

        await app.StartAsync();

        return new ApiTestHost(app, log, clock, publicPort, managementPort);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    // A port that nothing listens on now. Another process could take it before the host binds, which is rare, and the host fails loudly when it does.
    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
