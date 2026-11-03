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
    public static IDistributedCache CreateSharedCache(ValkeyOptions options) =>
        new RedisCache(Options.Create(new RedisCacheOptions { ConfigurationOptions = Configure(options) }));

    /// <summary>Builds the backplane over Valkey. The channel is named from the cache, so every environment has its own.</summary>
    /// <param name="options">The settings. They must be valid.</param>
    public static IFusionCacheBackplane CreateBackplane(ValkeyOptions options) =>
        new RedisBackplane(Options.Create(new RedisBackplaneOptions { ConfigurationOptions = Configure(options) }));

    private static int Milliseconds(TimeSpan value) => (int)Math.Min(value.TotalMilliseconds, int.MaxValue);
}
