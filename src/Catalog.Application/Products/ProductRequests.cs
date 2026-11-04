using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Catalog.Application.Caching;
using Catalog.Application.Idempotency;
using Catalog.Application.Messaging;
using Catalog.Application.Paging;
using Catalog.Application.Persistence;
using Catalog.Application.Security;
using Catalog.Domain.Categories;
using Catalog.Domain.Common;
using Catalog.Domain.Products;

namespace Catalog.Application.Products;

/// <summary>A product as a caller sees it. The values are plain, so the API maps them to the contract and the idempotency store keeps them as JSON.</summary>
/// <param name="Id">The identifier.</param>
/// <param name="Sku">The normalized SKU.</param>
/// <param name="Name">The name.</param>
/// <param name="Description">The description, or null.</param>
/// <param name="Amount">The amount of the price.</param>
/// <param name="Currency">The ISO 4217 code of the currency of the price.</param>
/// <param name="CategoryId">The identifier of the category.</param>
/// <param name="Status">The lifecycle status.</param>
/// <param name="CreatedAt">When the product was created.</param>
/// <param name="UpdatedAt">When the product last changed.</param>
/// <param name="ETag">The version of the product as the token of an <see cref="EntityTag"/>, which the API writes as a strong <c>ETag</c>.</param>
public sealed record ProductView(
    Guid Id,
    string Sku,
    string Name,
    string? Description,
    decimal Amount,
    string Currency,
    Guid CategoryId,
    LifecycleStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string ETag)
{
    /// <summary>Describes a product that the repository loaded or saved.</summary>
    /// <param name="product">The product.</param>
    /// <param name="tag">The version of the product.</param>
    public static ProductView From(Product product, EntityTag tag)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(tag);

        return new ProductView(
            product.Id.Value,
            product.Sku.Value,
            product.Name.Value,
            product.Description?.Value,
            product.Price.Amount,
            product.Price.Currency.Code,
            product.CategoryId.Value,
            product.Status,
            product.CreatedAt,
            product.UpdatedAt,
            tag.Value);
    }
}

/// <summary>Creates a product (<c>createProduct</c>). It needs the write level.</summary>
/// <param name="Sku">The SKU, which the model normalizes to upper case.</param>
/// <param name="Name">The name.</param>
/// <param name="Description">The description, or null.</param>
/// <param name="Amount">The amount of the price.</param>
/// <param name="Currency">The ISO 4217 code of the currency.</param>
/// <param name="CategoryId">The identifier of an active category.</param>
/// <param name="IdempotencyKey">The key of the request, or null when the caller sent none.</param>
public sealed record CreateProduct(string? Sku, string? Name, string? Description, decimal Amount, string? Currency, Guid CategoryId, IdempotencyKey? IdempotencyKey = null)
    : IProtectedRequest, IIdempotentCommand
{
    /// <inheritdoc />
    public static AccessLevel RequiredAccess => AccessLevel.Write;
}

/// <summary>Replaces the name, the description, the price, and the category of a product (<c>updateProduct</c>). It needs the write level and the current version.</summary>
/// <param name="ProductId">The identifier of the product.</param>
/// <param name="Name">The name.</param>
/// <param name="Description">The description, or null.</param>
/// <param name="Amount">The amount of the price.</param>
/// <param name="Currency">The ISO 4217 code of the currency.</param>
/// <param name="CategoryId">The identifier of the category.</param>
/// <param name="IfMatch">The version that the caller read, or null when it sent none.</param>
public sealed record UpdateProduct(Guid ProductId, string? Name, string? Description, decimal Amount, string? Currency, Guid CategoryId, EntityTag? IfMatch) : IProtectedRequest
{
    /// <inheritdoc />
    public static AccessLevel RequiredAccess => AccessLevel.Write;
}

/// <summary>Discontinues a product (<c>discontinueProduct</c>). It needs the admin level and the current version, and it changes nothing for a product that is already discontinued.</summary>
/// <param name="ProductId">The identifier of the product.</param>
/// <param name="IfMatch">The version that the caller read, or null when it sent none.</param>
public sealed record DiscontinueProduct(Guid ProductId, EntityTag? IfMatch) : IProtectedRequest
{
    /// <inheritdoc />
    public static AccessLevel RequiredAccess => AccessLevel.Admin;
}

