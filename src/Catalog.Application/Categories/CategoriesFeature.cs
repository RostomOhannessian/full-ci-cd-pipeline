using Catalog.Application.Messaging;
using Catalog.Application.Paging;

namespace Catalog.Application.Categories;

/// <summary>Registers the use cases of categories (REQ-APP-006). The list is explicit and in a fixed order: the commands, then the queries.</summary>
public static class CategoriesFeature
{
    /// <summary>Registers the handlers of the category use cases.</summary>
    /// <param name="registry">The registry of the composition root.</param>
    /// <returns>The same registry, so features chain.</returns>
    public static IFeatureRegistry AddCategories(this IFeatureRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.AddCommandHandler<CreateCategory, CategoryView, CreateCategoryHandler>();
        registry.AddCommandHandler<RenameCategory, CategoryView, RenameCategoryHandler>();
        registry.AddCommandHandler<DiscontinueCategory, CategoryView, DiscontinueCategoryHandler>();
        registry.AddQueryHandler<GetCategory, CategoryView, GetCategoryHandler>();
        registry.AddQueryHandler<ListCategories, Page<CategoryView>, ListCategoriesHandler>();

        return registry;
    }
}
