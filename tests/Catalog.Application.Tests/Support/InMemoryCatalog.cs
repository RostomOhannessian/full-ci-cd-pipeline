using Catalog.Application.Categories;
using Catalog.Application.Paging;
using Catalog.Application.Persistence;
using Catalog.Application.Products;
using Catalog.Domain.Categories;
using Catalog.Domain.Common;
using Catalog.Domain.Products;

namespace Catalog.Application.Tests.Support;

/// <summary>
/// An in-memory catalog that behaves like the persistence layer of WP1.8 as far as the use cases can see: a row has a version that changes when a save writes it, a save turns the events of an aggregate
/// into published events and clears them, and a read store returns the rows in the order of the contract. The tests of the real implementation use SQL Server (WP1.8).
/// </summary>
internal sealed class InMemoryCatalog
{
    private readonly Dictionary<ProductId, Entry<Product>> _products = [];
    private readonly Dictionary<CategoryId, Entry<Category>> _categories = [];

    public InMemoryCatalog()
    {
        Products = new FakeProductRepository(this);
        Categories = new FakeCategoryRepository(this);
        ProductReads = new FakeProductReadStore(this);
        CategoryReads = new FakeCategoryReadStore(this);
    }

    public FakeProductRepository Products { get; }

    public FakeCategoryRepository Categories { get; }

    public FakeProductReadStore ProductReads { get; }

    public FakeCategoryReadStore CategoryReads { get; }

    /// <summary>The events that saves wrote, in order, as the outbox would hold them.</summary>
    public List<IDomainEvent> PublishedEvents { get; } = [];

    public IEnumerable<Product> AllProducts => _products.Values.Select(entry => entry.Value);

    public IEnumerable<Category> AllCategories => _categories.Values.Select(entry => entry.Value);

    public void Seed(Product product)
    {
        _products[product.Id] = new Entry<Product>(product, 1);
        product.ClearDomainEvents();
    }

    public void Seed(Category category)
    {
        _categories[category.Id] = new Entry<Category>(category, 1);
        category.ClearDomainEvents();
    }

    public long VersionOf(Product product) => _products[product.Id].Version;

    public long VersionOf(Category category) => _categories[category.Id].Version;

    public void Save()
    {
        foreach (var entry in _products.Values.Where(entry => entry.Value.DomainEvents.Count > 0 || entry.Version == 0))
        {
            Write(entry, entry.Value);
        }

        foreach (var entry in _categories.Values.Where(entry => entry.Value.DomainEvents.Count > 0 || entry.Version == 0))
        {
            Write(entry, entry.Value);
        }
    }

    private void Write(Entry<Category> entry, Category aggregate)
    {
        PublishedEvents.AddRange(aggregate.DomainEvents);
        aggregate.ClearDomainEvents();
        entry.Version++;
    }

    private void Write(Entry<Product> entry, Product aggregate)
    {
        PublishedEvents.AddRange(aggregate.DomainEvents);
        aggregate.ClearDomainEvents();
        entry.Version++;
    }

    internal static EntityTag TagFor(long version) => EntityTag.FromBytes(BitConverter.GetBytes(version));

    /// <summary>The tag that the fake gives to a row at a version, so a test can name a current or a stale one.</summary>
    public static EntityTag TagForTests(long version) => TagFor(version);

    internal sealed class Entry<T>(T value, long version)
    {
        public T Value { get; } = value;

        public long Version { get; set; } = version;
    }

    internal Dictionary<ProductId, Entry<Product>> ProductEntries => _products;

    internal Dictionary<CategoryId, Entry<Category>> CategoryEntries => _categories;
}

internal sealed class FakeProductRepository(InMemoryCatalog catalog) : IProductRepository
{
    public int Finds { get; private set; }

    public void Add(Product product) => catalog.ProductEntries[product.Id] = new InMemoryCatalog.Entry<Product>(product, 0);

    public Task<Versioned<Product>?> FindAsync(ProductId id, CancellationToken cancellationToken)
    {
        Finds++;

        return Task.FromResult(catalog.ProductEntries.TryGetValue(id, out var entry) && entry.Version > 0
            ? new Versioned<Product>(entry.Value, InMemoryCatalog.TagFor(entry.Version))
            : null);
    }

    public Task<bool> SkuExistsAsync(Sku sku, CancellationToken cancellationToken) => Task.FromResult(catalog.AllProducts.Any(product => product.Sku == sku));