/// <summary>Reads one product (<c>getProduct</c>). It needs the read level, and the result may come from the cache.</summary>
/// <param name="ProductId">The identifier of the product.</param>
public sealed record GetProduct(Guid ProductId) : IProtectedRequest, ICachedQuery
{
    /// <inheritdoc />
    public static AccessLevel RequiredAccess => AccessLevel.Read;

    /// <inheritdoc />
    public CacheKey CacheKey => new($"product:{ProductId:D}", [CacheTags.Product(ProductId)]);
}

/// <summary>The sort orders of a list of products, which the cursor names (ADR-0028).</summary>
public enum ProductSort
{
    /// <summary>Newest first.</summary>
    CreatedAt,

    /// <summary>A to Z.</summary>
    Name,
}

/// <summary>What a list of products is narrowed by.</summary>
/// <param name="CategoryId">The category, or null for every category.</param>
/// <param name="Status">The status, or null for every status.</param>
/// <param name="Search">The text to look for in the name, or null.</param>
/// <param name="Sort">The sort order.</param>
public sealed record ProductFilter(CategoryId? CategoryId, LifecycleStatus? Status, string? Search, ProductSort Sort);

/// <summary>Reads a page of products (<c>listProducts</c>). It needs the read level, and the result may come from the cache.</summary>
/// <param name="CategoryId">Only the products of this category, or null.</param>
/// <param name="Status">Only the products with this status, or null.</param>
/// <param name="Q">Only the products whose name holds this text, or null.</param>
/// <param name="Sort">The sort order, <c>createdAt</c> or <c>name</c>.</param>
/// <param name="PageSize">The number of items that the caller wants.</param>
/// <param name="Cursor">The cursor of the page, or null for the first one.</param>
public sealed record ListProducts(
    Guid? CategoryId = null,
    LifecycleStatus? Status = null,
    string? Q = null,
    string Sort = ListProducts.DefaultSort,
    int PageSize = PageRequest.DefaultSize,
    string? Cursor = null) : IProtectedRequest, ICachedQuery
{
    /// <summary>The sort order when the caller names none.</summary>
    public const string DefaultSort = "createdAt";

    /// <inheritdoc />
    public static AccessLevel RequiredAccess => AccessLevel.Read;

    /// <inheritdoc />
    public CacheKey CacheKey => new($"products:list:{Hash()}", CategoryId is { } category ? [CacheTags.ProductList, CacheTags.Category(category)] : [CacheTags.ProductList]);

    /// <summary>Reads the name of a sort order, or returns null when it is not one of the two.</summary>
    /// <param name="name">The name that the caller sent.</param>
    public static ProductSort? ParseSort(string? name) => name switch
    {
        "createdAt" => ProductSort.CreatedAt,
        "name" => ProductSort.Name,
        _ => null,
    };

    // The same query gives the same key. The hash keeps the key short and free of text that a caller chose.
    private string Hash()
    {
        var text = string.Join('|', CategoryId?.ToString("D") ?? string.Empty, Status?.ToString() ?? string.Empty, Q ?? string.Empty, Sort, PageSize.ToString(CultureInfo.InvariantCulture), Cursor ?? string.Empty);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..32];
    }
}

/// <summary>Reads products for the query side. The persistence layer implements it as a read-only projection (WP1.8), so a query handler never loads an aggregate.</summary>
public interface IProductReadStore
{
    /// <summary>Reads one product, or returns null when no product has the identifier.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    Task<ProductView?> GetAsync(ProductId id, CancellationToken cancellationToken);

    /// <summary>
    /// Reads up to <paramref name="limit"/> products that match the filter, in the order of the sort (newest first for <see cref="ProductSort.CreatedAt"/>, A to Z for <see cref="ProductSort.Name"/>, and
    /// then by identifier), after a position.
    /// </summary>
    /// <param name="filter">What to narrow by, and how to sort.</param>
    /// <param name="after">The position after which to start, or null for the first page.</param>
    /// <param name="limit">The largest number of products to return.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    Task<IReadOnlyList<ProductView>> ListAsync(ProductFilter filter, KeysetPosition? after, int limit, CancellationToken cancellationToken);
}
