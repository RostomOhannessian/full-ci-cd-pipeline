using Catalog.Domain.Categories;
using Catalog.Domain.Products;

namespace Catalog.Application.Persistence;

/// <summary>
/// Loads and stores products for the command side. The persistence layer implements it (WP1.8). Reads for the query side go through the read stores that the
/// queries define (WP1.7), so a command handler never loads a page and a query handler never holds an aggregate.
/// </summary>
public interface IProductRepository
{
    /// <summary>Loads a product with the version that it has now, or returns null when no product has the identifier.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    Task<Versioned<Product>?> FindAsync(ProductId id, CancellationToken cancellationToken);

    /// <summary>Says whether a product already uses the SKU, whatever its status. It gives a friendly error first, and the unique index is the rule that cannot be raced.</summary>
    /// <param name="sku">The normalized SKU.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    Task<bool> SkuExistsAsync(Sku sku, CancellationToken cancellationToken);

    /// <summary>Starts to track a new product. It is written when the unit of work saves.</summary>
    /// <param name="product">The product.</param>
    void Add(Product product);

    /// <summary>
    /// Returns the version of a product that this repository loaded or added, as of the last <see cref="IUnitOfWork.SaveChangesAsync"/>. Before the first save, an
    /// added product has no version, and the call throws <see cref="InvalidOperationException"/>.
    /// </summary>
    /// <param name="product">The product.</param>
    EntityTag TagOf(Product product);
}

/// <summary>Loads and stores categories for the command side. The persistence layer implements it (WP1.8).</summary>
public interface ICategoryRepository
{
    /// <summary>Loads a category with the version that it has now, or returns null when no category has the identifier.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    Task<Versioned<Category>?> FindAsync(CategoryId id, CancellationToken cancellationToken);

    /// <summary>Starts to track a new category. It is written when the unit of work saves.</summary>
    /// <param name="category">The category.</param>
    void Add(Category category);

    /// <summary>Returns the version of a category as of the last <see cref="IUnitOfWork.SaveChangesAsync"/>, and throws <see cref="InvalidOperationException"/> for an added category before the first save.</summary>
    /// <param name="category">The category.</param>
    EntityTag TagOf(Category category);
}
