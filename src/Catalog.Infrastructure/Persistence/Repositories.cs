using Catalog.Application.Persistence;
using Catalog.Domain.Categories;
using Catalog.Domain.Products;
using Catalog.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Infrastructure.Persistence;

/// <summary>
/// Implements <see cref="IProductRepository"/> over the write context. It loads products with tracking, so the changes that a handler makes are written when the unit of work saves, and it reads the version of a row
/// from the row version that SQL Server keeps (plan section 8.4).
/// </summary>
/// <param name="context">The write context of the scope, which the unit of work shares.</param>
public sealed class ProductRepository(CatalogDbContext context) : IProductRepository
{
    private readonly CatalogDbContext _context = context ?? throw new ArgumentNullException(nameof(context));

    /// <inheritdoc />
    public async Task<Versioned<Product>?> FindAsync(ProductId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        var product = await _context.Products.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        return product is null ? null : new Versioned<Product>(product, TagOf(product));
    }

    /// <inheritdoc />
    public Task<bool> SkuExistsAsync(Sku sku, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sku);

        return _context.Products.AsNoTracking().AnyAsync(candidate => candidate.Sku == sku, cancellationToken);
    }

    /// <inheritdoc />
    public void Add(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);

        _context.Products.Add(product);
    }

    /// <inheritdoc />
    public EntityTag TagOf(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);

        return RowVersions.TagOf(_context, product);
    }
}

/// <summary>Implements <see cref="ICategoryRepository"/> over the write context, like <see cref="ProductRepository"/>.</summary>
/// <param name="context">The write context of the scope, which the unit of work shares.</param>
public sealed class CategoryRepository(CatalogDbContext context) : ICategoryRepository
{
    private readonly CatalogDbContext _context = context ?? throw new ArgumentNullException(nameof(context));

    /// <inheritdoc />
    public async Task<Versioned<Category>?> FindAsync(CategoryId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        var category = await _context.Categories.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        return category is null ? null : new Versioned<Category>(category, TagOf(category));
    }

    /// <inheritdoc />
    public void Add(Category category)
    {
        ArgumentNullException.ThrowIfNull(category);

        _context.Categories.Add(category);
    }

    /// <inheritdoc />
    public EntityTag TagOf(Category category)
    {
        ArgumentNullException.ThrowIfNull(category);

        return RowVersions.TagOf(_context, category);
    }
}

/// <summary>Reads the version of a tracked aggregate from its row version.</summary>
internal static class RowVersions
{
    /// <summary>Returns the tag of a tracked entity as of the last save. An entity that was added and not saved yet has no version, and the call throws.</summary>
    public static EntityTag TagOf(DbContext context, object entity)
    {
        var entry = context.Entry(entity);

        if (entry.State == EntityState.Detached)
        {
            throw new InvalidOperationException("The aggregate is not tracked by this repository, so it has no version.");
        }

        return entry.Property(CategoryConfiguration.RowVersion).CurrentValue is byte[] { Length: > 0 } version
            ? EntityTag.FromBytes(version)
            : throw new InvalidOperationException("The aggregate was added and has not been saved yet, so it has no version. Call IUnitOfWork.SaveChangesAsync first.");
    }
}
