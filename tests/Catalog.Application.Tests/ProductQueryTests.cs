using System.Globalization;
using AwesomeAssertions;
using Catalog.Application.Paging;
using Catalog.Application.Products;
using Catalog.Application.Results;
using Catalog.Application.Tests.Support;
using Catalog.Domain.Common;
using Catalog.Domain.Tests.Support;
using CsCheck;
using Xunit;

namespace Catalog.Application.Tests;

/// <summary>REQ-APP-001, REQ-APP-003, REQ-APP-004, and REQ-API-005: the queries of products, the keyset cursor, and the page size, through the production chain over the in-memory catalog.</summary>
public sealed class ProductQueryTests
{
    private static UseCaseRig RigWithProducts(out Domain.Categories.Category keyboards)
    {
        var rig = new UseCaseRig();
        keyboards = rig.SeedCategory();
        var mice = rig.SeedCategory("Mice");

        foreach (var (sku, name, category) in new[] { ("A-1", "Delta", keyboards), ("A-2", "Alpha", keyboards), ("A-3", "Charlie", mice), ("A-4", "Bravo", keyboards), ("A-5", "Echo", mice) })
        {
            rig.Clock.Advance(TimeSpan.FromMinutes(1));
            rig.SeedProduct(category, sku, name);
        }

        return rig;
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-001")]
    public async Task Reading_a_product_returns_its_view_or_not_found()
    {
        var rig = new UseCaseRig();
        var product = rig.SeedProduct(rig.SeedCategory());
        var handler = rig.Query<GetProduct, ProductView>();

        var found = await handler.HandleAsync(new GetProduct(product.Id.Value), CancellationToken.None);
        var missing = await handler.HandleAsync(new GetProduct(TestIds.Product(404).Value), CancellationToken.None);
        var invalid = await handler.HandleAsync(new GetProduct(Make.NotUuid7), CancellationToken.None);

        found.Value.Sku.Should().Be("KB-100");
        missing.Error.Should().Be(new NotFound("product"));
        invalid.Error.Should().BeOfType<ValidationFailed>().Which.Errors[0].Field.Should().Be("productId");
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-003")]
    public async Task A_product_that_was_read_is_served_from_the_cache_the_second_time()
    {
        var rig = new UseCaseRig();
        var product = rig.SeedProduct(rig.SeedCategory());
        var handler = rig.Query<GetProduct, ProductView>();

        await handler.HandleAsync(new GetProduct(product.Id.Value), CancellationToken.None);
        await handler.HandleAsync(new GetProduct(product.Id.Value), CancellationToken.None);

        rig.Journal.Entries.Count(entry => entry == "cache:store").Should().Be(1);
        rig.Journal.Entries.Should().Contain($"cache:get product:{product.Id.Value:D}");
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-003")]
    public void The_cache_keys_name_the_product_or_the_query_and_carry_the_tags_that_a_write_removes()
    {
        var id = TestIds.Product(7).Value;
        var category = TestIds.Category(3).Value;

        var product = new GetProduct(id).CacheKey;
        var list = new ListProducts(category).CacheKey;
        var all = new ListProducts().CacheKey;

        product.Name.Should().Be($"product:{id:D}");
        product.Tags.Should().Equal($"product:{id:D}");
        list.Tags.Should().Equal("products:list", $"category:{category:D}");
        all.Tags.Should().Equal("products:list");
        list.Name.Should().MatchRegex("^products:list:[0-9a-f]{32}$").And.NotBe(all.Name);
        new ListProducts(category).CacheKey.Name.Should().Be(list.Name);
        new ListProducts(category, Q: "key").CacheKey.Name.Should().NotBe(list.Name);
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-001")]
    public async Task Listing_products_newest_first_pages_through_every_product_once()
    {
        var rig = RigWithProducts(out _);
        var handler = rig.Query<ListProducts, Page<ProductView>>();
        List<string> names = [];
        string? cursor = null;

        do
        {
            var page = (await handler.HandleAsync(new ListProducts(PageSize: 2, Cursor: cursor), CancellationToken.None)).Value;
            names.AddRange(page.Items.Select(view => view.Name));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        names.Should().Equal("Echo", "Bravo", "Charlie", "Alpha", "Delta");
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-001")]
    public async Task Listing_products_by_name_pages_from_a_to_z_with_a_cursor_for_that_sort()
    {
        var rig = RigWithProducts(out _);
        var handler = rig.Query<ListProducts, Page<ProductView>>();

        var first = (await handler.HandleAsync(new ListProducts(Sort: "name", PageSize: 3), CancellationToken.None)).Value;
        var second = (await handler.HandleAsync(new ListProducts(Sort: "name", PageSize: 3, Cursor: first.NextCursor), CancellationToken.None)).Value;
        var mismatch = await handler.HandleAsync(new ListProducts(Sort: "createdAt", PageSize: 3, Cursor: first.NextCursor), CancellationToken.None);

        first.Items.Select(view => view.Name).Should().Equal("Alpha", "Bravo", "Charlie");
        second.Items.Select(view => view.Name).Should().Equal("Delta", "Echo");
        second.NextCursor.Should().BeNull();
        mismatch.Error.Should().BeOfType<ValidationFailed>().Which.Errors.Select(error => error.Field).Should().Equal("cursor");
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-001")]
    public async Task The_filters_narrow_the_list_and_reach_the_store_in_the_form_that_it_needs()
    {
        var rig = RigWithProducts(out var keyboards);
        rig.SeedProduct(keyboards, "A-6", "Foxtrot", discontinued: true);
        var handler = rig.Query<ListProducts, Page<ProductView>>();

        var byCategory = await handler.HandleAsync(new ListProducts(keyboards.Id.Value, Sort: "name"), CancellationToken.None);
        var byStatus = await handler.HandleAsync(new ListProducts(Status: LifecycleStatus.Discontinued), CancellationToken.None);
        var bySearch = await handler.HandleAsync(new ListProducts(Q: "AR"), CancellationToken.None);

        byCategory.Value.Items.Select(view => view.Name).Should().Equal("Alpha", "Bravo", "Delta", "Foxtrot");
        byStatus.Value.Items.Select(view => view.Name).Should().Equal("Foxtrot");
        bySearch.Value.Items.Select(view => view.Name).Should().Equal("Charlie");
        rig.Catalog.ProductReads.LastFilter.Should().Be(new ProductFilter(null, null, "AR", ProductSort.CreatedAt));
        rig.Catalog.ProductReads.Limit.Should().Be(PageRequest.DefaultSize + 1);
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-004")]
    public async Task Every_invalid_parameter_comes_back_at_once_named_as_in_the_contract_even_without_a_validator()
    {
        var rig = new UseCaseRig(withValidators: false);

        var result = await rig.Query<ListProducts, Page<ProductView>>().HandleAsync(
            new ListProducts(Make.NotUuid7, (LifecycleStatus)9, string.Empty, "price", 500, "no cursor"),
            CancellationToken.None);

        result.Error.Should().BeOfType<ValidationFailed>().Which.Errors.Select(error => error.Field).Should().Equal("sort", "categoryId", "status", "q", "pageSize", "cursor");
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-005")]
    public async Task The_validator_reports_the_same_invalid_parameters_before_the_handler_runs()
    {
        var rig = new UseCaseRig();

        var result = await rig.Query<ListProducts, Page<ProductView>>().HandleAsync(
            new ListProducts(Make.NotUuid7, (LifecycleStatus)9, new string('q', 101), "price", 0, "no cursor"),
            CancellationToken.None);

        result.Error.Should().BeOfType<ValidationFailed>().Which.Errors.Select(error => error.Field).Should().Equal("categoryId", "status", "q", "sort", "pageSize", "cursor");
        rig.Catalog.ProductReads.Limit.Should().Be(0);
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-001")]
    public void A_cursor_round_trips_and_names_its_sort_and_its_position()
    {
        Property.Check(
            Gen.String.AlphaNumeric.Where(text => text.Length > 0).Select(Gen.Guid, (key, id) => new KeysetPosition(key, id)),
            position =>
            {
                var cursor = PageCursor.Encode("name", position);

                PageCursor.TryDecode(cursor, "name", out var decoded).Should().BeTrue();
                decoded.Should().Be(position);
                PageCursor.TryDecode(cursor, "createdAt", out _).Should().BeFalse();
                cursor.Should().MatchRegex("^[A-Za-z0-9_-]+$");
            });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64 url!")]
    [InlineData("e30")]
    [InlineData("bm90IGpzb24")]
    [Trait("Requirement", "REQ-APP-001")]
    public void Text_that_is_not_a_cursor_is_refused(string? text)
    {
        PageCursor.TryDecode(text, "name", out var position).Should().BeFalse();
        position.Should().BeNull();
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-001")]
    public void A_cursor_of_another_version_or_an_empty_position_is_refused()
    {
        string Make(string json) => System.Buffers.Text.Base64Url.EncodeToString(System.Text.Encoding.UTF8.GetBytes(json));
        var id = TestIds.Product(1).Value.ToString("D", CultureInfo.InvariantCulture);

        PageCursor.TryDecode(Make($$"""{"V":2,"S":"name","K":"a","I":"{{id}}"}"""), "name", out _).Should().BeFalse();
        PageCursor.TryDecode(Make($$"""{"V":1,"S":"name","K":"","I":"{{id}}"}"""), "name", out _).Should().BeFalse();
        PageCursor.TryDecode(Make("""{"V":1,"S":"name","K":"a","I":"00000000-0000-0000-0000-000000000000"}"""), "name", out _).Should().BeFalse();
        PageCursor.TryDecode(Make($$"""{"V":1,"S":"name","K":"a","I":"{{id}}"}"""), "name", out _).Should().BeTrue();
        PageCursor.TryDecode(new string('a', PageCursor.MaxLength + 1), "name", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    [Trait("Requirement", "REQ-APP-001")]
    public void The_page_size_is_between_1_and_100(int size, bool valid)
    {
        PageRequest.Parse(size, null, "name").IsSuccess.Should().Be(valid);
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-001")]
    public void An_encoding_without_a_position_is_a_programming_error()
    {
        var act = () => PageCursor.Encode("name", null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
