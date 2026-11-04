using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Catalog.Api.Tests.Support;
using Catalog.Application.Categories;
using Catalog.Application.Idempotency;
using Xunit;

namespace Catalog.Api.Tests;

/// <summary>
/// The operations on categories over HTTP, against a real SQL Server and a real Valkey (REQ-API-001, REQ-API-003, and REQ-API-004): the answers, the headers that the contract names, the conditional requests, the
/// idempotent create, and the pages. Each test starts from an empty catalog.
/// </summary>
public sealed partial class CategoryEndpointTests(CatalogApiFixture api) : IClassFixture<CatalogApiFixture>
{
    private const string CallerOfTheTokens = "11111111-2222-3333-4444-555555555555";

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex ETagToken();

    [Fact]
    [Trait("Requirement", "REQ-API-001")]
    [Trait("Requirement", "REQ-API-003")]
    public async Task Creating_a_category_answers_201_with_the_location_the_etag_and_the_body()
    {
        await api.ResetAsync();

        var created = await api.SendAsync(HttpMethod.Post, "/api/v1/categories", ApiCalls.Admin, """{"name":"  Electronics  "}""", ApiCalls.WithKey(ApiCalls.NewKey()));

        created.Status.Should().Be(HttpStatusCode.Created, created.Body);
        created.ContentType.Should().Be("application/json");
        created.ETag.Should().MatchRegex(ETagToken());
        created.Location.Should().Be($"http://localhost/api/v1/categories/{created.Id}");
        created.Header("api-supported-versions").Should().Be("1.0");
        created.Json.GetProperty("name").GetString().Should().Be("Electronics");
        created.Json.GetProperty("status").GetString().Should().Be("active");
        created.Json.GetProperty("createdAt").GetString().Should().EndWith("Z");
        created.Json.GetProperty("updatedAt").GetString().Should().EndWith("Z");

        var read = await api.SendAsync(HttpMethod.Get, created.Location!, ApiCalls.Reader);

        read.Status.Should().Be(HttpStatusCode.OK);
        read.ETag.Should().Be(created.ETag);
        read.Json.GetProperty("id").GetGuid().Should().Be(created.Id);
    }

    [Fact]
    [Trait("Requirement", "REQ-API-004")]
    [Trait("Requirement", "REQ-DATA-004")]
    public async Task The_same_key_and_the_same_body_replay_the_first_answer_and_create_one_category()
    {
        await api.ResetAsync();
        var key = ApiCalls.NewKey();

        var first = await api.SendAsync(HttpMethod.Post, "/api/v1/categories", ApiCalls.Admin, """{"name":"Garden"}""", ApiCalls.WithKey(key));
        var second = await api.SendAsync(HttpMethod.Post, "/api/v1/categories", ApiCalls.Admin, """{"name":"Garden"}""", ApiCalls.WithKey(key));
        var list = await api.SendAsync(HttpMethod.Get, "/api/v1/categories", ApiCalls.Reader);

        first.Status.Should().Be(HttpStatusCode.Created);
        second.Status.Should().Be(HttpStatusCode.Created);
        second.Id.Should().Be(first.Id);
        second.ETag.Should().Be(first.ETag);
        second.Location.Should().Be(first.Location);
        second.Json.GetProperty("createdAt").GetString().Should().Be(first.Json.GetProperty("createdAt").GetString());
        list.Json.GetProperty("items").GetArrayLength().Should().Be(1);
    }

