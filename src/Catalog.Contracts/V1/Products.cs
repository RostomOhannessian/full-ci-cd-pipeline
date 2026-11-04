namespace Catalog.Contracts.V1;

/// <summary>A product as the API returns it (<c>Product</c> in the contract).</summary>
/// <param name="Id">The identifier, a UUID version 7.</param>
/// <param name="Sku">The SKU in its canonical, upper-case form.</param>
/// <param name="Name">The name.</param>
/// <param name="Description">The description, or null when the product has none.</param>
/// <param name="Price">The price.</param>
/// <param name="CategoryId">The identifier of the category.</param>
/// <param name="Status">The lifecycle, <c>active</c> or <c>discontinued</c>.</param>
/// <param name="CreatedAt">When the product was created, in UTC.</param>
/// <param name="UpdatedAt">When the product last changed, in UTC.</param>
public sealed record Product(Guid Id, string Sku, string Name, string? Description, Money Price, Guid CategoryId, string Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>The body that creates a product (<c>ProductCreate</c> in the contract). A missing member is a validation error, so the members that the contract requires can be null here.</summary>
/// <param name="Sku">The SKU, in any case.</param>
/// <param name="Name">The name.</param>
/// <param name="Description">The description, or null.</param>
/// <param name="Price">The price.</param>
/// <param name="CategoryId">The identifier of an active category.</param>
public sealed record ProductCreate(string? Sku, string? Name, string? Description, Money? Price, Guid? CategoryId);

/// <summary>The body that replaces a product (<c>ProductUpdate</c> in the contract). A missing member is a validation error, so the members that the contract requires can be null here.</summary>
/// <param name="Name">The name.</param>
/// <param name="Description">The description, or null to clear it.</param>
/// <param name="Price">The price.</param>
/// <param name="CategoryId">The identifier of the category.</param>
public sealed record ProductUpdate(string? Name, string? Description, Money? Price, Guid? CategoryId);

/// <summary>One page of products (<c>ProductPage</c> in the contract).</summary>
/// <param name="Items">The products of the page.</param>
/// <param name="NextCursor">The cursor of the next page, or null on the last page.</param>
public sealed record ProductPage(IReadOnlyList<Product> Items, string? NextCursor);
