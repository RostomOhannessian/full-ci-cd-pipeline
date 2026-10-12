using Catalog.Domain.Categories;
using Catalog.Domain.Common;
using Catalog.Domain.Pricing;

namespace Catalog.Domain.Products;

/// <summary>A product was created.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="Sku">The SKU, which never changes.</param>
/// <param name="Name">The name that it was created with.</param>
/// <param name="Description">The description that it was created with, or null.</param>
/// <param name="Price">The price that it was created with.</param>
/// <param name="CategoryId">The category that it was created in.</param>
/// <param name="OccurredAt">When it happened, in UTC.</param>
public sealed record ProductCreated(
    ProductId ProductId,
    Sku Sku,
    ProductName Name,
    Description? Description,
    Money Price,
    CategoryId CategoryId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    /// <inheritdoc />
    public string EventType => nameof(ProductCreated);

    /// <inheritdoc />
    public int SchemaVersion => 1;
}

/// <summary>The name or the description of a product changed.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="Name">The name that it has now.</param>
/// <param name="Description">The description that it has now, or null.</param>
/// <param name="OccurredAt">When it happened, in UTC.</param>
public sealed record ProductDetailsChanged(ProductId ProductId, ProductName Name, Description? Description, DateTimeOffset OccurredAt) : IDomainEvent
{
    /// <inheritdoc />
    public string EventType => nameof(ProductDetailsChanged);

    /// <inheritdoc />
    public int SchemaVersion => 1;
}

/// <summary>The price of a product changed.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="OldPrice">The price that it had.</param>
/// <param name="NewPrice">The price that it has now.</param>
/// <param name="OccurredAt">When it happened, in UTC.</param>
public sealed record ProductPriceChanged(ProductId ProductId, Money OldPrice, Money NewPrice, DateTimeOffset OccurredAt) : IDomainEvent
{
    /// <inheritdoc />
    public string EventType => nameof(ProductPriceChanged);

    /// <inheritdoc />
    public int SchemaVersion => 1;
}

/// <summary>A product moved to another category.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="OldCategoryId">The category that it was in.</param>
/// <param name="NewCategoryId">The category that it is in now.</param>
/// <param name="OccurredAt">When it happened, in UTC.</param>
public sealed record ProductCategoryChanged(ProductId ProductId, CategoryId OldCategoryId, CategoryId NewCategoryId, DateTimeOffset OccurredAt) : IDomainEvent
{
    /// <inheritdoc />
    public string EventType => nameof(ProductCategoryChanged);

    /// <inheritdoc />
    public int SchemaVersion => 1;
}

/// <summary>A product was discontinued.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="OccurredAt">When it happened, in UTC.</param>
public sealed record ProductDiscontinued(ProductId ProductId, DateTimeOffset OccurredAt) : IDomainEvent
{
    /// <inheritdoc />
    public string EventType => nameof(ProductDiscontinued);

    /// <inheritdoc />
    public int SchemaVersion => 1;
}
