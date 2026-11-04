namespace Catalog.Contracts.V1;

/// <summary>A category as the API returns it (<c>Category</c> in the contract).</summary>
/// <param name="Id">The identifier, a UUID version 7.</param>
/// <param name="Name">The name.</param>
/// <param name="Status">The lifecycle, <c>active</c> or <c>discontinued</c>.</param>
/// <param name="CreatedAt">When the category was created, in UTC.</param>
/// <param name="UpdatedAt">When the category last changed, in UTC.</param>
public sealed record Category(Guid Id, string Name, string Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>The body that creates a category (<c>CategoryCreate</c> in the contract). A missing name is a validation error, so the member can be null here.</summary>
/// <param name="Name">The name of the new category.</param>
public sealed record CategoryCreate(string? Name);

/// <summary>The body that renames a category (<c>CategoryUpdate</c> in the contract). A missing name is a validation error, so the member can be null here.</summary>
/// <param name="Name">The new name of the category.</param>
public sealed record CategoryUpdate(string? Name);

/// <summary>One page of categories (<c>CategoryPage</c> in the contract).</summary>
/// <param name="Items">The categories of the page.</param>
/// <param name="NextCursor">The cursor of the next page, or null on the last page.</param>
public sealed record CategoryPage(IReadOnlyList<Category> Items, string? NextCursor);
