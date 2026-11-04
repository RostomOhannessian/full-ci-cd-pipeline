using System.Collections.Concurrent;
using Catalog.Application.Caching;
using Catalog.Application.Events;
using Catalog.Application.Paging;
using Catalog.Application.Products;
using Catalog.Application.Results;
using Catalog.CrossCutting.Caching;
using Catalog.Domain.Products;
using Catalog.Domain.Tests.Support;
using Catalog.Infrastructure.Caching;
using Catalog.Infrastructure.Outbox;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using ZiggyCreatures.Caching.Fusion.Backplane;

namespace Catalog.Infrastructure.Tests.Support;

/// <summary>A cache that records the tags that it was asked to remove, whether the removal worked or not, and passes every call to the cache that it wraps.</summary>
/// <param name="inner">The cache that does the work.</param>
internal sealed class CountingCache(ICatalogCache inner) : ICatalogCache
{
    private readonly ConcurrentQueue<string> _removed = new();

    /// <summary>Gets the tags in the order in which the removals were asked for.</summary>
    public IReadOnlyList<string> Removed => [.. _removed];

    public Task<Result<T>> GetOrLoadAsync<T>(CacheKey key, Func<CancellationToken, Task<Result<T>>> load, CancellationToken cancellationToken) => inner.GetOrLoadAsync(key, load, cancellationToken);

    public async Task RemoveByTagAsync(string tag, CancellationToken cancellationToken)
    {
        _removed.Enqueue(tag);
        await inner.RemoveByTagAsync(tag, cancellationToken);
    }
}

/// <summary>
/// One replica of the API for the tests of the whole invalidation path: its own cache node over a shared Valkey, its own outbox processor over a shared SQL Server database, the invalidation subscriber that both paths
/// call, and the dispatcher that a scope uses after a commit. It is the reference of how the Api composes the pieces in WP1.10 (<see cref="TestComposition"/>), written without a container because the tests need
/// the pieces and not the container. Two hosts in one test share the database and the Valkey server and nothing else, so what one host knows, the other learns only from them (plan sections 8.4 and 8.5, ADR-0039).
/// </summary>
internal sealed class CacheHost : IDisposable
{
    private readonly CatalogDatabaseFixture _database;
    private readonly List<IOutboxMessageHandler> _handlers = [];

    public CacheHost(CatalogDatabaseFixture database, ValkeyOptions valkey, CatalogCacheOptions options, FakeTimeProvider clock, IFusionCacheBackplane? backplane = null, OutboxOptions? outbox = null, params IOutboxMessageHandler[] more)
    {
        _database = database;
        Clock = clock;
        Node = new CacheNode(valkey, options, backplane);
        Cache = new CountingCache(Node.Cache);
        Subscriber = new CacheInvalidationSubscriber(Cache);
        _handlers.Add(Subscriber);
        _handlers.AddRange(more);
        Signal = new OutboxSignal();
        Metrics = new OutboxMetrics();
        DispatchLog = new CapturingLogger<OutboxDispatcher>();
        Processor = new OutboxProcessor(
            database.NewContextFactory(),
            _handlers,
            Options.Create(outbox ?? new OutboxOptions { JitterRatio = 0 }),
            clock,
            Metrics,
            new CapturingLogger<OutboxProcessor>(),
            new Random(7),
            () => TestIds.V7(Counters.Next()));
    }

    public FakeTimeProvider Clock { get; }

    public CacheNode Node { get; }

    public CountingCache Cache { get; }

    public CacheInvalidationSubscriber Subscriber { get; }

    public OutboxSignal Signal { get; }

    public OutboxMetrics Metrics { get; }

    public CapturingLogger<OutboxDispatcher> DispatchLog { get; }

    public OutboxProcessor Processor { get; }

    /// <summary>Counts the times that a loader ran on this host, which is the number of reads that the cache could not answer.</summary>
    public int Loads => Volatile.Read(ref _loads);

    private int _loads;

    /// <summary>Makes the scope of one request: a write context with the outbox interceptor, and the repositories and read stores over it.</summary>
    public CatalogScope NewScope() => new(_database, Clock);

