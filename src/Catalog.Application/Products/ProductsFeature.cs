using Catalog.Application.Messaging;
using Catalog.Application.Paging;

namespace Catalog.Application.Products;

/// <summary>Registers the use cases of products (REQ-APP-006). The list is explicit and in a fixed order: the commands, then the queries.</summary>
public static class ProductsFeature
{
    /// <summary>Registers the handlers of the product use cases.</summary>
    /// <param name="registry">The registry of the composition root.</param>
    /// <returns>The same registry, so features chain.</returns>
    public static IFeatureRegistry AddProducts(this IFeatureRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.AddCommandHandler<CreateProduct, ProductView, CreateProductHandler>();
        registry.AddCommandHandler<UpdateProduct, ProductView, UpdateProductHandler>();
        registry.AddCommandHandler<DiscontinueProduct, ProductView, DiscontinueProductHandler>();
        registry.AddQueryHandler<GetProduct, ProductView, GetProductHandler>();
        registry.AddQueryHandler<ListProducts, Page<ProductView>, ListProductsHandler>();

        return registry;
    }
}