    [Fact]
    [Trait("Requirement", "REQ-API-004")]
    public async Task The_same_key_with_another_body_is_422_and_creates_nothing()
    {
        await api.ResetAsync();
        var key = ApiCalls.NewKey();
        await api.SendAsync(HttpMethod.Post, "/api/v1/categories", ApiCalls.Admin, """{"name":"Garden"}""", ApiCalls.WithKey(key));

        var reused = await api.SendAsync(HttpMethod.Post, "/api/v1/categories", ApiCalls.Admin, """{"name":"Toys"}""", ApiCalls.WithKey(key));
        var list = await api.SendAsync(HttpMethod.Get, "/api/v1/categories", ApiCalls.Reader);

        reused.Status.Should().Be(HttpStatusCode.UnprocessableEntity);
        reused.ContentType.Should().Be("application/problem+json");
        reused.Slug.Should().Be("idempotency-key-reused");
        list.Json.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString()).Should().Equal("Garden");
    }

    [Fact]
    [Trait("Requirement", "REQ-API-004")]
    [Trait("Requirement", "REQ-DATA-004")]
    public async Task The_same_key_while_the_first_request_still_runs_is_409()
    {
        await api.ResetAsync();
        var text = $"busy-{Guid.NewGuid():N}";
        IdempotencyKey.TryCreate(text, out var key).Should().BeTrue();

        // The first request is still running when the store holds its reservation and has no result. The fingerprint is the hash of the command that the pipeline would make from this body.
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new CreateCategory("Busy", key))));
        await using (var scope = api.Host.Services.CreateAsyncScope())
        {
            var decision = await scope.ServiceProvider.GetRequiredService<IIdempotencyStore>().BeginAsync(new IdempotencyRequest(key!, CallerOfTheTokens, fingerprint), TestContext.Current.CancellationToken);
            decision.Outcome.Should().Be(IdempotencyOutcome.Reserved);
        }

        var running = await api.SendAsync(HttpMethod.Post, "/api/v1/categories", ApiCalls.Admin, """{"name":"Busy"}""", ApiCalls.WithKey(ApiCalls.Quote(text)));

        running.Status.Should().Be(HttpStatusCode.Conflict);
        running.Slug.Should().Be("idempotency-request-in-progress");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("\"\"")]
    [InlineData("\"has \\\" a quote\"")]
    [InlineData("\"a\\\\b\"")]
    [Trait("Requirement", "REQ-API-004")]
    public async Task A_missing_or_malformed_key_is_400_idempotency_key_required(string? header)
    {
        var response = await api.SendAsync(HttpMethod.Post, "/api/v1/categories", ApiCalls.Admin, """{"name":"Garden"}""", header is null ? null : ApiCalls.WithKey(header));

        response.Status.Should().Be(HttpStatusCode.BadRequest);
        response.Slug.Should().Be("idempotency-key-required");
    }

    [Fact]
    [Trait("Requirement", "REQ-API-004")]
    public async Task A_key_of_129_characters_or_a_second_key_header_is_refused()
    {
        var tooLong = await api.SendAsync(HttpMethod.Post, "/api/v1/categories", ApiCalls.Admin, """{"name":"Garden"}""", ApiCalls.WithKey(ApiCalls.Quote(new string('k', 129))));
        var twice = await api.SendAsync(HttpMethod.Post, "/api/v1/categories", ApiCalls.Admin, """{"name":"Garden"}""", ApiCalls.WithBoth(ApiCalls.WithKey(ApiCalls.NewKey()), ApiCalls.WithKey(ApiCalls.NewKey())));

        tooLong.Slug.Should().Be("idempotency-key-required");
        twice.Slug.Should().Be("idempotency-key-required");
    }

    [Fact]
    [Trait("Requirement", "REQ-API-005")]
    [Trait("Requirement", "REQ-APP-005")]
    public async Task A_missing_or_too_long_name_is_a_validation_error_for_the_field()
    {
        await api.ResetAsync();

        var missing = await api.SendAsync(HttpMethod.Post, "/api/v1/categories", ApiCalls.Admin, "{}", ApiCalls.WithKey(ApiCalls.NewKey()));
        var tooLong = await api.SendAsync(HttpMethod.Post, "/api/v1/categories", ApiCalls.Admin, $$"""{"name":"{{new string('n', 101)}}"}""", ApiCalls.WithKey(ApiCalls.NewKey()));

        missing.Status.Should().Be(HttpStatusCode.BadRequest);
        missing.Slug.Should().Be("validation-failed");
        missing.Json.GetProperty("errors").TryGetProperty("name", out _).Should().BeTrue();
        tooLong.Slug.Should().Be("validation-failed");
        tooLong.Json.GetProperty("errors").TryGetProperty("name", out _).Should().BeTrue();
    }

    [Fact]
    [Trait("Requirement", "REQ-API-003")]
    public async Task A_get_with_the_current_etag_is_304_with_no_body_and_a_stale_one_gets_the_category()
    {
        await api.ResetAsync();
        var created = await api.CreateCategoryAsync("Garden");

        var unchanged = await api.SendAsync(HttpMethod.Get, $"/api/v1/categories/{created.Id}", ApiCalls.Reader, null, ApiCalls.WithIfNoneMatch(ApiCalls.Quote(created.ETag!)));
        var stale = await api.SendAsync(HttpMethod.Get, $"/api/v1/categories/{created.Id}", ApiCalls.Reader, null, ApiCalls.WithIfNoneMatch(ApiCalls.Quote("AAAAAAAAAAA")));

        unchanged.Status.Should().Be(HttpStatusCode.NotModified);
        unchanged.Body.Should().BeEmpty();
        unchanged.ETag.Should().Be(created.ETag);
        unchanged.Header("api-supported-versions").Should().Be("1.0");
        stale.Status.Should().Be(HttpStatusCode.OK);
        stale.ETag.Should().Be(created.ETag);
    }

    [Theory]
    [InlineData("W/\"AAAAAAAAAAA\"")]
    [InlineData("*")]
    [InlineData("AAAAAAAAAAA")]
    [InlineData("\"AAAAAAAAAAA\", \"BBBBBBBBBBB\"")]
    [InlineData("\"not valid!\"")]
    [Trait("Requirement", "REQ-API-003")]
    public async Task A_weak_or_listed_or_unquoted_etag_in_a_header_is_a_validation_error_for_that_header(string header)
    {
        await api.ResetAsync();
        var created = await api.CreateCategoryAsync("Garden");

        var read = await api.SendAsync(HttpMethod.Get, $"/api/v1/categories/{created.Id}", ApiCalls.Reader, null, ApiCalls.WithIfNoneMatch(header));
        var change = await api.SendAsync(HttpMethod.Put, $"/api/v1/categories/{created.Id}", ApiCalls.Admin, """{"name":"Yard"}""", ApiCalls.WithIfMatch(header));

        read.Status.Should().Be(HttpStatusCode.BadRequest);
        read.Slug.Should().Be("validation-failed");
        read.Json.GetProperty("errors").TryGetProperty("If-None-Match", out _).Should().BeTrue();
        change.Status.Should().Be(HttpStatusCode.BadRequest);
        change.Json.GetProperty("errors").TryGetProperty("If-Match", out _).Should().BeTrue();
    }

    [Fact]
    [Trait("Requirement", "REQ-API-001")]
    public async Task An_unknown_category_is_404_and_an_identifier_that_is_not_a_uuid_is_a_validation_error()
    {
        await api.ResetAsync();

        var unknown = await api.SendAsync(HttpMethod.Get, $"/api/v1/categories/{Guid.CreateVersion7()}", ApiCalls.Reader);
        var invalid = await api.SendAsync(HttpMethod.Get, "/api/v1/categories/not-a-uuid", ApiCalls.Reader);

        unknown.Status.Should().Be(HttpStatusCode.NotFound);
        unknown.Slug.Should().Be("not-found");
        invalid.Status.Should().Be(HttpStatusCode.BadRequest);
        invalid.Json.GetProperty("errors").TryGetProperty("categoryId", out _).Should().BeTrue();
    }

    [Fact]
    [Trait("Requirement", "REQ-API-003")]
    [Trait("Requirement", "REQ-APP-004")]
    public async Task Renaming_with_the_current_etag_gives_the_new_name_and_a_new_etag_and_the_old_etag_is_then_stale()
    {
        await api.ResetAsync();
        var created = await api.CreateCategoryAsync("Garden");
        var path = $"/api/v1/categories/{created.Id}";

        var renamed = await api.SendAsync(HttpMethod.Put, path, ApiCalls.Admin, """{"name":"Yard"}""", ApiCalls.WithIfMatch(ApiCalls.Quote(created.ETag!)));
        var again = await api.SendAsync(HttpMethod.Put, path, ApiCalls.Admin, """{"name":"Lawn"}""", ApiCalls.WithIfMatch(ApiCalls.Quote(created.ETag!)));
        var read = await api.SendAsync(HttpMethod.Get, path, ApiCalls.Reader);

        renamed.Status.Should().Be(HttpStatusCode.OK);
        renamed.Json.GetProperty("name").GetString().Should().Be("Yard");
        renamed.ETag.Should().NotBe(created.ETag).And.MatchRegex(ETagToken());
        again.Status.Should().Be(HttpStatusCode.PreconditionFailed);
        again.Slug.Should().Be("precondition-failed");
        read.Json.GetProperty("name").GetString().Should().Be("Yard");
        read.ETag.Should().Be(renamed.ETag);
    }

    [Fact]
    [Trait("Requirement", "REQ-API-003")]
    [Trait("Requirement", "REQ-APP-004")]
    public async Task A_change_without_if_match_is_428_and_does_not_change_the_category()
    {
        await api.ResetAsync();
        var created = await api.CreateCategoryAsync("Garden");
        var path = $"/api/v1/categories/{created.Id}";

        var rename = await api.SendAsync(HttpMethod.Put, path, ApiCalls.Admin, """{"name":"Yard"}""");
        var discontinue = await api.SendAsync(HttpMethod.Post, $"{path}/discontinue", ApiCalls.Admin);
        var read = await api.SendAsync(HttpMethod.Get, path, ApiCalls.Reader);

        rename.Status.Should().Be(HttpStatusCode.PreconditionRequired);
        rename.Slug.Should().Be("precondition-required");
        discontinue.Status.Should().Be(HttpStatusCode.PreconditionRequired);
        read.Json.GetProperty("name").GetString().Should().Be("Garden");
        read.Json.GetProperty("status").GetString().Should().Be("active");
        read.ETag.Should().Be(created.ETag);
    }

    [Fact]
    [Trait("Requirement", "REQ-API-005")]
    public async Task Renaming_to_an_empty_name_or_with_an_unknown_member_is_400_and_a_missing_category_is_404()
    {
        await api.ResetAsync();
        var created = await api.CreateCategoryAsync("Garden");
        var path = $"/api/v1/categories/{created.Id}";
        var ifMatch = ApiCalls.WithIfMatch(ApiCalls.Quote(created.ETag!));

        var empty = await api.SendAsync(HttpMethod.Put, path, ApiCalls.Admin, """{"name":"   "}""", ifMatch);
        var unknownMember = await api.SendAsync(HttpMethod.Put, path, ApiCalls.Admin, """{"name":"Yard","color":"green"}""", ifMatch);
        var missing = await api.SendAsync(HttpMethod.Put, $"/api/v1/categories/{Guid.CreateVersion7()}", ApiCalls.Admin, """{"name":"Yard"}""", ifMatch);

        empty.Status.Should().Be(HttpStatusCode.BadRequest);
        empty.Json.GetProperty("errors").TryGetProperty("name", out _).Should().BeTrue();
        unknownMember.Status.Should().Be(HttpStatusCode.BadRequest);
        unknownMember.Json.GetProperty("errors").TryGetProperty("color", out _).Should().BeTrue();
        missing.Status.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Requirement", "REQ-API-003")]
    public async Task Discontinuing_changes_the_status_once_and_the_same_action_again_changes_nothing()
    {
        await api.ResetAsync();
        var created = await api.CreateCategoryAsync("Garden");
        var path = $"/api/v1/categories/{created.Id}/discontinue";

        var first = await api.SendAsync(HttpMethod.Post, path, ApiCalls.Admin, null, ApiCalls.WithIfMatch(ApiCalls.Quote(created.ETag!)));
        var second = await api.SendAsync(HttpMethod.Post, path, ApiCalls.Admin, null, ApiCalls.WithIfMatch(ApiCalls.Quote(first.ETag!)));
        var stale = await api.SendAsync(HttpMethod.Post, path, ApiCalls.Admin, null, ApiCalls.WithIfMatch(ApiCalls.Quote(created.ETag!)));
        var unknown = await api.SendAsync(HttpMethod.Post, $"/api/v1/categories/{Guid.CreateVersion7()}/discontinue", ApiCalls.Admin, null, ApiCalls.WithIfMatch(ApiCalls.Quote(created.ETag!)));

        first.Status.Should().Be(HttpStatusCode.OK);
        first.Json.GetProperty("status").GetString().Should().Be("discontinued");
        first.ETag.Should().NotBe(created.ETag);
        second.Status.Should().Be(HttpStatusCode.OK);
        second.ETag.Should().Be(first.ETag);
        stale.Status.Should().Be(HttpStatusCode.PreconditionFailed);
        unknown.Status.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Requirement", "REQ-API-001")]
    public async Task Categories_come_in_pages_by_name_and_the_link_header_leads_to_the_next_page()
    {
        await api.ResetAsync();
        foreach (var name in new[] { "Delta", "Alpha", "Charlie", "Bravo", "Echo" })
        {
            await api.CreateCategoryAsync(name);
        }

        List<string> names = [];
        var next = "/api/v1/categories?pageSize=2";
        var pages = 0;

        while (next is not null)
        {
            var page = await api.SendAsync(HttpMethod.Get, next, ApiCalls.Reader);
            page.Status.Should().Be(HttpStatusCode.OK);
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
                link.Should().Be($"<http://localhost/api/v1/categories?pageSize=2&cursor={cursor.GetString()}>; rel=\"next\"");
                next = link![1..link.IndexOf('>', StringComparison.Ordinal)];
            }

            pages++;
        }

        pages.Should().Be(3);
        names.Should().Equal("Alpha", "Bravo", "Charlie", "Delta", "Echo");
    }

    [Fact]
    [Trait("Requirement", "REQ-API-001")]
    public async Task A_status_keeps_only_the_categories_that_have_it()
    {
        await api.ResetAsync();
        await api.CreateCategoryAsync("Garden");
        var toys = await api.CreateCategoryAsync("Toys");
        await api.SendAsync(HttpMethod.Post, $"/api/v1/categories/{toys.Id}/discontinue", ApiCalls.Admin, null, ApiCalls.WithIfMatch(ApiCalls.Quote(toys.ETag!)));

        var active = await api.SendAsync(HttpMethod.Get, "/api/v1/categories?status=active", ApiCalls.Reader);
        var discontinued = await api.SendAsync(HttpMethod.Get, "/api/v1/categories?status=discontinued", ApiCalls.Reader);
        var every = await api.SendAsync(HttpMethod.Get, "/api/v1/categories", ApiCalls.Reader);

        Names(active).Should().Equal("Garden");
        Names(discontinued).Should().Equal("Toys");
        Names(every).Should().Equal("Garden", "Toys");
    }

    [Theory]
    [InlineData("pageSize=0", "pageSize")]
    [InlineData("pageSize=101", "pageSize")]
    [InlineData("pageSize=many", "pageSize")]
    [InlineData("pageSize=-1", "pageSize")]
    [InlineData("cursor=not-a-cursor", "cursor")]
    [InlineData("status=gone", "status")]
    [InlineData("status=active&status=discontinued", "status")]
    [Trait("Requirement", "REQ-API-005")]
    public async Task A_parameter_that_is_not_valid_is_a_validation_error_that_names_it(string query, string field)
    {
        var response = await api.SendAsync(HttpMethod.Get, $"/api/v1/categories?{query}", ApiCalls.Reader);

        response.Status.Should().Be(HttpStatusCode.BadRequest);
        response.Slug.Should().Be("validation-failed");
        response.Json.GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue();
    }

    private static IEnumerable<string> Names(ApiResponse page) => page.Json.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString()!);
}
