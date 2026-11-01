using System.Buffers.Text;
using System.Globalization;
using System.Text;
using AwesomeAssertions;
using Catalog.Application.Categories;
using Catalog.Application.Paging;
using Catalog.Application.Persistence;
using Catalog.Application.Products;
using Catalog.Application.Results;
using Catalog.Application.Tests.Support;
using Catalog.Domain.Common;
using Catalog.Domain.Tests.Support;
using Xunit;

namespace Catalog.Application.Tests;

/// <summary>
/// REQ-APP-001, REQ-APP-004, REQ-QUA-006: the exact edges of the page, the cursor, the tag, and the search text, the messages of the refusals, and the handlers when no validator runs. These tests came from the
/// first mutation run of the application layer, which showed that each of these edges could move one step without a test failing (docs/testing/evidence/2026-11-01-application-mutation-baseline.md).
/// </summary>
public sealed class BoundaryTests
{
    [Fact]
    [Trait("Requirement", "REQ-APP-001")]
    public async Task A_page_that_holds_exactly_the_page_size_has_no_next_cursor()
    {
        var rig = new UseCaseRig();
        foreach (var name in new[] { "A", "B", "C", "D" })
        {
            rig.SeedCategory(name);
        }

        var handler = rig.Query<ListCategories, Page<CategoryView>>();

        var first = (await handler.HandleAsync(new ListCategories(2), CancellationToken.None)).Value;
        var second = (await handler.HandleAsync(new ListCategories(2, first.NextCursor), CancellationToken.None)).Value;

        first.NextCursor.Should().NotBeNull();
        second.Items.Select(view => view.Name).Should().Equal("C", "D");
        second.NextCursor.Should().BeNull();
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-001")]
    public void A_cursor_of_exactly_512_characters_is_accepted_and_one_that_is_longer_is_not()
    {
        var id = TestIds.Product(1).Value.ToString("D", CultureInfo.InvariantCulture);
        const string Prefix = "{\"V\":1,\"S\":\"name\",\"K\":\"";
        var suffix = "\",\"I\":\"" + id + "\"}";
        var json = Prefix + new string('k', 384 - Prefix.Length - suffix.Length) + suffix;
        var exact = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(json));

        exact.Should().HaveLength(PageCursor.MaxLength);
        PageCursor.TryDecode(exact, "name", out var position).Should().BeTrue();
        position!.Id.Should().Be(TestIds.Product(1).Value);
        PageCursor.TryDecode(exact + "AAAA", "name", out _).Should().BeFalse();
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-001")]
    public async Task The_cursor_of_a_page_by_creation_time_names_the_last_row_with_a_round_trip_time()
    {
        var rig = new UseCaseRig();
        var category = rig.SeedCategory();
        foreach (var sku in new[] { "A-1", "A-2", "A-3" })
        {
            rig.Clock.Advance(TimeSpan.FromMinutes(1));
            rig.SeedProduct(category, sku, sku);
        }

        var page = (await rig.Query<ListProducts, Page<ProductView>>().HandleAsync(new ListProducts(PageSize: 2), CancellationToken.None)).Value;

        PageCursor.TryDecode(page.NextCursor, "createdAt", out var position).Should().BeTrue();
        position!.SortKey.Should().Be(page.Items[^1].CreatedAt.ToString("O", CultureInfo.InvariantCulture)).And.Be("2026-11-01T12:02:00.0000000+00:00");
        position.Id.Should().Be(page.Items[^1].Id);
    }

    [Fact]
    [Trait("Requirement", "REQ-API-003")]
    public void A_tag_of_64_characters_is_accepted_and_one_of_65_is_not()
    {
        EntityTag.TryCreate(new string('a', 64), out _).Should().BeTrue();
        EntityTag.TryCreate(new string('a', 65), out _).Should().BeFalse();
    }

    [Fact]
    [Trait("Requirement", "REQ-API-003")]
    public void An_empty_row_version_is_refused_with_a_message_that_says_why()
    {
        var empty = () => EntityTag.FromBytes([]);

        empty.Should().Throw<ArgumentException>().WithMessage("*at least one byte*");
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    [Trait("Requirement", "REQ-APP-004")]
    public async Task A_search_text_of_1_to_100_characters_is_accepted(int length, bool valid)
    {
        var rig = new UseCaseRig(withValidators: false);

        var result = await rig.Query<ListProducts, Page<ProductView>>().HandleAsync(new ListProducts(Q: new string('q', length)), CancellationToken.None);

        result.IsSuccess.Should().Be(valid);
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-004")]
    public async Task The_messages_of_a_refused_list_say_what_to_send()
    {
        var rig = new UseCaseRig(withValidators: false);

        var result = await rig.Query<ListProducts, Page<ProductView>>().HandleAsync(
            new ListProducts(Make.NotUuid7, (LifecycleStatus)9, string.Empty, "price", 500, "x"),
            CancellationToken.None);

        result.Error.Should().BeOfType<ValidationFailed>().Which.Errors.Should().Equal(
            new FieldError("sort", "The sort must be createdAt or name."),
            new FieldError("categoryId", "The category identifier must be a UUID version 7."),
            new FieldError("status", "The status must be active or discontinued."),
            new FieldError("q", "The search text must have 1 to 100 characters."),
            new FieldError("pageSize", "The page size must be between 1 and 100."),
            new FieldError("cursor", "The cursor is not valid for this request. Start again without a cursor."));
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-004")]
    public async Task A_query_for_a_bad_identifier_is_refused_by_the_handler_when_no_validator_runs()
    {
        var rig = new UseCaseRig(withValidators: false);

        var product = await rig.Query<GetProduct, ProductView>().HandleAsync(new GetProduct(Make.NotUuid7), CancellationToken.None);
        var category = await rig.Query<GetCategory, CategoryView>().HandleAsync(new GetCategory(Make.NotUuid7), CancellationToken.None);

        product.Error.Should().BeOfType<ValidationFailed>().Which.Errors[0].Field.Should().Be("productId");
        category.Error.Should().BeOfType<ValidationFailed>().Which.Errors[0].Field.Should().Be("categoryId");
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-003")]
    public void Each_parameter_of_a_list_changes_its_cache_key()
    {
        var category = TestIds.Category(1).Value;
        var baseKey = new ListProducts().CacheKey.Name;

        new ListProducts(category).CacheKey.Name.Should().NotBe(baseKey);
        new ListProducts(Status: LifecycleStatus.Active).CacheKey.Name.Should().NotBe(baseKey);
        new ListProducts(Status: LifecycleStatus.Discontinued).CacheKey.Name.Should().NotBe(new ListProducts(Status: LifecycleStatus.Active).CacheKey.Name);
        new ListProducts(Q: "a").CacheKey.Name.Should().NotBe(baseKey);
        new ListProducts(Sort: "name").CacheKey.Name.Should().NotBe(baseKey);
        new ListProducts(PageSize: 21).CacheKey.Name.Should().NotBe(baseKey);
        new ListProducts(Cursor: "abc").CacheKey.Name.Should().NotBe(baseKey);
        new ListProducts().CacheKey.Name.Should().Be(baseKey);
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-001")]
    public void A_view_needs_the_aggregate_and_its_version()
    {
        var rig = new UseCaseRig();
        var category = rig.SeedCategory();
        var product = rig.SeedProduct(category);
        var tag = rig.TagOf(product);

        var categoryWithoutTag = () => CategoryView.From(category, null!);
        var categoryWithout = () => CategoryView.From(null!, tag);
        var productWithoutTag = () => ProductView.From(product, null!);
        var productWithout = () => ProductView.From(null!, tag);

        categoryWithoutTag.Should().Throw<ArgumentNullException>();
        categoryWithout.Should().Throw<ArgumentNullException>();
        productWithoutTag.Should().Throw<ArgumentNullException>();
        productWithout.Should().Throw<ArgumentNullException>();
    }
}