    /// <summary>Makes the dispatcher that the request of a scope calls after its commit.</summary>
    /// <param name="scope">The scope.</param>
    public OutboxDispatcher DispatcherFor(CatalogScope scope) => new(scope.Context, _handlers, Signal, Metrics, DispatchLog);

    /// <summary>Reads a product the way the query decorator does: through the cache, and from the database when the cache has no answer.</summary>
    /// <param name="id">The identifier of the product.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    public async Task<ProductView> ReadAsync(ProductId id, CancellationToken cancellationToken)
    {
        await using var scope = NewScope();
        var result = await Cache.GetOrLoadAsync(
            new GetProduct(id.Value).CacheKey,
            async token =>
            {
                Interlocked.Increment(ref _loads);
                var view = await scope.ProductReads.GetAsync(id, token);

                return view is null ? Result.Failure<ProductView>(new NotFound("product")) : Result.Success(view);
            },
            cancellationToken);

        return result.Value!;
    }

    /// <summary>Reads a list of products through the cache with the key of the unfiltered list. The loader returns a page of the one product, so a test sees its price change.</summary>
    /// <param name="id">The identifier of the product that the page holds.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    public async Task<Page<ProductView>> ReadListAsync(ProductId id, CancellationToken cancellationToken)
    {
        await using var scope = NewScope();
        var result = await Cache.GetOrLoadAsync(
            new ListProducts().CacheKey,
            async token =>
            {
                Interlocked.Increment(ref _loads);
                var view = await scope.ProductReads.GetAsync(id, token) ?? throw new InvalidOperationException("The product does not exist.");

                return Result.Success(new Page<ProductView>([view], null));
            },
            cancellationToken);

        return result.Value!;
    }

    /// <summary>
    /// Changes the price of a product in a request of this host and commits it. When <paramref name="dispatch"/> is true the request then dispatches in process, which is the best-effort path of a write. When it is
    /// false the request ends right after the commit, which is a host that crashed or a dispatch that was lost, and only the outbox can finish the invalidation.
    /// </summary>
    /// <param name="id">The identifier of the product.</param>
    /// <param name="price">The new price in dollars.</param>
    /// <param name="dispatch">Whether the request dispatches after the commit.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    public async Task ChangePriceAsync(ProductId id, decimal price, bool dispatch, CancellationToken cancellationToken)
    {
        await using var scope = NewScope();
        var result = await scope.UnitOfWork.ExecuteAsync(
            async token =>
            {
                var found = await scope.Products.FindAsync(id, token) ?? throw new InvalidOperationException("The product does not exist.");
                var category = await scope.Categories.FindAsync(found.Entity.CategoryId, token) ?? throw new InvalidOperationException("The category does not exist.");
                var update = found.Entity.Update(found.Entity.Name, found.Entity.Description, Make.Usd(price), category.Entity, Clock);

                return update.IsSuccess ? Result.Success(1) : throw new InvalidOperationException("The domain refused the price.");
            },
            cancellationToken);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"The price was not saved: {result.Error}.");
        }

        if (dispatch)
        {
            await DispatcherFor(scope).DispatchCommittedAsync(cancellationToken);
        }
    }

    /// <summary>Writes one category and one product to the database and clears the outbox, so a test starts from stored data with no message waiting. Returns the identifier of the product.</summary>
    /// <param name="price">The price of the product in dollars.</param>
    public async Task<ProductId> SeedProductAsync(decimal price)
    {
        await using var scope = NewScope();
        var category = Make.Category(Counters.Next(), "Keyboards");
        var product = Product.Create(TestIds.Product(Counters.Next()), Make.Sku(Counters.Sku()), Make.ProductName(), null, Make.Usd(price), category, Clock).Value;
        await scope.SaveAsync(category);
        await scope.SaveAsync(product);
        await _database.ResetOutboxAsync();

        return product.Id;
    }

    public void Dispose()
    {
        Signal.Dispose();
        Metrics.Dispose();
        Node.Dispose();
    }
}
