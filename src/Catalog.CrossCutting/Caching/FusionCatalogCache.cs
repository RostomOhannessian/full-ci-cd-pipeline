using System.Runtime.ExceptionServices;
using Catalog.Application.Caching;
using Catalog.Application.Results;
using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion;

namespace Catalog.CrossCutting.Caching;

/// <summary>
/// Implements <see cref="ICatalogCache"/> over FusionCache: the memory of the node (L1), the shared cache (L2), and the backplane that tells the other nodes to drop an entry (plan section 8.5, ADR-0016,
/// ADR-0038). A read is served from memory, then from the shared cache, then from the loader, and calls for one key on one node share one load. Only a success is kept. When the shared cache or the backplane is
/// down, a read still works: the circuit breakers open after the first failure, the node serves its memory and runs the loader, and the metrics report the state. A removal is different. It removes the entries of
/// this node, awaits the shared cache and the backplane, and throws <see cref="CacheInvalidationIncompleteException"/> when either level was not reached, so the outbox can try again and the other nodes converge.
/// Build one with <see cref="CatalogCacheFactory"/>.
/// </summary>
public sealed partial class FusionCatalogCache : ICatalogCache, IDisposable
{
    private readonly IFusionCache _cache;
    private readonly CacheMetrics _metrics;
    private readonly ILogger<FusionCatalogCache> _logger;
    private readonly FusionCacheEntryOptions _entryOptions;
    private readonly FusionCacheEntryOptions _listOptions;
    private readonly IDisposable[] _owned;

    internal FusionCatalogCache(IFusionCache cache, CatalogCacheOptions options, CacheMetrics metrics, ILogger<FusionCatalogCache> logger, params IDisposable[] owned)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _owned = owned ?? [];

        ArgumentNullException.ThrowIfNull(options);
        _entryOptions = CatalogCacheFactory.EntryOptions(options, options.MemoryDuration, options.SharedDuration);
        _listOptions = CatalogCacheFactory.EntryOptions(options, options.ListDuration, options.ListDuration);

        _cache.Events.Distributed.CircuitBreakerChange += (_, args) => _metrics.SharedCacheChanged(args.IsClosed);
        _cache.Events.Backplane.CircuitBreakerChange += (_, args) => _metrics.BackplaneChanged(args.IsClosed);
        _cache.Events.Distributed.SerializationError += (_, _) => _metrics.SerializationError();
        _cache.Events.Distributed.DeserializationError += (_, _) => _metrics.SerializationError();
    }

    /// <inheritdoc />
    public async Task<Result<T>> GetOrLoadAsync<T>(CacheKey key, Func<CancellationToken, Task<Result<T>>> load, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(load);

        Result<T>? loaded = null;
        ExceptionDispatchInfo? loaderFault = null;

        try
        {
            var value = await _cache.GetOrSetAsync<T>(
                CacheContract.KeyOf(key.Name),
                async (context, token) =>
                {
                    try
                    {
                        loaded = await load(token).ConfigureAwait(false);
                    }
#pragma warning disable CA1031 // The fault belongs to the use case and not to the cache. It is captured so that it reaches the caller unchanged, whatever the cache does with an exception of a factory.
                    catch (Exception exception)
#pragma warning restore CA1031
                    {
                        loaderFault = ExceptionDispatchInfo.Capture(exception);
                        throw;
                    }

                    if (!loaded.IsSuccess)
                    {
                        // A refusal such as not-found must not outlive the change that fixes it, so nothing of it reaches either level.
                        context.Options.Duration = TimeSpan.Zero;
                        context.Options.DistributedCacheDuration = TimeSpan.Zero;
                        context.Options.SkipDistributedCacheWrite = true;
                        context.Options.SkipMemoryCacheWrite = true;

                        return default!;
                    }

                    return loaded.Value!;
                },
                options: key.Name.StartsWith(CacheContract.ListNamePrefix, StringComparison.Ordinal) ? _listOptions : _entryOptions,
                tags: key.Tags,
                token: cancellationToken).ConfigureAwait(false);

            _metrics.Request(hit: loaded is null);

            return loaded ?? Result.Success(value!);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // A fault of the cache must never fail a use case. A fault of the loader is rethrown below, and every other fault is absorbed by running the loader without the cache.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            if (loaderFault is not null)
            {
                loaderFault.Throw();
            }

            if (loaded is not null)
            {
                return loaded;
            }

            _metrics.Fault();
            LogReadFault(exception);

            return await load(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task RemoveByTagAsync(string tag, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        Exception? fault = null;

        try
        {
            await _cache.RemoveByTagAsync(tag, token: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Any fault of the cache means that the removal is not confirmed, which the exception below reports.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            fault = exception;
        }

        var degradation = _metrics.Degradation;

        if (fault is null && !degradation.Degraded)
        {
            _metrics.Invalidation(complete: true);

            return;
        }

        _metrics.Invalidation(complete: false);
        LogIncompleteRemoval(fault, degradation.SharedCacheDown, degradation.BackplaneDown);

        throw new CacheInvalidationIncompleteException(
            $"The removal of the tag {tag} is not confirmed: the shared cache is {(degradation.SharedCacheDown ? "down" : "up")} and the backplane is {(degradation.BackplaneDown ? "down" : "up")}.",
            fault ?? new InvalidOperationException("The circuit breaker of a cache level is open."));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _cache.Dispose();

        foreach (var owned in _owned)
        {
            owned.Dispose();
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "The cache failed on a read, so the loader ran without it.")]
    private partial void LogReadFault(Exception exception);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "A removal by tag is not confirmed on every level. Shared cache down: {SharedCacheDown}. Backplane down: {BackplaneDown}.")]
    private partial void LogIncompleteRemoval(Exception? exception, bool sharedCacheDown, bool backplaneDown);
}
