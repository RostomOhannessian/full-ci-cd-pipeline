using System.Data.SqlTypes;
using System.Globalization;
using AwesomeAssertions;
using Catalog.Application.Categories;
using Catalog.Application.Paging;
using Catalog.Application.Products;
using Catalog.Domain.Common;
using Catalog.Domain.Products;
using Catalog.Domain.Tests.Support;
using Catalog.Infrastructure.Tests.Support;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Catalog.Infrastructure.Tests;

/// <summary>
/// REQ-DATA-001 and REQ-APP-001: the read stores answer the query side without loading an aggregate. A page is a keyset query, so every row comes exactly once, in the order of the sort, even when rows
/// have the same sort key and when rows are added between two pages (plan section 8.4, ADR-0028).
/// </summary>
public sealed class ReadStoreTests(CatalogDatabaseFixture database) : IClassFixture<CatalogDatabaseFixture>
{
    [Fact]
    [Trait("Requirement", "REQ-DATA-001")]
    public async Task A_product_is_read_as_a_view_with_the_version_that_the_repository_reports()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        var category = Make.Category(Counters.Next());
        var product = Make.Product(category, clock, Counters.Next(), Counters.Sku(), 89.90m);
        await using var scope = new CatalogScope(database);
        await scope.SaveAsync(category);
        var tag = await scope.SaveAsync(product);

        var view = await scope.ProductReads.GetAsync(product.Id, CancellationToken.None);

        view.Should().Be(ProductView.From(product, tag));
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-001")]
    public async Task A_price_in_yen_is_read_without_decimal_places_like_the_model_writes_it()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        var category = Make.Category(Counters.Next());
        var product = Product.Create(TestIds.Product(Counters.Next()), Make.Sku(Counters.Sku()), Make.ProductName(), null, Make.Jpy(1500m), category, clock).Value;
        await using var scope = new CatalogScope(database);
        await scope.SaveAsync(category);
        await scope.SaveAsync(product);

        var view = await scope.ProductReads.GetAsync(product.Id, CancellationToken.None);

