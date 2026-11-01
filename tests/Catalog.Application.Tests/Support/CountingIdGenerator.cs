using Catalog.Application.Identity;
using Catalog.Domain.Categories;
using Catalog.Domain.Products;
using Catalog.Domain.Tests.Support;

namespace Catalog.Application.Tests.Support;

/// <summary>An identifier generator for tests. It builds each identifier from a counter, so the same call gives the same identifier on every run (plan section 10.3).</summary>
internal sealed class CountingIdGenerator(long start = 1) : IIdGenerator
{
    private long _next = start - 1;

    public ProductId NewProductId() => TestIds.Product(++_next);

    public CategoryId NewCategoryId() => TestIds.Category(++_next);
}
