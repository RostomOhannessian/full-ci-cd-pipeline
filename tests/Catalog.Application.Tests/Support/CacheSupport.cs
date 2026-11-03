using System.Collections.Concurrent;
using Catalog.CrossCutting.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using ZiggyCreatures.Caching.Fusion.Backplane;

namespace Catalog.Application.Tests.Support;

/// <summary>A shared cache that fails on every call, as a Valkey that cannot be reached does.</summary>
internal sealed class BrokenDistributedCache : IDistributedCache
{
    public int Calls { get; private set; }

    public byte[]? Get(string key) => Fail();

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Fail<byte[]?>();

    public void Refresh(string key) => Fail();

    public Task RefreshAsync(string key, CancellationToken token = default) => Fail<object?>();

    public void Remove(string key) => Fail();

    public Task RemoveAsync(string key, CancellationToken token = default) => Fail<object?>();

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => Fail();

    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => Fail<object?>();

    private byte[]? Fail()
    {
        Calls++;

        throw new IOException("The shared cache cannot be reached.");
    }

    private Task<T> Fail<T>()
    {
        Calls++;

        throw new IOException("The shared cache cannot be reached.");
    }
}

/// <summary>A memory cache that throws, so a test can prove that a fault of the cache never fails a read.</summary>
internal sealed class BrokenMemoryCache : IMemoryCache
{
    public ICacheEntry CreateEntry(object key) => throw new InvalidOperationException("The memory cache is broken.");

    public void Dispose()
    {
    }

    public void Remove(object key) => throw new InvalidOperationException("The memory cache is broken.");

    public bool TryGetValue(object key, out object? value) => throw new InvalidOperationException("The memory cache is broken.");
}

/// <summary>
/// A memory cache that records the absolute expiration of each entry that FusionCache writes. FusionCache computes the expiration from the wall clock, so a test cannot move it with a fake clock, and it reads
/// the configured time to live from the entry instead of waiting for it.
/// </summary>
/// <param name="inner">The real memory cache.</param>
internal sealed class RecordingMemoryCache(IMemoryCache inner) : IMemoryCache
{
    private readonly ConcurrentQueue<(string Key, DateTimeOffset? Expires)> _entries = new();

    public IReadOnlyList<(string Key, DateTimeOffset? Expires)> Entries => [.. _entries];

    public ICacheEntry CreateEntry(object key) => new RecordingEntry(inner.CreateEntry(key), _entries);

    public void Dispose() => inner.Dispose();

    public void Remove(object key) => inner.Remove(key);

    public bool TryGetValue(object key, out object? value) => inner.TryGetValue(key, out value);

    private sealed class RecordingEntry(ICacheEntry entry, ConcurrentQueue<(string Key, DateTimeOffset? Expires)> entries) : ICacheEntry
    {
        public object Key => entry.Key;

        public object? Value
        {
            get => entry.Value;
            set => entry.Value = value;
        }

        public DateTimeOffset? AbsoluteExpiration
        {
            get => entry.AbsoluteExpiration;
            set => entry.AbsoluteExpiration = value;
        }

        public TimeSpan? AbsoluteExpirationRelativeToNow
        {
            get => entry.AbsoluteExpirationRelativeToNow;
            set => entry.AbsoluteExpirationRelativeToNow = value;
        }

        public TimeSpan? SlidingExpiration
        {
            get => entry.SlidingExpiration;
            set => entry.SlidingExpiration = value;
        }

        public IList<IChangeToken> ExpirationTokens => entry.ExpirationTokens;

        public IList<PostEvictionCallbackRegistration> PostEvictionCallbacks => entry.PostEvictionCallbacks;

        public CacheItemPriority Priority
        {
            get => entry.Priority;
            set => entry.Priority = value;
        }

        public long? Size
        {
            get => entry.Size;
            set => entry.Size = value;
        }

        public void Dispose()
        {
            entries.Enqueue((entry.Key.ToString() ?? string.Empty, entry.AbsoluteExpiration));
            entry.Dispose();
        }
    }
}

/// <summary>A cache of one node for a test: the adapter, its metrics, the log, and a memory cache that records the expiration of its entries.</summary>
internal sealed class CacheRig : IDisposable
{
    public CacheRig(CatalogCacheOptions? options = null, IDistributedCache? shared = null, IFusionCacheBackplane? backplane = null, IMemoryCache? memory = null)
    {
        Options = options ?? new CatalogCacheOptions();
        Memory = new RecordingMemoryCache(memory ?? new MemoryCache(new MemoryCacheOptions { SizeLimit = Options.MemoryMaxEntries }));
        Metrics = new CacheMetrics();
        Log = new CapturingLoggerFactory();
        Cache = CatalogCacheFactory.Create(Options, Metrics, shared, backplane, Log, Memory);
        Measurements = new Measurements(Metrics);
    }

    public CatalogCacheOptions Options { get; }

    public RecordingMemoryCache Memory { get; }

    public CacheMetrics Metrics { get; }

    public CapturingLoggerFactory Log { get; }

    public FusionCatalogCache Cache { get; }

    public Measurements Measurements { get; }

    public void Dispose()
    {
        Cache.Dispose();
        Measurements.Dispose();
        Metrics.Dispose();
        Memory.Dispose();
    }
}
