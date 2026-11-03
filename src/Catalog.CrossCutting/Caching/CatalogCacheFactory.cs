using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;

namespace Catalog.CrossCutting.Caching;

/// <summary>
/// Builds the FusionCache adapter of the catalog from its settings, the shared cache, and the backplane that Infrastructure builds (plan sections 8.1 and 8.5, ADR-0038). The composition root calls it once, so the
/// settings that the plan names are written in one place: the key prefix and the channel of the environment, the times to live, the size of the memory, the timeout of the shared cache, and the circuit breakers, which
/// FusionCache leaves off unless a duration is set (spike 1.b). The project that calls this method owns the shared cache and the backplane, and the adapter owns what it builds itself.
/// </summary>
public static class CatalogCacheFactory
{
    // A tag removal is stored as a marker that must outlive every entry that it can remove, so the marker lives for twice the longest time to live of an entry.
    private const int MarkerLifetimeFactor = 2;

    /// <summary>Builds the adapter.</summary>
    /// <param name="options">The settings. They must be valid.</param>
    /// <param name="metrics">The metrics, which the adapter reports to and which hold the state of the circuit breakers.</param>
    /// <param name="sharedCache">The shared cache (L2), or null to run with the memory of the node only.</param>
    /// <param name="backplane">The backplane, or null to run without cross-node invalidation.</param>
    /// <param name="loggers">The factory of the loggers, or null for no logging.</param>
    /// <param name="memory">The memory cache (L1) of the node, which the caller owns, or null to let the adapter make one that holds at most <see cref="CatalogCacheOptions.MemoryMaxEntries"/> entries.</param>
    public static FusionCatalogCache Create(
        CatalogCacheOptions options,
        CacheMetrics metrics,
        IDistributedCache? sharedCache = null,
        IFusionCacheBackplane? backplane = null,
        ILoggerFactory? loggers = null,
        IMemoryCache? memory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(metrics);

        var problems = options.Validate();
        if (problems.Count > 0)
        {
            throw new ArgumentException($"The cache settings are not valid: {string.Join(' ', problems)}", nameof(options));
        }

        loggers ??= NullLoggerFactory.Instance;

        var longest = options.SharedDuration > options.ListDuration ? options.SharedDuration : options.ListDuration;
        var fusion = new FusionCacheOptions
        {
            CacheName = "catalog",
            CacheKeyPrefix = options.KeyPrefix,
            BackplaneChannelPrefix = options.ChannelPrefix,
            DistributedCacheCircuitBreakerDuration = options.SharedCircuitBreakerDuration,
            BackplaneCircuitBreakerDuration = options.BackplaneCircuitBreakerDuration,

            // The adapter reads the state of the circuit breakers right after a call, so the handlers of those events must have run by then. They only flip a flag and count.
            EnableSyncEventHandlersExecution = true,

            // A node that serves before it subscribed could miss an invalidation, so the first call waits for the subscription, or for its failure.
            WaitForInitialBackplaneSubscribe = true,
            DefaultEntryOptions = EntryOptions(options, options.MemoryDuration, options.SharedDuration),
            TagsDefaultEntryOptions = TagOptions(options, longest * MarkerLifetimeFactor),
        };

        var owned = new List<IDisposable>();
        var memoryCache = memory;

        if (memoryCache is null)
        {
            var created = new MemoryCache(new MemoryCacheOptions { SizeLimit = options.MemoryMaxEntries });
            owned.Add(created);
            memoryCache = created;
        }

        var cache = new FusionCache(Options.Create(fusion), memoryCache, loggers.CreateLogger<FusionCache>());

        if (sharedCache is not null)
        {
            cache.SetupDistributedCache(sharedCache, new FusionCacheSystemTextJsonSerializer(CacheJsonContext.Default.Options));
        }

        if (backplane is not null)
        {
            cache.SetupBackplane(backplane);
        }

        // The adapter disposes the cache first, and then the memory that it made.
        return new FusionCatalogCache(cache, options, metrics, loggers.CreateLogger<FusionCatalogCache>(), [.. owned]);
    }

    internal static FusionCacheEntryOptions EntryOptions(CatalogCacheOptions options, TimeSpan memoryDuration, TimeSpan sharedDuration) =>
        new()
        {
            Duration = memoryDuration,
            DistributedCacheDuration = sharedDuration,
            Size = 1,
            DistributedCacheHardTimeout = options.SharedTimeout,
        };

    // The marker of a removal is written to the shared cache and published on the backplane before the call returns, because the write path of plan section 8.5 awaits both.
    private static FusionCacheEntryOptions TagOptions(CatalogCacheOptions options, TimeSpan lifetime) =>
        new()
        {
            Duration = lifetime,
            Size = 1,
            DistributedCacheHardTimeout = options.SharedTimeout,
            AllowBackgroundDistributedCacheOperations = false,
            AllowBackgroundBackplaneOperations = false,
        };
}
