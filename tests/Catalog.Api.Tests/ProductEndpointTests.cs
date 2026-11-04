using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Catalog.Api.Tests.Support;
using Xunit;

namespace Catalog.Api.Tests;

/// <summary>
/// The operations on products over HTTP, against a real SQL Server and a real Valkey (REQ-API-001, REQ-API-003, REQ-API-004, and REQ-API-005): the create with its price and SKU rules, the cached read and its
/// <c>ETag</c>, the conditional change, the discontinue, the filters, and the pages. Each test starts from an empty catalog.
/// </summary>
public sealed class ProductEndpointTests(CatalogApiFixture api) : IClassFixture<CatalogApiFixture>
{
    [Fact]
    [Trait("Requirement", "REQ-API-001")]
    [Trait("Requirement", "REQ-API-003")]
    public async Task Creating_a_product_answers_201_with_the_location_the_etag_and_the_price_in_its_currency()
    {
        await api.ResetAsync();
        var category = await api.CreateCategoryAsync("Keyboards");

        var created = await api.SendAsync(HttpMethod.Post, "/api/v1/products", ApiCalls.Editor, CatalogApiFixture.ProductJson(category.Id, "kb-100", "  Mechanical keyboard  ", "89.9", "USD"), ApiCalls.WithKey(ApiCalls.NewKey()));

        created.Status.Should().Be(HttpStatusCode.Created, created.Body);
        created.ContentType.Should().Be("application/json");
        created.Location.Should().Be($"http://localhost/api/v1/products/{created.Id}");
        created.ETag.Should().NotBeNullOrEmpty();
        created.Header("api-supported-versions").Should().Be("1.0");
        created.Json.GetProperty("sku").GetString().Should().Be("KB-100");
        created.Json.GetProperty("name").GetString().Should().Be("Mechanical keyboard");
        created.Json.GetProperty("description").ValueKind.Should().Be(JsonValueKind.Null);
        created.Json.GetProperty("price").GetProperty("amount").GetString().Should().Be("89.90");
        created.Json.GetProperty("price").GetProperty("currency").GetString().Should().Be("USD");
        created.Json.GetProperty("categoryId").GetGuid().Should().Be(category.Id);
        created.Json.GetProperty("status").GetString().Should().Be("active");

        var read = await api.SendAsync(HttpMethod.Get, created.Location!, ApiCalls.Reader);

        read.Status.Should().Be(HttpStatusCode.OK);
        read.ETag.Should().Be(created.ETag);
    }

    [Theory]
    [InlineData("1990", "JPY", "1990")]
    [InlineData("5", "USD", "5.00")]
    [InlineData("0", "EUR", "0.00")]
    [InlineData("12.5", "GBP", "12.50")]
    [Trait("Requirement", "REQ-API-001")]
    public async Task A_price_is_written_with_the_decimal_places_of_its_currency(string sent, string currency, string expected)
    {
        await api.ResetAsync();
        var category = await api.CreateCategoryAsync("Keyboards");

        var created = await api.CreateProductAsync(category.Id, "PRICE-1", amount: sent, currency: currency);
        var read = await api.SendAsync(HttpMethod.Get, created.Location!, ApiCalls.Reader);

        created.Json.GetProperty("price").GetProperty("amount").GetString().Should().Be(expected);
        read.Json.GetProperty("price").GetProperty("amount").GetString().Should().Be(expected);
    }

    [Fact]
    [Trait("Requirement", "REQ-API-004")]
    [Trait("Requirement", "REQ-DATA-004")]
    public async Task The_same_key_and_the_same_body_replay_the_first_product_and_another_body_is_422()
    {
        await api.ResetAsync();
        var category = await api.CreateCategoryAsync("Keyboards");
        var key = ApiCalls.NewKey();
        var body = CatalogApiFixture.ProductJson(category.Id, "KB-100", "Keyboard", "89.99", "USD");

        var first = await api.SendAsync(HttpMethod.Post, "/api/v1/products", ApiCalls.Editor, body, ApiCalls.WithKey(key));
        var replay = await api.SendAsync(HttpMethod.Post, "/api/v1/products", ApiCalls.Editor, body, ApiCalls.WithKey(key));
        var reused = await api.SendAsync(HttpMethod.Post, "/api/v1/products", ApiCalls.Editor, CatalogApiFixture.ProductJson(category.Id, "KB-200", "Keyboard", "89.99", "USD"), ApiCalls.WithKey(key));
        var list = await api.SendAsync(HttpMethod.Get, "/api/v1/products", ApiCalls.Reader);

        first.Status.Should().Be(HttpStatusCode.Created);
        replay.Status.Should().Be(HttpStatusCode.Created);
        replay.Id.Should().Be(first.Id);
        replay.ETag.Should().Be(first.ETag);
        reused.Status.Should().Be(HttpStatusCode.UnprocessableEntity);
        reused.Slug.Should().Be("idempotency-key-reused");
        list.Json.GetProperty("items").GetArrayLength().Should().Be(1);
    }

