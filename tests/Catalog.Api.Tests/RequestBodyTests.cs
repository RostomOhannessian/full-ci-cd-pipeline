using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AwesomeAssertions;
using Catalog.Api.Tests.Support;
using Xunit;

namespace Catalog.Api.Tests;

/// <summary>
/// The strict and bounded reading of a request body (REQ-API-006): the media type, the size, the depth, the members that the contract does not name, and the types of the values. Every case is refused before a use case
/// runs, so these tests need no database. The limits are the defaults of the API: 32 KiB and 8 levels.
/// </summary>
public sealed class RequestBodyTests(SecuredApiFixture api) : IClassFixture<SecuredApiFixture>
{
    private const int LimitBytes = 32 * 1024;

    private string Admin => api.Token(new TokenSpec { Scopes = ["catalog.read", "catalog.write", "catalog.admin"], Roles = ["catalog-admin"] });

    private async Task<ApiResponse> PostAsync(string path, string? token, Action<HttpRequestMessage>? configure = null)
    {
        using var response = await api.SendAsync(HttpMethod.Post, path, token, request =>
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", ApiCalls.NewKey());
            configure?.Invoke(request);
        });
        var body = await response.TextAsync();

        return new ApiResponse(response.StatusCode, response.Headers, response.Content.Headers.ContentType?.MediaType, body);
    }

    private Task<ApiResponse> PostJsonAsync(string json, string path = "/api/v1/categories") =>
        PostAsync(path, Admin, request => request.Content = new StringContent(json, Encoding.UTF8, "application/json"));

    [Theory]
    [InlineData(null)]
    [InlineData("text/plain")]
    [InlineData("application/xml")]
    [InlineData("application/json; charset=utf-16")]
    [InlineData("application/jsonx")]
    [Trait("Requirement", "REQ-API-006")]
    public async Task A_body_that_is_not_utf_8_json_is_415(string? contentType)
    {
        var response = await PostAsync("/api/v1/categories", Admin, request =>
        {
            request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes("""{"name":"Garden"}"""));
            request.Content.Headers.ContentType = contentType is null ? null : MediaTypeHeaderValue.Parse(contentType);
        });

        response.Status.Should().Be(HttpStatusCode.UnsupportedMediaType);
        response.ContentType.Should().Be("application/problem+json");
        response.Slug.Should().Be("unsupported-media-type");
    }

    [Theory]
    [InlineData("application/json; charset=utf-8")]
    [InlineData("APPLICATION/JSON")]
    [InlineData("application/json")]
    [Trait("Requirement", "REQ-API-006")]
    public async Task The_media_type_is_compared_without_case_and_a_utf_8_charset_is_allowed(string contentType)
    {
        var response = await PostAsync("/api/v1/categories", Admin, request =>
        {
            request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes("""{"unknown":1}"""));
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        });

        response.Status.Should().Be(HttpStatusCode.BadRequest);
        response.Slug.Should().Be("validation-failed");
    }

    [Fact]
    [Trait("Requirement", "REQ-API-006")]
    public async Task A_body_larger_than_the_limit_is_413_whether_or_not_the_length_is_announced()
    {
        var announced = await PostAsync("/api/v1/categories", Admin, request =>
        {
            request.Content = new ByteArrayContent(new byte[LimitBytes + 1]);
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
        });
        var streamed = await PostAsync("/api/v1/categories", Admin, request =>
        {
            request.Content = new StreamContent(new MemoryStream(new byte[LimitBytes * 2]));
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
            request.Headers.TransferEncodingChunked = true;
        });

        announced.Status.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        announced.Slug.Should().Be("payload-too-large");
        streamed.Status.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        streamed.Slug.Should().Be("payload-too-large");
    }

    [Fact]
    [Trait("Requirement", "REQ-API-006")]
    public async Task A_body_of_exactly_the_limit_is_read()
    {
        // The member is unknown, so the request ends in a validation error and no use case runs, and the error shows that the whole body was read.
        var padding = new string('x', LimitBytes - """{"zzz":""}""".Length);
        var json = $$"""{"zzz":"{{padding}}"}""";
        Encoding.UTF8.GetByteCount(json).Should().Be(LimitBytes);

        var response = await PostJsonAsync(json);

        response.Status.Should().Be(HttpStatusCode.BadRequest);
        response.Slug.Should().Be("validation-failed");
        response.Json.GetProperty("errors").TryGetProperty("zzz", out _).Should().BeTrue();
    }

    [Fact]
    [Trait("Requirement", "REQ-API-006")]
    public async Task A_body_nested_deeper_than_the_limit_is_malformed_and_one_at_the_limit_is_read()
    {
        static string Nested(int levels) => string.Concat(Enumerable.Repeat("{\"a\":", levels)) + "1" + new string('}', levels);

        var atTheLimit = await PostJsonAsync(Nested(8));
        var deeper = await PostJsonAsync(Nested(9));

        atTheLimit.Slug.Should().Be("validation-failed");
        deeper.Status.Should().Be(HttpStatusCode.BadRequest);
        deeper.Slug.Should().Be("malformed-request", deeper.Body);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{\"name\":")]
    [InlineData("{\"name\":\"Garden\"")]
    [InlineData("not json")]
    [InlineData("{'name':'Garden'}")]
    [InlineData("{\"name\":\"Garden\"}{\"name\":\"Yard\"}")]
    [Trait("Requirement", "REQ-API-006")]
    public async Task A_body_that_is_not_valid_json_is_malformed(string json)
    {
        var response = await PostJsonAsync(json);

        response.Status.Should().Be(HttpStatusCode.BadRequest);
        response.Slug.Should().Be("malformed-request");
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("5")]
    [InlineData("null")]
    [InlineData("true")]
    [Trait("Requirement", "REQ-API-006")]
    public async Task A_body_that_is_not_an_object_is_a_validation_error_for_the_body(string json)
    {
        var response = await PostJsonAsync(json);

        response.Status.Should().Be(HttpStatusCode.BadRequest);
        response.Slug.Should().Be("validation-failed");
        response.Json.GetProperty("errors").TryGetProperty("body", out _).Should().BeTrue();
    }

    [Fact]
    [Trait("Requirement", "REQ-API-006")]
    public async Task Every_member_that_the_contract_does_not_name_is_listed_with_its_path_and_case_matters()
    {
        var category = await PostJsonAsync("""{"name":"Garden","color":"green","Name":"x"}""");
        var product = await PostJsonAsync("""{"sku":"KB-1","name":"k","price":{"amount":"1","currency":"USD","extra":1},"categoryId":"0197f3a2-8c1e-7a53-9b0d-4f6e2c1a5d90","tags":[]}""", "/api/v1/products");

        var categoryErrors = category.Json.GetProperty("errors");
        var productErrors = product.Json.GetProperty("errors");

        category.Status.Should().Be(HttpStatusCode.BadRequest);
        categoryErrors.TryGetProperty("color", out _).Should().BeTrue();
        categoryErrors.TryGetProperty("Name", out _).Should().BeTrue();
        categoryErrors.TryGetProperty("name", out _).Should().BeFalse();
        productErrors.TryGetProperty("price.extra", out _).Should().BeTrue();
        productErrors.TryGetProperty("tags", out _).Should().BeTrue();
    }

    [Theory]
    [InlineData("""{"name":5}""", "/api/v1/categories", "name")]
    [InlineData("""{"name":["a"]}""", "/api/v1/categories", "name")]
    [InlineData("""{"sku":"KB-1","name":"k","price":"free","categoryId":"0197f3a2-8c1e-7a53-9b0d-4f6e2c1a5d90"}""", "/api/v1/products", "price")]
    [InlineData("""{"sku":"KB-1","name":"k","price":{"amount":5,"currency":"USD"},"categoryId":"0197f3a2-8c1e-7a53-9b0d-4f6e2c1a5d90"}""", "/api/v1/products", "price.amount")]
    [InlineData("""{"sku":"KB-1","name":"k","price":{"amount":"5","currency":"USD"},"categoryId":"nope"}""", "/api/v1/products", "categoryId")]
    [InlineData("""{"sku":"KB-1","name":"k","description":7,"price":{"amount":"5","currency":"USD"},"categoryId":"0197f3a2-8c1e-7a53-9b0d-4f6e2c1a5d90"}""", "/api/v1/products", "description")]
    [Trait("Requirement", "REQ-API-006")]
    public async Task A_value_of_the_wrong_type_is_a_validation_error_for_its_field(string json, string path, string field)
    {
        var response = await PostJsonAsync(json, path);

        response.Status.Should().Be(HttpStatusCode.BadRequest);
        response.Slug.Should().Be("validation-failed");
        response.Json.GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue(response.Body);
    }

    [Fact]
    [Trait("Requirement", "REQ-API-006")]
    public async Task The_scope_is_checked_before_the_body_and_the_key_before_the_body()
    {
        var reader = await PostAsync("/api/v1/categories", api.Token(), request =>
        {
            request.Content = new StringContent("not json", Encoding.UTF8, "text/plain");
        });
        using var withoutKey = await api.SendAsync(HttpMethod.Post, "/api/v1/categories", Admin, request => request.Content = new StringContent("not json", Encoding.UTF8, "application/json"));

        reader.Status.Should().Be(HttpStatusCode.Forbidden);
        reader.Slug.Should().Be("forbidden");
        withoutKey.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await withoutKey.JsonAsync()).ProblemSlug().Should().Be("idempotency-key-required");
    }
}
