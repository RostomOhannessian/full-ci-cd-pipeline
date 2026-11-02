using Catalog.Application.Persistence;
using Catalog.Application.Results;
using Catalog.Domain.Categories;
using Catalog.Domain.Products;
using Catalog.Domain.Tests.Support;
using Catalog.Infrastructure.Outbox;
using Catalog.Infrastructure.Persistence;
using Catalog.Infrastructure.Persistence.Reading;
using Microsoft.Extensions.Time.Testing;

namespace Catalog.Infrastructure.Tests.Support;

/// <summary>
/// What one request sees: a write context and the repositories and the unit of work that share it, and a read context with the read stores. A test makes one scope for each request that it simulates, so two scopes
/// are two requests that load the same row and race to change it (plan section 8.4).
/// </summary>
public sealed class CatalogScope : IAsyncDisposable
{
    private readonly CatalogReadContext _readContext;

    /// <summary>Initializes a new instance of the <see cref="CatalogScope"/> class.</summary>
    /// <param name="database">The database of the test class.</param>
    /// <param name="outboxClock">The clock of the outbox interceptor, which a scope that writes outbox rows needs. A scope without it writes none.</param>
    public CatalogScope(CatalogDatabaseFixture database, TimeProvider? outboxClock = null)
    {
        ArgumentNullException.ThrowIfNull(database);

        Context = outboxClock is null ? database.NewContext() : database.NewContext(new OutboxInterceptor(outboxClock, () => TestIds.V7(Counters.Next())));
        _readContext = database.NewReadContext();
        Products = new ProductRepository(Context);
        Categories = new CategoryRepository(Context);
        UnitOfWork = new EfUnitOfWork(Context);
        ProductReads = new ProductReadStore(_readContext);
        CategoryReads = new CategoryReadStore(_readContext);
    }

    /// <summary>Gets the write context.</summary>
    public CatalogDbContext Context { get; }

    /// <summary>Gets the repository of products.</summary>
    public ProductRepository Products { get; }

    /// <summary>Gets the repository of categories.</summary>
    public CategoryRepository Categories { get; }

    /// <summary>Gets the unit of work.</summary>
    public EfUnitOfWork UnitOfWork { get; }

    /// <summary>Gets the read store of products.</summary>
    public ProductReadStore ProductReads { get; }

    /// <summary>Gets the read store of categories.</summary>
    public CategoryReadStore CategoryReads { get; }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        await _readContext.DisposeAsync();
    }

    /// <summary>Saves a category in its own transaction and returns the tag of the stored row.</summary>
    /// <param name="category">The category.</param>
    public async Task<EntityTag> SaveAsync(Category category)
    {
        ArgumentNullException.ThrowIfNull(category);

        var result = await UnitOfWork.ExecuteAsync(
            _ =>
            {
                Categories.Add(category);

                return Task.FromResult(Result.Success(category.Id.Value));
            },
            CancellationToken.None);

        return result.IsSuccess ? Categories.TagOf(category) : throw new InvalidOperationException($"The category was not saved: {result.Error}.");
    }

    /// <summary>Saves a product in its own transaction and returns the tag of the stored row.</summary>
    /// <param name="product">The product.</param>
    public async Task<EntityTag> SaveAsync(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);

        var result = await UnitOfWork.ExecuteAsync(
            _ =>
            {
                Products.Add(product);

                return Task.FromResult(Result.Success(product.Id.Value));
            },
            CancellationToken.None);

        return result.IsSuccess ? Products.TagOf(product) : throw new InvalidOperationException($"The product was not saved: {result.Error}.");
    }

    /// <summary>Makes an active product in an active category, with the events of its creation cleared, using the builders that every test project shares.</summary>
    /// <param name="category">The category.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="counter">The counter that the identifier is built from.</param>
    /// <param name="sku">The SKU.</param>
    /// <param name="price">The amount in dollars.</param>
    public static Product NewProduct(Category category, FakeTimeProvider clock, long counter, string sku, decimal price = 89.90m) =>
        Make.Product(category, clock, counter, sku, price);
}
