using System.Globalization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using ZiggyCreatures.Caching.Fusion.Backplane;
using ZiggyCreatures.Caching.Fusion.Backplane.StackExchangeRedis;

namespace Catalog.Infrastructure.Caching;

/// <summary>
/// Builds the two things that the cache needs from Valkey: the shared cache (an <see cref="IDistributedCache"/>) and the backplane (plan sections 8.1 and 8.5, ADR-0038). Each opens its own connection through
/// StackExchange.Redis, and both read the same settings. The connection is lazy and never aborts when the server is down (<c>AbortOnConnectFail</c> is off), so the host starts without Valkey and the circuit
/// breakers of the cache take over, which plan section 8.9 requires because readiness excludes Valkey. The composition root owns what these methods return and disposes it.
/// </summary>
public static class ValkeyConnection
{
    /// <summary>Builds the connection settings of a set of Valkey settings. A test of the access control list reads them too.</summary>
    /// <param name="options">The settings. They must be valid.</param>
    public static ConfigurationOptions Configure(ValkeyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var problems = options.Validate();
        if (problems.Count > 0)
        {
            throw new ArgumentException($"The Valkey settings are not valid: {string.Join(' ', problems)}", nameof(options));
        }

        var separator = options.Endpoint.LastIndexOf(':');
        var configuration = new ConfigurationOptions
        {
            User = options.UserName,
            Password = options.Password,
            Ssl = options.UseTls,
            ClientName = options.ClientName,
            ConnectTimeout = Milliseconds(options.ConnectTimeout),
            SyncTimeout = Milliseconds(options.CommandTimeout),
            AsyncTimeout = Milliseconds(options.CommandTimeout),

            // The host starts when Valkey is down, and the circuit breakers of the cache degrade to memory and the database (plan sections 8.5 and 8.9).
            AbortOnConnectFail = false,

            // The connection handshake of StackExchange.Redis reads the topology of a cluster and the settings of the server, reads a key to choose between two masters, and listens on a channel for a change of
            // master. The cache needs none of it, so each is switched off, and the access control list of the application user can refuse the commands and name no key and no channel for them (spike 1.b,
            // finding 4, ADR-0038).
            CommandMap = CommandMap.Create(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CLUSTER", "CONFIG" }, available: false),
            TieBreaker = string.Empty,
            ConfigurationChannel = string.Empty,
        };

        configuration.EndPoints.Add(options.Endpoint[..separator], int.Parse(options.Endpoint[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture));

        return configuration;
    }

    /// <summary>Builds the shared cache (L2) over Valkey.</summary>
    /// <param name="options">The settings. They must be valid.</param>
    /// <param name="observer">Told about the connection when it opens, so the tracing can instrument it, or null to leave the connection to the cache.</param>
    public static IDistributedCache CreateSharedCache(ValkeyOptions options, ValkeyConnectionObserver? observer = null)
    {
        var configuration = Configure(options);
        var cache = new RedisCacheOptions { ConfigurationOptions = configuration };

        if (observer is not null)
        {
            cache.ConnectionMultiplexerFactory = () => ConnectAsync(configuration, observer);
        }

        return new RedisCache(Options.Create(cache));
    }

    /// <summary>Builds the backplane over Valkey. The channel is named from the cache, so every environment has its own.</summary>
    /// <param name="options">The settings. They must be valid.</param>
    /// <param name="observer">Told about the connection when it opens, so the tracing can instrument it, or null to leave the connection to the backplane.</param>
    public static IFusionCacheBackplane CreateBackplane(ValkeyOptions options, ValkeyConnectionObserver? observer = null)
    {
        var configuration = Configure(options);
        var backplane = new RedisBackplaneOptions { ConfigurationOptions = configuration };

        if (observer is not null)
        {
            backplane.ConnectionMultiplexerFactory = () => ConnectAsync(configuration, observer);
        }

        return new RedisBackplane(Options.Create(backplane));
    }

    private static async Task<IConnectionMultiplexer> ConnectAsync(ConfigurationOptions configuration, ValkeyConnectionObserver observer)
    {
        var connection = await ConnectionMultiplexer.ConnectAsync(configuration);
        observer.Connected(connection);

        return connection;
    }

    private static int Milliseconds(TimeSpan value) => (int)Math.Min(value.TotalMilliseconds, int.MaxValue);
}

/// <summary>
/// Passes each connection to Valkey that the cache or the backplane opens to whoever instruments connections (plan section 8.11, ADR-0043). The composition root registers one instance and hands it to both builders, and
/// the tracing registers a sink once its provider exists. A connection that opened before the sink arrived is passed to it then, and a connection that opens later is passed at once, so no connection is missed whichever
/// comes first, and none is passed twice.
/// </summary>
public sealed class ValkeyConnectionObserver
{
    private readonly object _gate = new();
    private readonly List<IConnectionMultiplexer> _connections = [];
    private Action<IConnectionMultiplexer>? _sink;

    /// <summary>Reports a connection that has just opened.</summary>
    /// <param name="connection">The connection.</param>
    public void Connected(IConnectionMultiplexer connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        Action<IConnectionMultiplexer>? sink;

        lock (_gate)
        {
            _connections.Add(connection);
            sink = _sink;
        }

        sink?.Invoke(connection);
    }

    /// <summary>Sets the sink that receives every connection, the ones that opened already and the ones that open later. Call it once.</summary>
    /// <param name="sink">The sink.</param>
    public void Attach(Action<IConnectionMultiplexer> sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        IConnectionMultiplexer[] opened;

        lock (_gate)
        {
            _sink = sink;
            opened = [.. _connections];
        }

        foreach (var connection in opened)
        {
            sink(connection);
        }
    }
}