    [Fact]
    [Trait("Requirement", "REQ-API-001")]
    [Trait("Requirement", "REQ-APP-004")]
    public async Task A_sku_that_is_in_use_is_409_whatever_its_case()
    {
        await api.ResetAsync();
        var category = await api.CreateCategoryAsync("Keyboards");
        await api.CreateProductAsync(category.Id, "KB-100");

        var duplicate = await api.SendAsync(HttpMethod.Post, "/api/v1/products", ApiCalls.Editor, CatalogApiFixture.ProductJson(category.Id, "kb-100", "Other", "10", "USD"), ApiCalls.WithKey(ApiCalls.NewKey()));

        duplicate.Status.Should().Be(HttpStatusCode.Conflict);
        duplicate.Slug.Should().Be("sku-already-exists");
    }

    [Fact]
    [Trait("Requirement", "REQ-API-005")]
    [Trait("Requirement", "REQ-APP-005")]
    public async Task A_category_that_does_not_exist_or_is_discontinued_is_a_validation_error_for_category_id()
    {
        await api.ResetAsync();
        var category = await api.CreateCategoryAsync("Keyboards");
        await api.SendAsync(HttpMethod.Post, $"/api/v1/categories/{category.Id}/discontinue", ApiCalls.Admin, null, ApiCalls.WithIfMatch(ApiCalls.Quote(category.ETag!)));

        var missing = await api.SendAsync(HttpMethod.Post, "/api/v1/products", ApiCalls.Editor, CatalogApiFixture.ProductJson(Guid.CreateVersion7(), "KB-1", "Keyboard", "10", "USD"), ApiCalls.WithKey(ApiCalls.NewKey()));
        var discontinued = await api.SendAsync(HttpMethod.Post, "/api/v1/products", ApiCalls.Editor, CatalogApiFixture.ProductJson(category.Id, "KB-2", "Keyboard", "10", "USD"), ApiCalls.WithKey(ApiCalls.NewKey()));

        missing.Status.Should().Be(HttpStatusCode.BadRequest);
        missing.Slug.Should().Be("validation-failed");
        missing.Json.GetProperty("errors").TryGetProperty("categoryId", out _).Should().BeTrue();
        discontinued.Status.Should().Be(HttpStatusCode.BadRequest);
        discontinued.Json.GetProperty("errors").TryGetProperty("categoryId", out _).Should().BeTrue();
    }

    [Theory]
    [InlineData("abc", "USD", "price.amount")]
    [InlineData("-1", "USD", "price.amount")]
    [InlineData("1e3", "USD", "price.amount")]
    [InlineData("01", "USD", "price.amount")]
    [InlineData("1,5", "USD", "price.amount")]
    [InlineData("1234567890123", "USD", "price.amount")]
    [InlineData("1.234", "USD", "price.amount")]
    [InlineData("1.5", "JPY", "price.amount")]
    [InlineData("10", "ABC", "price.currency")]
    [InlineData("10", "usd", "price.currency")]
    [Trait("Requirement", "REQ-API-005")]
    public async Task An_amount_or_a_currency_that_is_not_valid_is_a_validation_error_for_its_field(string amount, string currency, string field)
    {
        await api.ResetAsync();
        var category = await api.CreateCategoryAsync("Keyboards");

        var response = await api.SendAsync(HttpMethod.Post, "/api/v1/products", ApiCalls.Editor, CatalogApiFixture.ProductJson(category.Id, "KB-1", "Keyboard", amount, currency), ApiCalls.WithKey(ApiCalls.NewKey()));

        response.Status.Should().Be(HttpStatusCode.BadRequest);
        response.Slug.Should().Be("validation-failed");
        response.Json.GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue(response.Body);
    }