        view!.Amount.ToString(CultureInfo.InvariantCulture).Should().Be("1500");
        view.Currency.Should().Be("JPY");
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-001")]
    public async Task An_identifier_that_no_row_has_reads_as_nothing()
    {
        await using var scope = new CatalogScope(database);

        (await scope.ProductReads.GetAsync(TestIds.Product(Counters.Next()), CancellationToken.None)).Should().BeNull();
        (await scope.CategoryReads.GetAsync(TestIds.Category(Counters.Next()), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-001")]
    public async Task A_category_is_read_as_a_view_with_the_version_that_the_repository_reports()
    {
        await database.ResetAsync();
        var category = Make.Category(Counters.Next(), "Keyboards");
        await using var scope = new CatalogScope(database);
        var tag = await scope.SaveAsync(category);

        var view = await scope.CategoryReads.GetAsync(category.Id, CancellationToken.None);

        view.Should().Be(CategoryView.From(category, tag));
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-001")]
    public async Task Products_newest_first_come_once_each_in_order_even_when_several_share_a_creation_time()
    {
        var all = await SeedProductsAsync();

        var read = await ReadAllAsync(new ProductFilter(null, null, null, ProductSort.CreatedAt), pageSize: 7);

        read.Select(view => view.Id).Should().OnlyHaveUniqueItems().And.BeEquivalentTo(all.Select(product => product.Id.Value));
        read.Should().BeInDescendingOrder(view => view.CreatedAt);
        foreach (var group in read.GroupBy(view => view.CreatedAt))
        {
            // SQL Server orders a uniqueidentifier by its own rule (the last six bytes first), and SqlGuid applies the same rule. The sort and the keyset predicate both run in SQL Server, so they agree.
            group.Select(view => new SqlGuid(view.Id)).Should().BeInAscendingOrder("a tie is broken by the identifier, in the order of SQL Server");
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-001")]
    public async Task Products_by_name_come_once_each_in_order_even_when_names_repeat_or_differ_only_in_case()
    {
        var all = await SeedProductsAsync();

        var read = await ReadAllAsync(new ProductFilter(null, null, null, ProductSort.Name), pageSize: 5);

        read.Select(view => view.Id).Should().OnlyHaveUniqueItems().And.BeEquivalentTo(all.Select(product => product.Id.Value));
        read.Select(view => view.Name).Should().BeInAscendingOrder(StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-001")]
    public async Task A_row_that_arrives_between_two_pages_is_neither_skipped_nor_repeated()
    {
        await SeedProductsAsync();
        await using var reader = new CatalogScope(database);
        var filter = new ProductFilter(null, null, null, ProductSort.CreatedAt);
        var first = await reader.ProductReads.ListAsync(filter, null, 6, CancellationToken.None);
        var keptFirstPage = first.Take(5).ToList();

        // A product that is newer than everything arrives after the first page was read.
        var clock = new FakeTimeProvider(Make.Epoch.AddDays(30));
        var category = Make.Category(Counters.Next());
        await using (var writer = new CatalogScope(database))
        {
            await writer.SaveAsync(category);
            await writer.SaveAsync(Make.Product(category, clock, Counters.Next(), Counters.Sku()));
        }

        var second = await reader.ProductReads.ListAsync(filter, Position(keptFirstPage[^1], ProductSort.CreatedAt), 100, CancellationToken.None);

        second.Select(view => view.Id).Should().NotIntersectWith(keptFirstPage.Select(view => view.Id));
        keptFirstPage.Count.Should().Be(5);
        second.Should().HaveCount((await CountAsync()) - 5 - 1, "the new product sorts before the position, so only the rest of the old rows follow it");
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-001")]
    public async Task The_filters_narrow_the_list_by_category_status_and_name()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        var keyboards = Make.Category(Counters.Next(), "Keyboards");
        var mice = Make.Category(Counters.Next(), "Mice");
        var cotton = Product.Create(TestIds.Product(Counters.Next()), Make.Sku(Counters.Sku()), Make.ProductName("100% cotton mat"), null, Make.Usd(5m), mice, clock).Value;
        var cottonish = Product.Create(TestIds.Product(Counters.Next()), Make.Sku(Counters.Sku()), Make.ProductName("1000 cotton mats"), null, Make.Usd(5m), mice, clock).Value;
        var keyboard = Make.Product(keyboards, clock, Counters.Next(), Counters.Sku());
        var old = Make.Product(keyboards, clock, Counters.Next(), Counters.Sku());
        old.Discontinue(clock);
        await using var scope = new CatalogScope(database);
        await scope.SaveAsync(keyboards);
        await scope.SaveAsync(mice);
        foreach (var product in new[] { cotton, cottonish, keyboard, old })
        {
            await scope.SaveAsync(product);
        }

        async Task<IReadOnlyList<Guid>> Ids(ProductFilter filter) =>
            [.. (await scope.ProductReads.ListAsync(filter, null, 100, CancellationToken.None)).Select(view => view.Id)];

        (await Ids(new ProductFilter(mice.Id, null, null, ProductSort.Name))).Should().BeEquivalentTo([cotton.Id.Value, cottonish.Id.Value]);
        (await Ids(new ProductFilter(null, LifecycleStatus.Discontinued, null, ProductSort.Name))).Should().BeEquivalentTo([old.Id.Value]);
        (await Ids(new ProductFilter(keyboards.Id, LifecycleStatus.Active, null, ProductSort.Name))).Should().BeEquivalentTo([keyboard.Id.Value]);
        (await Ids(new ProductFilter(null, null, "COTTON", ProductSort.Name))).Should().BeEquivalentTo([cotton.Id.Value, cottonish.Id.Value], "the search ignores case");
        (await Ids(new ProductFilter(null, null, "100%", ProductSort.Name))).Should().BeEquivalentTo([cotton.Id.Value], "a percent sign in the search is a character and not a wildcard");
        (await Ids(new ProductFilter(null, null, "no such name", ProductSort.Name))).Should().BeEmpty();
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-001")]
    public async Task A_cursor_that_a_caller_changed_by_hand_returns_no_rows_and_no_error()
    {
        await SeedProductsAsync();
        await using var scope = new CatalogScope(database);

        var rows = await scope.ProductReads.ListAsync(new ProductFilter(null, null, null, ProductSort.CreatedAt), new KeysetPosition("not a time", TestIds.V7(1)), 10, CancellationToken.None);

        rows.Should().BeEmpty();
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-001")]
    public async Task Categories_come_once_each_ordered_by_name_and_then_by_identifier()
    {
        await database.ResetAsync();
        var names = new[] { "Mice", "Keyboards", "keyboards", "Cables", "Monitors", "Cables", "Docks" };
        var categories = names.Select(name => Make.Category(Counters.Next(), name)).ToList();
        await using var scope = new CatalogScope(database);
        foreach (var category in categories)
        {
            await scope.SaveAsync(category);
        }

        List<CategoryView> read = [];
        KeysetPosition? after = null;
        while (true)
        {
            var rows = await scope.CategoryReads.ListAsync(after, 3, CancellationToken.None);
            read.AddRange(rows);
            if (rows.Count < 3)
            {
                break;
            }

            after = new KeysetPosition(rows[^1].Name, rows[^1].Id);
        }

        read.Select(view => view.Id).Should().OnlyHaveUniqueItems().And.BeEquivalentTo(categories.Select(category => category.Id.Value));
        read.Select(view => view.Name).Should().BeInAscendingOrder(StringComparer.OrdinalIgnoreCase);
    }

    // Seeds 24 products in two categories. The creation times step by one second except for runs of three that share an instant, and the names repeat and differ in case.
    private async Task<IReadOnlyList<Product>> SeedProductsAsync()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        var first = Make.Category(Counters.Next(), "Keyboards");
        var second = Make.Category(Counters.Next(), "Mice");
        var names = new[] { "Alpha", "alpha", "Bravo", "Alpha", "Charlie", "bravo", "Delta", "Echo" };
        List<Product> products = [];

        for (var index = 0; index < 24; index++)
        {
            if (index % 3 == 0)
            {
                clock.Advance(TimeSpan.FromSeconds(1));
            }

            var category = index % 2 == 0 ? first : second;
            products.Add(Product.Create(TestIds.Product(Counters.Next()), Make.Sku(Counters.Sku()), Make.ProductName(names[index % names.Length]), null, Make.Usd(10m + index), category, clock).Value);
        }

        await using var scope = new CatalogScope(database);
        await scope.SaveAsync(first);
        await scope.SaveAsync(second);
        foreach (var product in products)
        {
            await scope.SaveAsync(product);
        }

        return products;
    }

    private async Task<List<ProductView>> ReadAllAsync(ProductFilter filter, int pageSize)
    {
        await using var scope = new CatalogScope(database);
        List<ProductView> all = [];
        KeysetPosition? after = null;

        while (true)
        {
            var rows = await scope.ProductReads.ListAsync(filter, after, pageSize + 1, CancellationToken.None);
            all.AddRange(rows.Take(pageSize));
            if (rows.Count <= pageSize)
            {
                return all;
            }

            after = Position(rows[pageSize - 1], filter.Sort);
        }
    }

    private static KeysetPosition Position(ProductView row, ProductSort sort) =>
        new(sort == ProductSort.Name ? row.Name : row.CreatedAt.ToString("O", CultureInfo.InvariantCulture), row.Id);

    private async Task<int> CountAsync() =>
        await SqlProbe.ScalarAsync<int>(database.ConnectionString, "SELECT COUNT(*) FROM catalog.Products");
}
