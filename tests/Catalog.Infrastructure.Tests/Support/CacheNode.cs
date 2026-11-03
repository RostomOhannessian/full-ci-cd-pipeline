using Catalog.Application.Caching;
using Catalog.Application.Products;
using Catalog.Application.Results;
using Catalog.Application.Tests.Support;
using Catalog.CrossCutting.Caching;
using Catalog.Domain.Common;
using Catalog.Domain.Tests.Support;
using Catalog.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Distributed;
using ZiggyCreatures.Caching.Fusion.Backplane;

namespace Catalog.Infrastructure.Tests.Support;

/// <summary>
/// One node of the API for a test: the cache adapter over a connection to Valkey, with its own metrics and its own log, as a replica of the API has them. Two nodes in one test share one server and nothing else, so
/// what one node knows about a key, the other learns only from Valkey (plan section 8.5).
/// </summary>
internal sealed class CacheNode : IDisposable
{
    public CacheNode(ValkeyOptions valkey, CatalogCacheOptions? options = null, IFusionCacheBackplane? backplane = null)
    {
        Options = options ?? new CatalogCacheOptions { Environment = $"t{Counters.Next()}" };
        Shared = ValkeyConnection.CreateSharedCache(valkey);
        Backplane = backplane ?? ValkeyConnection.CreateBackplane(valkey);
        Metrics = new CacheMetrics();
        Log = new CapturingLoggerFactory();
        Cache = CatalogCacheFactory.Create(Options, Metrics, Shared, Backplane, Log);
        Measurements = new Measurements(Metrics);
    }

    public CatalogCacheOptions Options { get; }

    public IDistributedCache Shared { get; }

    public IFusionCacheBackplane Backplane { get; }

    public CacheMetrics Metrics { get; }

    public CapturingLoggerFactory Log { get; }

    public FusionCatalogCache Cache { get; }

    public Measurements Measurements { get; }

    public void Dispose()
    {
        Cache.Dispose();
        (Backplane as IDisposable)?.Dispose();
        (Shared as IDisposable)?.Dispose();
        Measurements.Dispose();
        Metrics.Dispose();
    }
}

/// <summary>The keys, the views, and the loaders that the cache tests share, built from the identifiers that every test project uses.</summary>
internal static class CacheData
{
    public static Guid Product(int number) => TestIds.Product(number).Value;

    public static CacheKey ProductKey(Guid id) => new($"product:{id:D}", [$"product:{id:D}"]);

    public static CacheKey ListKey(string hash, params string[] extra) => new($"products:list:{hash}", ["products:list", .. extra]);

    public static ProductView View(Guid id, string name = "Mechanical keyboard", string eTag = "AAAAAAAAB9E") =>
        new(id, "KB-100", name, null, 89.90m, "USD", TestIds.Category(1).Value, LifecycleStatus.Active, new DateTimeOffset(2026, 1, 15, 9, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 1, 15, 9, 30, 0, TimeSpan.Zero), eTag);

    /// <summary>Makes a loader that returns a view and counts its runs.</summary>
    public static Func<CancellationToken, Task<Result<ProductView>>> Loads(ProductView view, Action? onCall = null) =>
        _ =>
        {
            onCall?.Invoke();

            return Task.FromResult(Result.Success(view));
        };
}