    [Fact]
    [Trait("Requirement", "REQ-API-005")]
    public async Task A_missing_price_a_missing_sku_and_a_missing_name_are_validation_errors()
    {
        await api.ResetAsync();
        var category = await api.CreateCategoryAsync("Keyboards");

        var noPrice = await api.SendAsync(HttpMethod.Post, "/api/v1/products", ApiCalls.Editor, $$"""{"sku":"KB-1","name":"Keyboard","categoryId":"{{category.Id}}"}""", ApiCalls.WithKey(ApiCalls.NewKey()));
        var noSku = await api.SendAsync(HttpMethod.Post, "/api/v1/products", ApiCalls.Editor, CatalogApiFixture.ProductJson(category.Id, null, "Keyboard", "10", "USD"), ApiCalls.WithKey(ApiCalls.NewKey()));
        var noName = await api.SendAsync(HttpMethod.Post, "/api/v1/products", ApiCalls.Editor, $$"""{"sku":"KB-1","price":{"amount":"10","currency":"USD"},"categoryId":"{{category.Id}}"}""", ApiCalls.WithKey(ApiCalls.NewKey()));

        noPrice.Json.GetProperty("errors").TryGetProperty("price", out _).Should().BeTrue(noPrice.Body);
        noSku.Json.GetProperty("errors").TryGetProperty("sku", out _).Should().BeTrue(noSku.Body);
        noName.Json.GetProperty("errors").TryGetProperty("name", out _).Should().BeTrue(noName.Body);
    }

    [Fact]
    [Trait("Requirement", "REQ-API-003")]
    public async Task A_cached_read_keeps_its_etag_and_a_304_needs_no_body()
    {
        await api.ResetAsync();
        var category = await api.CreateCategoryAsync("Keyboards");
        var created = await api.CreateProductAsync(category.Id, "KB-100");
        var path = $"/api/v1/products/{created.Id}";

        var first = await api.SendAsync(HttpMethod.Get, path, ApiCalls.Reader);
        var second = await api.SendAsync(HttpMethod.Get, path, ApiCalls.Reader);
        var notModified = await api.SendAsync(HttpMethod.Get, path, ApiCalls.Reader, null, ApiCalls.WithIfNoneMatch(ApiCalls.Quote(first.ETag!)));

        second.Body.Should().Be(first.Body);
        second.ETag.Should().Be(first.ETag);
        notModified.Status.Should().Be(HttpStatusCode.NotModified);
        notModified.Body.Should().BeEmpty();
        notModified.ETag.Should().Be(first.ETag);
    }

    [Fact]
    [Trait("Requirement", "REQ-API-003")]
    public async Task A_change_with_the_current_etag_shows_at_once_in_the_next_read_and_the_old_etag_is_stale()
    {
        await api.ResetAsync();
        var category = await api.CreateCategoryAsync("Keyboards");
        var created = await api.CreateProductAsync(category.Id, "KB-100", description: "Hot-swappable");
        var path = $"/api/v1/products/{created.Id}";
        await api.SendAsync(HttpMethod.Get, path, ApiCalls.Reader);

        var changed = await api.SendAsync(HttpMethod.Put, path, ApiCalls.Editor, UpdateJson(category.Id, "Keyboard 75", "79.99", "USD"), ApiCalls.WithIfMatch(ApiCalls.Quote(created.ETag!)));
        var read = await api.SendAsync(HttpMethod.Get, path, ApiCalls.Reader);
        var stale = await api.SendAsync(HttpMethod.Put, path, ApiCalls.Editor, UpdateJson(category.Id, "Keyboard 80", "99.99", "USD"), ApiCalls.WithIfMatch(ApiCalls.Quote(created.ETag!)));

        changed.Status.Should().Be(HttpStatusCode.OK, changed.Body);
        changed.ETag.Should().NotBe(created.ETag);
        changed.Json.GetProperty("name").GetString().Should().Be("Keyboard 75");
        changed.Json.GetProperty("description").ValueKind.Should().Be(JsonValueKind.Null);
        changed.Json.GetProperty("sku").GetString().Should().Be("KB-100");
        read.ETag.Should().Be(changed.ETag);
        read.Json.GetProperty("price").GetProperty("amount").GetString().Should().Be("79.99");
        stale.Status.Should().Be(HttpStatusCode.PreconditionFailed);
        stale.Slug.Should().Be("precondition-failed");
    }

