using Catalog.Domain.Categories;
using Catalog.Domain.Common;
using Catalog.Domain.Pricing;
using Catalog.Domain.Products;
using Microsoft.Extensions.Time.Testing;

namespace Catalog.Domain.Tests.Support;

/// <summary>
/// Synthetic builders. They make valid values with short names, so a test states only the part that it is about. The data is invented and is the same
/// on every run.
/// </summary>
internal static class Make
{
    /// <summary>The instant that every <see cref="Clock"/> starts at.</summary>
    public static readonly DateTimeOffset Epoch = new(2026, 1, 15, 9, 30, 0, TimeSpan.Zero);

    public static FakeTimeProvider Clock() => new(Epoch);

    public static Money Usd(decimal amount) => Money.Create(amount, Currency.Usd).Value;

    public static Money Jpy(decimal amount) => Money.Create(amount, Currency.Jpy).Value;

    public static Sku Sku(string value = "KB-100") => Products.Sku.Create(value).Value;

    public static ProductName ProductName(string value = "Mechanical keyboard") => Products.ProductName.Create(value).Value;

    public static CategoryName CategoryName(string value = "Keyboards") => Categories.CategoryName.Create(value).Value;

    public static Description? Description(string? value = "A tenkeyless keyboard.") => Products.Description.Create(value).Value;

    public static Category Category(long counter = 1, string name = "Keyboards") => Categories.Category.Create(TestIds.Category(counter), CategoryName(name), Clock());

    public static Category DiscontinuedCategory(long counter = 1, string name = "Keyboards")
    {
        var category = Category(counter, name);
        category.Discontinue(Clock());
        category.ClearDomainEvents();

        return category;
    }

    /// <summary>Creates an active product in <paramref name="category"/> and clears the events of its creation, so a test sees only what it causes.</summary>
    public static Product Product(Category category, TimeProvider clock, long counter = 1, string sku = "KB-100", decimal price = 89.90m)
    {
        var result = Products.Product.Create(TestIds.Product(counter), Sku(sku), ProductName(), Description(), Usd(price), category, clock);
        var product = result.Value;
        product.ClearDomainEvents();

        return product;
    }

    public static DomainError ErrorOf<T>(DomainResult<T> result) =>
        result.IsSuccess ? throw new InvalidOperationException("The result succeeded, so it has no error.") : result.Error;
}
