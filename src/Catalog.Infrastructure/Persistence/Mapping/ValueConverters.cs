using Catalog.Domain.Categories;
using Catalog.Domain.Pricing;
using Catalog.Domain.Products;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Catalog.Infrastructure.Persistence.Mapping;

// The value objects of the Domain have private constructors and make themselves through Create, so each converter rebuilds the value through Create. A stored value passed Create when it was written,
// so a failure here means that the row was changed by hand, and the exception names the converter and not the value.

internal sealed class ProductIdConverter : ValueConverter<ProductId, Guid>
{
    public ProductIdConverter()
        : base(id => id.Value, value => ProductId.Create(value).Value)
    {
    }
}

internal sealed class CategoryIdConverter : ValueConverter<CategoryId, Guid>
{
    public CategoryIdConverter()
        : base(id => id.Value, value => CategoryId.Create(value).Value)
    {
    }
}

internal sealed class SkuConverter : ValueConverter<Sku, string>
{
    public SkuConverter()
        : base(sku => sku.Value, value => Sku.Create(value).Value)
    {
    }
}

internal sealed class ProductNameConverter : ValueConverter<ProductName, string>
{
    public ProductNameConverter()
        : base(name => name.Value, value => ProductName.Create(value).Value)
    {
    }
}

internal sealed class CategoryNameConverter : ValueConverter<CategoryName, string>
{
    public CategoryNameConverter()
        : base(name => name.Value, value => CategoryName.Create(value).Value)
    {
    }
}

internal sealed class DescriptionConverter : ValueConverter<Description?, string>
{
    // A stored description is never empty, because the model turns blank text into no description, so Create returns a description for it. EF Core never passes a null to a converter.
    public DescriptionConverter()
        : base(description => description!.Value, value => Description.Create(value).Value)
    {
    }
}

internal sealed class CurrencyConverter : ValueConverter<Currency, string>
{
    public CurrencyConverter()
        : base(currency => currency.Code, value => Currency.Create(value).Value)
    {
    }
}
