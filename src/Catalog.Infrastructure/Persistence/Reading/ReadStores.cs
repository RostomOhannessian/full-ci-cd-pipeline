using System.Globalization;
using Catalog.Application.Categories;
using Catalog.Application.Paging;
using Catalog.Application.Persistence;
using Catalog.Application.Products;
using Catalog.Domain.Categories;
using Catalog.Domain.Common;
using Catalog.Domain.Pricing;
using Catalog.Domain.Products;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Infrastructure.Persistence.Reading;

/// <summary>
/// Implements <see cref="IProductReadStore"/> over the read model. A query reads rows without tracking and maps them to views, and a page is a keyset query: the sort key and then the identifier, after a position,
/// so no row is skipped or repeated when others are added (plan section 8.4). The sort orders match the indexes of the table.
/// </summary>
/// <param name="context">The read context of the scope.</param>
public sealed class ProductReadStore(CatalogReadContext context) : IProductReadStore
{
    private readonly CatalogReadContext _context = context ?? throw new ArgumentNullException(nameof(context));

    /// <inheritdoc />
    public async Task<ProductView?> GetAsync(ProductId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        var guid = id.Value;
        var row = await _context.Products.SingleOrDefaultAsync(candidate => candidate.Id == guid, cancellationToken);

        return row is null ? null : ToView(row);
    }

    /// <inheritdoc />
    /// <remarks>A position whose sort key is not one that the store would have made returns no rows, because only a caller that changed a cursor by hand can send one.</remarks>
    public async Task<IReadOnlyList<ProductView>> ListAsync(ProductFilter filter, KeysetPosition? after, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = _context.Products.AsQueryable();

        if (filter.CategoryId is { } category)
        {
            var categoryGuid = category.Value;
            query = query.Where(row => row.CategoryId == categoryGuid);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(row => row.Status == status);
        }

        if (!string.IsNullOrEmpty(filter.Search))
        {
            var search = filter.Search;
            query = query.Where(row => row.Name.Contains(search) || row.Sku.Contains(search));
        }

        IQueryable<ProductRow> ordered;

        if (filter.Sort == ProductSort.Name)
        {
            if (after is not null)
            {
                var name = after.SortKey;
                var afterId = after.Id;
                query = query.Where(row => row.Name.CompareTo(name) > 0 || (row.Name == name && row.Id.CompareTo(afterId) > 0));
            }

            ordered = query.OrderBy(row => row.Name).ThenBy(row => row.Id);
        }
        else
        {
            if (after is not null)
            {
                if (!DateTimeOffset.TryParseExact(after.SortKey, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var createdAt))
                {
                    return [];
                }

                var afterId = after.Id;
                query = query.Where(row => row.CreatedAt < createdAt || (row.CreatedAt == createdAt && row.Id.CompareTo(afterId) > 0));
            }

            ordered = query.OrderByDescending(row => row.CreatedAt).ThenBy(row => row.Id);
        }

        var rows = await ordered.Take(limit).ToListAsync(cancellationToken);

        return [.. rows.Select(ToView)];
    }

    private static ProductView ToView(ProductRow row) =>
        new(
            row.Id,
            row.Sku,
            row.Name,
            row.Description,
            AmountOf(row),
            row.PriceCurrency,
            row.CategoryId,
            row.Status,
            row.CreatedAt,
            row.UpdatedAt,
            EntityTag.FromBytes(row.RowVersion).Value);

    // The column keeps two decimal places for every currency, and the model keeps as many as the currency has, so the amount goes through the model to read the same as the one that was written.
    private static decimal AmountOf(ProductRow row) =>
        Money.Create(row.PriceAmount, Currency.Create(row.PriceCurrency).Value).Value.Amount;
}

/// <summary>Implements <see cref="ICategoryReadStore"/> over the read model, like <see cref="ProductReadStore"/>.</summary>
/// <param name="context">The read context of the scope.</param>
public sealed class CategoryReadStore(CatalogReadContext context) : ICategoryReadStore
{
    private readonly CatalogReadContext _context = context ?? throw new ArgumentNullException(nameof(context));

    /// <inheritdoc />
    public async Task<CategoryView?> GetAsync(CategoryId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        var guid = id.Value;
        var row = await _context.Categories.SingleOrDefaultAsync(candidate => candidate.Id == guid, cancellationToken);

        return row is null ? null : ToView(row);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CategoryView>> ListAsync(LifecycleStatus? status, KeysetPosition? after, int limit, CancellationToken cancellationToken)
    {
        var query = _context.Categories.AsQueryable();

        if (status is { } kept)
        {
            query = query.Where(row => row.Status == kept);
        }

        if (after is not null)
        {
            var name = after.SortKey;
            var afterId = after.Id;
            query = query.Where(row => row.Name.CompareTo(name) > 0 || (row.Name == name && row.Id.CompareTo(afterId) > 0));
        }

        var rows = await query.OrderBy(row => row.Name).ThenBy(row => row.Id).Take(limit).ToListAsync(cancellationToken);

        return [.. rows.Select(ToView)];
    }

    private static CategoryView ToView(CategoryRow row) =>
        new(row.Id, row.Name, row.Status, row.CreatedAt, row.UpdatedAt, EntityTag.FromBytes(row.RowVersion).Value);
}
