namespace Catalog.Application.Caching;

/// <summary>
/// The names of the tags that group cache entries (plan section 8.5, ADR-0039). A query attaches them when it reads, and the invalidation policy removes them when an event changes the data, so both sides
/// build a tag from one place and a typo cannot make a write miss the entry that it changed.
/// </summary>
public static class CacheTags
{
    /// <summary>The tag of every list of products, whatever its filter.</summary>
    public const string ProductList = "products:list";

    /// <summary>Makes the tag of one product, which its entry carries.</summary>
    /// <param name="productId">The identifier of the product.</param>
    public static string Product(Guid productId) => $"product:{productId:D}";

    /// <summary>Makes the tag of one category, which the lists that are narrowed to it carry.</summary>
    /// <param name="categoryId">The identifier of the category.</param>
    public static string Category(Guid categoryId) => $"category:{categoryId:D}";
}