    [Fact]
    [Trait("Requirement", "REQ-API-003")]
    public async Task A_change_without_if_match_is_428_and_with_an_unknown_product_is_404()
    {
        await api.ResetAsync();
        var category = await api.CreateCategoryAsync("Keyboards");
        var created = await api.CreateProductAsync(category.Id, "KB-100");

        var missing = await api.SendAsync(HttpMethod.Put, $"/api/v1/products/{created.Id}", ApiCalls.Editor, UpdateJson(category.Id, "Keyboard", "10", "USD"));
        var unknown = await api.SendAsync(HttpMethod.Put, $"/api/v1/products/{Guid.CreateVersion7()}", ApiCalls.Editor, UpdateJson(category.Id, "Keyboard", "10", "USD"), ApiCalls.WithIfMatch(ApiCalls.Quote(created.ETag!)));
        var invalid = await api.SendAsync(HttpMethod.Put, "/api/v1/products/nope", ApiCalls.Editor, UpdateJson(category.Id, "Keyboard", "10", "USD"), ApiCalls.WithIfMatch(ApiCalls.Quote(created.ETag!)));

        missing.Status.Should().Be(HttpStatusCode.PreconditionRequired);
        missing.Slug.Should().Be("precondition-required");
        unknown.Status.Should().Be(HttpStatusCode.NotFound);
        unknown.Slug.Should().Be("not-found");
        invalid.Status.Should().Be(HttpStatusCode.BadRequest);
        invalid.Json.GetProperty("errors").TryGetProperty("productId", out _).Should().BeTrue();
    }

    [Fact]
    [Trait("Requirement", "REQ-API-003")]
    [Trait("Requirement", "REQ-DOM-003")]
    public async Task Discontinuing_a_product_is_idempotent_and_after_it_a_price_or_category_change_is_409()
    {
        await api.ResetAsync();
        var category = await api.CreateCategoryAsync("Keyboards");
        var created = await api.CreateProductAsync(category.Id, "KB-100");
        var path = $"/api/v1/products/{created.Id}";

        var first = await api.SendAsync(HttpMethod.Post, $"{path}/discontinue", ApiCalls.Admin, null, ApiCalls.WithIfMatch(ApiCalls.Quote(created.ETag!)));
        var second = await api.SendAsync(HttpMethod.Post, $"{path}/discontinue", ApiCalls.Admin, null, ApiCalls.WithIfMatch(ApiCalls.Quote(first.ETag!)));
        var stale = await api.SendAsync(HttpMethod.Post, $"{path}/discontinue", ApiCalls.Admin, null, ApiCalls.WithIfMatch(ApiCalls.Quote(created.ETag!)));
        var missing = await api.SendAsync(HttpMethod.Post, $"{path}/discontinue", ApiCalls.Admin);
        var repriced = await api.SendAsync(HttpMethod.Put, path, ApiCalls.Editor, UpdateJson(category.Id, "Keyboard", "1", "USD"), ApiCalls.WithIfMatch(ApiCalls.Quote(first.ETag!)));

        first.Status.Should().Be(HttpStatusCode.OK);
        first.Json.GetProperty("status").GetString().Should().Be("discontinued");
        second.Status.Should().Be(HttpStatusCode.OK);
        second.ETag.Should().Be(first.ETag);
        stale.Status.Should().Be(HttpStatusCode.PreconditionFailed);
        missing.Status.Should().Be(HttpStatusCode.PreconditionRequired);
        repriced.Status.Should().Be(HttpStatusCode.Conflict);
        repriced.Slug.Should().Be("product-discontinued");
    }

    [Fact]
    [Trait("Requirement", "REQ-API-001")]
    public async Task Products_are_filtered_by_category_status_and_text_and_sorted_by_creation_or_name()
    {
        await api.ResetAsync();
        var keyboards = await api.CreateCategoryAsync("Keyboards");
        var mice = await api.CreateCategoryAsync("Mice");
        await api.CreateProductAsync(keyboards.Id, "KB-1", "Zebra keyboard");
        await api.CreateProductAsync(keyboards.Id, "KB-2", "Alpha keyboard");
        var mouse = await api.CreateProductAsync(mice.Id, "MS-1", "Beta mouse");
        await api.SendAsync(HttpMethod.Post, $"/api/v1/products/{mouse.Id}/discontinue", ApiCalls.Admin, null, ApiCalls.WithIfMatch(ApiCalls.Quote(mouse.ETag!)));

        var byName = await api.SendAsync(HttpMethod.Get, "/api/v1/products?sort=name", ApiCalls.Reader);
        var newest = await api.SendAsync(HttpMethod.Get, "/api/v1/products", ApiCalls.Reader);
        var inKeyboards = await api.SendAsync(HttpMethod.Get, $"/api/v1/products?categoryId={keyboards.Id}&sort=name", ApiCalls.Reader);
        var discontinued = await api.SendAsync(HttpMethod.Get, "/api/v1/products?status=discontinued", ApiCalls.Reader);
        var search = await api.SendAsync(HttpMethod.Get, "/api/v1/products?q=ALPHA", ApiCalls.Reader);
        var bySku = await api.SendAsync(HttpMethod.Get, "/api/v1/products?q=ms-1", ApiCalls.Reader);

        Skus(byName).Should().Equal("KB-2", "MS-1", "KB-1");
        Skus(newest).Should().Equal("MS-1", "KB-2", "KB-1");
        Skus(inKeyboards).Should().Equal("KB-2", "KB-1");
        Skus(discontinued).Should().Equal("MS-1");
        Skus(search).Should().Equal("KB-2");
        Skus(bySku).Should().Equal("MS-1");
    }

