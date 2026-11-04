using System.Globalization;
using Catalog.Application.Categories;
using Catalog.Application.Paging;
using Catalog.Application.Products;
using Catalog.Contracts.V1;
using Catalog.Domain.Common;
using Currency = Catalog.Domain.Pricing.Currency;

namespace Catalog.Api.Mapping;

/// <summary>
/// Turns the views of the use cases into the records of the contract. The views carry plain values and the contract carries text, so this is the one place that writes an amount with the decimal places of its
/// currency (<c>1990</c> for the yen and <c>89.99</c> for the dollar) and a status as <c>active</c> or <c>discontinued</c>. The persistence layer may hand back more decimal places than the currency has, and
/// the contract never shows them.
/// </summary>
internal static class ViewMapping
{
    /// <summary>Gets the statuses that a filter may name, by the name that the contract uses.</summary>
    public static IReadOnlyDictionary<string, LifecycleStatus> StatusesByName { get; } = new Dictionary<string, LifecycleStatus>(StringComparer.Ordinal)
    {
        ["active"] = LifecycleStatus.Active,
        ["discontinued"] = LifecycleStatus.Discontinued,
    };

    /// <summary>Describes a category as the contract does.</summary>
    /// <param name="view">The view of the use case.</param>
    public static Category ToContract(this CategoryView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return new Category(view.Id, view.Name, NameOf(view.Status), view.CreatedAt, view.UpdatedAt);
    }

    /// <summary>Describes a product as the contract does.</summary>
    /// <param name="view">The view of the use case.</param>
    public static Product ToContract(this ProductView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return new Product(
            view.Id,
            view.Sku,
            view.Name,
            view.Description,
            new Money(Format(view.Amount, view.Currency), view.Currency),
            view.CategoryId,
            NameOf(view.Status),
            view.CreatedAt,
            view.UpdatedAt);
    }

    /// <summary>Describes a page of categories as the contract does.</summary>
    /// <param name="page">The page of the use case.</param>
    public static CategoryPage ToContract(this Page<CategoryView> page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new CategoryPage([.. page.Items.Select(item => item.ToContract())], page.NextCursor);
    }

    /// <summary>Describes a page of products as the contract does.</summary>
    /// <param name="page">The page of the use case.</param>
    public static ProductPage ToContract(this Page<ProductView> page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new ProductPage([.. page.Items.Select(item => item.ToContract())], page.NextCursor);
    }

    private static string NameOf(LifecycleStatus status) => StatusesByName.First(pair => pair.Value == status).Key;

    private static string Format(decimal amount, string currencyCode)
    {
        var places = Currency.Create(currencyCode) is { IsSuccess: true } currency ? currency.Value.MinorUnits : 2;

        return decimal.Round(amount, places, MidpointRounding.ToZero).ToString($"F{places}", CultureInfo.InvariantCulture);
    }
}
