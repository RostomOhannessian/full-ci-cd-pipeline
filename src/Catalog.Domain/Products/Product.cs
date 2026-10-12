using Catalog.Domain.Categories;
using Catalog.Domain.Common;
using Catalog.Domain.Pricing;

namespace Catalog.Domain.Products;

/// <summary>
/// An item of the catalog. A product belongs to one category, has a price, and is active until it is discontinued. The SKU and the identifier never
/// change. A discontinued product keeps its name and description editable, and it refuses a new price or a new category (plan section 8.3).
/// </summary>
/// <remarks>
/// The rules that need other aggregates take the other aggregate as an argument. A product is created in, and moved to, a <see cref="Category"/> that the caller
/// loaded, so the compiler stops code that skips the check. That the SKU is unique across the catalog is not a rule of one product, so the application checks it
/// first for a friendly error, and the unique index of the database is the rule that cannot be raced (plan section 8.3).
/// </remarks>
public sealed class Product : AggregateRoot<ProductId>
{
    private Product(ProductId id, Sku sku, ProductName name, Description? description, Money price, CategoryId categoryId, DateTimeOffset now)
        : base(id)
    {
        Sku = sku;
        Name = name;
        Description = description;
        Price = price;
        CategoryId = categoryId;
        Status = LifecycleStatus.Active;
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Gets the SKU, which never changes.</summary>
    public Sku Sku { get; }

    /// <summary>Gets the name.</summary>
    public ProductName Name { get; private set; }

    /// <summary>Gets the description, or null when the product has none.</summary>
    public Description? Description { get; private set; }

    /// <summary>Gets the price.</summary>
    public Money Price { get; private set; }

    /// <summary>Gets the category that the product belongs to.</summary>
    public CategoryId CategoryId { get; private set; }

    /// <summary>Gets the lifecycle status.</summary>
    public LifecycleStatus Status { get; private set; }

    /// <summary>Gets a value that says whether the product can still change its price and its category.</summary>
    public bool IsActive => Status == LifecycleStatus.Active;

    /// <summary>Gets when the product was created, in UTC.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Gets when the product last changed, in UTC.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Creates an active product in an active category and records <see cref="ProductCreated"/>.</summary>
    /// <param name="id">The identifier, which the caller made with its identifier generator.</param>
    /// <param name="sku">The SKU.</param>
    /// <param name="name">The name.</param>
    /// <param name="description">The description, or null.</param>
    /// <param name="price">The price.</param>
    /// <param name="category">The category that the product is created in, which must be active.</param>
    /// <param name="clock">The source of the current time.</param>
    public static DomainResult<Product> Create(ProductId id, Sku sku, ProductName name, Description? description, Money price, Category category, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(sku);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(price);
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(clock);

        if (!category.IsActive)
        {
            return DomainResult.Failure<Product>(CategoryNotActive());
        }

        var now = clock.GetUtcNow();
        var product = new Product(id, sku, name, description, price, category.Id, now);
        product.Raise(new ProductCreated(id, sku, name, description, price, category.Id, now));

        return DomainResult.Success<Product>(product);
    }

    /// <summary>
    /// Replaces the name, the description, the price, and the category in one step, as the <c>PUT</c> of the contract does. Either every change is accepted or none is.
    /// A value that equals the current one changes nothing, so repeating an update records nothing. For each part that changed, the product records
    /// <see cref="ProductDetailsChanged"/>, <see cref="ProductPriceChanged"/>, or <see cref="ProductCategoryChanged"/>.
    /// </summary>
    /// <remarks>
    /// A discontinued product refuses a new price and a new category. A product moves only to an active category, and keeping the category that it is already in
    /// is never a move, even when that category has been discontinued since, so such a product can still be renamed.
    /// </remarks>
    /// <param name="name">The new name.</param>
    /// <param name="description">The new description, or null for none.</param>
    /// <param name="price">The new price.</param>
    /// <param name="category">The category that the product should be in, which must be active when it differs from the current one.</param>
    /// <param name="clock">The source of the current time.</param>
    public DomainResult Update(ProductName name, Description? description, Money price, Category category, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(price);
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(clock);

        var priceChanges = price != Price;
        var categoryChanges = category.Id != CategoryId;

        if (!IsActive && (priceChanges || categoryChanges))
        {
            return DomainResult.Failure(new DomainError(DomainErrorCode.ProductDiscontinued, string.Empty, "The product is discontinued. Only its name and description can change."));
        }

        if (categoryChanges && !category.IsActive)
        {
            return DomainResult.Failure(CategoryNotActive());
        }

        var detailsChange = name != Name || description != Description;
        if (!detailsChange && !priceChanges && !categoryChanges)
        {
            return DomainResult.Success();
        }

        var now = clock.GetUtcNow();
        if (detailsChange)
        {
            Name = name;
            Description = description;
            Raise(new ProductDetailsChanged(Id, name, description, now));
        }

        if (priceChanges)
        {
            Raise(new ProductPriceChanged(Id, Price, price, now));
            Price = price;
        }

        if (categoryChanges)
        {
            Raise(new ProductCategoryChanged(Id, CategoryId, category.Id, now));
            CategoryId = category.Id;
        }

        UpdatedAt = now;

        return DomainResult.Success();
    }

    /// <summary>
    /// Discontinues the product and records <see cref="ProductDiscontinued"/>. Discontinuing a product that is already discontinued changes nothing and
    /// records nothing, so the action is idempotent (plan section 8.3).
    /// </summary>
    /// <param name="clock">The source of the current time.</param>
    public void Discontinue(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (!IsActive)
        {
            return;
        }

        var now = clock.GetUtcNow();
        Status = LifecycleStatus.Discontinued;
        UpdatedAt = now;
        Raise(new ProductDiscontinued(Id, now));
    }

    private static DomainError CategoryNotActive() =>
        new(DomainErrorCode.CategoryNotActive, "categoryId", "The category is discontinued, so no product can be assigned to it.");
}