    public EntityTag TagOf(Product product) =>
        catalog.ProductEntries.TryGetValue(product.Id, out var entry) && entry.Version > 0
            ? InMemoryCatalog.TagFor(entry.Version)
            : throw new InvalidOperationException("The product has no version before the first save.");
}

internal sealed class FakeCategoryRepository(InMemoryCatalog catalog) : ICategoryRepository
{
    public void Add(Category category) => catalog.CategoryEntries[category.Id] = new InMemoryCatalog.Entry<Category>(category, 0);

    public Task<Versioned<Category>?> FindAsync(CategoryId id, CancellationToken cancellationToken) =>
        Task.FromResult(catalog.CategoryEntries.TryGetValue(id, out var entry) && entry.Version > 0
            ? new Versioned<Category>(entry.Value, InMemoryCatalog.TagFor(entry.Version))
            : null);

    public EntityTag TagOf(Category category) =>
        catalog.CategoryEntries.TryGetValue(category.Id, out var entry) && entry.Version > 0
            ? InMemoryCatalog.TagFor(entry.Version)
            : throw new InvalidOperationException("The category has no version before the first save.");
}

internal sealed class FakeCategoryReadStore(InMemoryCatalog catalog) : ICategoryReadStore
{
    public int Limit { get; private set; }

    public LifecycleStatus? LastStatus { get; private set; }

    public Task<CategoryView?> GetAsync(CategoryId id, CancellationToken cancellationToken) =>
        Task.FromResult(catalog.CategoryEntries.TryGetValue(id, out var entry) ? CategoryView.From(entry.Value, InMemoryCatalog.TagFor(entry.Version)) : null);

    public Task<IReadOnlyList<CategoryView>> ListAsync(LifecycleStatus? status, KeysetPosition? after, int limit, CancellationToken cancellationToken)
    {
        Limit = limit;
        LastStatus = status;
        var views = catalog.CategoryEntries.Values
            .Select(entry => CategoryView.From(entry.Value, InMemoryCatalog.TagFor(entry.Version)))
            .Where(view => status is null || view.Status == status)
            .OrderBy(view => view.Name, StringComparer.Ordinal)
            .ThenBy(view => view.Id)
            .Where(view => after is null || string.CompareOrdinal(view.Name, after.SortKey) > 0 || (view.Name == after.SortKey && view.Id.CompareTo(after.Id) > 0))
            .Take(limit)
            .ToList();

        return Task.FromResult<IReadOnlyList<CategoryView>>(views);
    }
}

internal sealed class FakeProductReadStore(InMemoryCatalog catalog) : IProductReadStore
{
    public ProductFilter? LastFilter { get; private set; }

    public int Limit { get; private set; }

    public Task<ProductView?> GetAsync(ProductId id, CancellationToken cancellationToken) =>
        Task.FromResult(catalog.ProductEntries.TryGetValue(id, out var entry) ? ProductView.From(entry.Value, InMemoryCatalog.TagFor(entry.Version)) : null);

    public Task<IReadOnlyList<ProductView>> ListAsync(ProductFilter filter, KeysetPosition? after, int limit, CancellationToken cancellationToken)
    {
        LastFilter = filter;
        Limit = limit;
        var rows = catalog.ProductEntries.Values
            .Select(entry => ProductView.From(entry.Value, InMemoryCatalog.TagFor(entry.Version)))
            .Where(view => filter.CategoryId is null || view.CategoryId == filter.CategoryId.Value)
            .Where(view => filter.Status is null || view.Status == filter.Status)
            .Where(view => filter.Search is null || view.Name.Contains(filter.Search, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var ordered = filter.Sort == ProductSort.Name
            ? rows.OrderBy(view => view.Name, StringComparer.Ordinal).ThenBy(view => view.Id)
            : rows.OrderByDescending(view => view.CreatedAt).ThenBy(view => view.Id);

        var page = ordered.Where(view => after is null || IsAfter(view, filter.Sort, after)).Take(limit).ToList();

        return Task.FromResult<IReadOnlyList<ProductView>>(page);
    }

    private static bool IsAfter(ProductView view, ProductSort sort, KeysetPosition after)
    {
        if (sort == ProductSort.Name)
        {
            var byName = string.CompareOrdinal(view.Name, after.SortKey);
            return byName > 0 || (byName == 0 && view.Id.CompareTo(after.Id) > 0);
        }

        var createdAt = DateTimeOffset.Parse(after.SortKey, System.Globalization.CultureInfo.InvariantCulture);

        return view.CreatedAt < createdAt || (view.CreatedAt == createdAt && view.Id.CompareTo(after.Id) > 0);
    }
}