    [Fact]
    [Trait("Requirement", "REQ-API-001")]
    public async Task Products_come_in_pages_and_the_link_keeps_every_filter_and_the_sort()
    {
        await api.ResetAsync();
        var category = await api.CreateCategoryAsync("Keyboards");
        foreach (var name in new[] { "Delta", "Alpha", "Charlie", "Bravo", "Echo" })
        {
            await api.CreateProductAsync(category.Id, $"SKU-{name.ToUpperInvariant()}", $"{name} keyboard");
        }

        List<string> names = [];
        var next = $"/api/v1/products?categoryId={category.Id}&status=active&sort=name&pageSize=2";
        var pages = 0;

        while (next is not null)
        {
            var page = await api.SendAsync(HttpMethod.Get, next, ApiCalls.Reader);
            page.Status.Should().Be(HttpStatusCode.OK, page.Body);
            names.AddRange(page.Json.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString()!));

            var cursor = page.Json.GetProperty("nextCursor");
            var link = page.Header("Link");

            if (cursor.ValueKind == JsonValueKind.Null)
            {
                link.Should().BeNull();
                next = null;
            }
            else
            {
                link.Should().Be($"<http://localhost/api/v1/products?categoryId={category.Id}&status=active&sort=name&pageSize=2&cursor={cursor.GetString()}>; rel=\"next\"");
                next = link![1..link.IndexOf('>', StringComparison.Ordinal)];
            }

            pages++;
        }

        pages.Should().Be(3);
        names.Should().Equal("Alpha keyboard", "Bravo keyboard", "Charlie keyboard", "Delta keyboard", "Echo keyboard");
    }

    [Theory]
    [InlineData("sort=price", "sort")]
    [InlineData("pageSize=0", "pageSize")]
    [InlineData("pageSize=101", "pageSize")]
    [InlineData("cursor=abc", "cursor")]
    [InlineData("categoryId=not-a-uuid", "categoryId")]
    [InlineData("status=gone", "status")]
    [InlineData("q=", "q")]
    [InlineData("sort=name&sort=createdAt", "sort")]
    [Trait("Requirement", "REQ-API-005")]
    public async Task A_filter_or_a_page_parameter_that_is_not_valid_is_a_validation_error_that_names_it(string query, string field)
    {
        var response = await api.SendAsync(HttpMethod.Get, $"/api/v1/products?{query}", ApiCalls.Reader);

        response.Status.Should().Be(HttpStatusCode.BadRequest);
        response.Slug.Should().Be("validation-failed");
        response.Json.GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue(response.Body);
    }

    [Fact]
    [Trait("Requirement", "REQ-API-001")]
    public async Task A_cursor_of_one_sort_is_refused_for_another()
    {
        await api.ResetAsync();
        var category = await api.CreateCategoryAsync("Keyboards");
        await api.CreateProductAsync(category.Id, "KB-1", "Alpha");
        await api.CreateProductAsync(category.Id, "KB-2", "Beta");
        var page = await api.SendAsync(HttpMethod.Get, "/api/v1/products?pageSize=1", ApiCalls.Reader);
        var cursor = page.Json.GetProperty("nextCursor").GetString();

        var wrongSort = await api.SendAsync(HttpMethod.Get, $"/api/v1/products?pageSize=1&sort=name&cursor={cursor}", ApiCalls.Reader);

        wrongSort.Status.Should().Be(HttpStatusCode.BadRequest);
        wrongSort.Json.GetProperty("errors").TryGetProperty("cursor", out _).Should().BeTrue();
    }

    private static string UpdateJson(Guid categoryId, string name, string amount, string currency) =>
        $$"""{"name":"{{name}}","price":{"amount":"{{amount}}","currency":"{{currency}}"},"categoryId":"{{categoryId}}"}""";

    private static IEnumerable<string> Skus(ApiResponse page) => page.Json.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("sku").GetString()!);
}
