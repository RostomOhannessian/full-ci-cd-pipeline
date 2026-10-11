using System.Text.RegularExpressions;
using Catalog.Contract.Tests.Support;
using Microsoft.OpenApi;
using Xunit;

namespace Catalog.Contract.Tests;

/// <summary>
/// REQ-API-002, REQ-API-003, and REQ-API-004: the headers, the conditional requests, and the idempotency rules of plan sections 8.4 and 8.6, checked against
/// sample values and against the status codes that each operation documents. The linter checks that a header is present, and these tests check what it accepts.
/// </summary>
public sealed class HttpSemanticsTests
{
    private static readonly string[] Common = ["400", "401", "403", "429", "500", "503"];

    private static readonly string[] VersionHeaders = ["api-supported-versions", "api-deprecated-versions", "Deprecation", "Sunset"];

    private static bool Matches(string pattern, string value) =>
        Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static string PatternOf(string schemaName) => Authored.Document.Components!.Schemas![schemaName].Pattern!;

    private static string HeaderPattern(string headerName) => Authored.Document.Components!.Headers![headerName].Schema!.Pattern!;

    [Theory]
    [InlineData("\"AAAAAAAAB9E\"", true)]
    [InlineData("\"abc\"", true)]
    [InlineData("\"a-b_C9\"", true)]
    [InlineData("W/\"abc\"", false)]
    [InlineData("*", false)]
    [InlineData("\"a\", \"b\"", false)]
    [InlineData("abc", false)]
    [InlineData("\"\"", false)]
    [InlineData("\"with space\"", false)]
    [Trait("Requirement", "REQ-API-003")]
    public void An_entity_tag_is_one_strong_quoted_token(string value, bool accepted)
    {
        Assert.Equal(accepted, Matches(PatternOf("StrongEntityTag"), value));
    }

    [Theory]
    [InlineData("\"c1b7e2a4-5f1d-4d0a-8f2e-3a6d9e1b7c42\"", true)]
    [InlineData("\"a\"", true)]
    [InlineData("c1b7e2a4-5f1d-4d0a-8f2e-3a6d9e1b7c42", false)]
    [InlineData("\"\"", false)]
    [InlineData("\"has\\\"quote\"", false)]
    [InlineData("\"has space\"", false)]
    [Trait("Requirement", "REQ-API-004")]
    public void An_idempotency_key_is_the_quoted_string_of_the_ietf_draft(string value, bool accepted)
    {
        var pattern = Authored.Document.Components!.Parameters!["IdempotencyKey"].Schema!.Pattern!;

        Assert.Equal(accepted, Matches(pattern, value));
    }

    [Fact]
    [Trait("Requirement", "REQ-API-004")]
    public void An_idempotency_key_of_128_characters_is_accepted_and_one_of_129_is_not()
    {
        var pattern = Authored.Document.Components!.Parameters!["IdempotencyKey"].Schema!.Pattern!;

        Assert.True(Matches(pattern, $"\"{new string('k', 128)}\""));
        Assert.False(Matches(pattern, $"\"{new string('k', 129)}\""));
    }

    [Theory]
    [InlineData("1.0", true)]
    [InlineData("1.0, 2.0", true)]
    [InlineData("1", false)]
    [InlineData("v1", false)]
    [Trait("Requirement", "REQ-API-002")]
    public void The_version_headers_name_versions_as_major_and_minor_numbers(string value, bool accepted)
    {
        Assert.Equal(accepted, Matches(HeaderPattern("ApiSupportedVersions"), value));
        Assert.Equal(accepted, Matches(HeaderPattern("ApiDeprecatedVersions"), value));
    }

    [Theory]
    [InlineData("@1798761600", true)]
    [InlineData("1798761600", false)]
    [InlineData("@-1", false)]
    [InlineData("Wed, 30 Jun 2027 23:59:59 GMT", false)]
    [Trait("Requirement", "REQ-API-002")]
    public void The_deprecation_header_is_a_structured_field_date_as_rfc_9745_says(string value, bool accepted)
    {
        Assert.Equal(accepted, Matches(HeaderPattern("Deprecation"), value));
    }

    [Fact]
    [Trait("Requirement", "REQ-API-002")]
    public void Every_answer_documents_the_four_version_headers_and_the_supported_versions_is_required()
    {
        var headers = Authored.Document.Components!.Headers!;

        Assert.True(headers["ApiSupportedVersions"].Required);
        Assert.All(["ApiDeprecatedVersions", "Deprecation", "Sunset"], name => Assert.False(headers[name].Required, $"{name} is sent only for a deprecated version."));

        var responses = ContractOperation.All.SelectMany(operation => operation.Operation.Responses!.Values)
            .Concat(Authored.Document.Components.Responses!.Values);
        Assert.All(responses, response => Assert.True(
            response.Headers is not null && VersionHeaders.All(response.Headers.ContainsKey),
            $"A response lacks a version header: {response.Description}"));
    }

    [Theory]
    [InlineData("getCategory")]
    [InlineData("getProduct")]
    [Trait("Requirement", "REQ-API-003")]
    public void A_get_of_one_item_is_conditional_and_answers_304_with_its_etag_and_no_body(string operationId)
    {
        var operation = ContractOperation.Get(operationId);

        Assert.False(operation.HeaderParameters["If-None-Match"], "If-None-Match is optional.");
        var notModified = operation.Operation.Responses!["304"];
        Assert.True(notModified.Headers!.ContainsKey("ETag"));
        Assert.True(notModified.Content is null or { Count: 0 }, "A 304 has no body.");
        Assert.True(operation.Operation.Responses["200"].Headers!["ETag"].Required);
    }

    [Theory]
    [InlineData("updateCategory")]
    [InlineData("discontinueCategory")]
    [InlineData("updateProduct")]
    [InlineData("discontinueProduct")]
    [Trait("Requirement", "REQ-API-003")]
    public void A_change_requires_if_match_and_documents_412_for_a_stale_tag_and_428_for_a_missing_one(string operationId)
    {
        var operation = ContractOperation.Get(operationId);

        Assert.True(operation.HeaderParameters["If-Match"], "If-Match is required.");
        Assert.Contains("412", operation.Statuses);
        Assert.Contains("428", operation.Statuses);
        Assert.Equal(412, ProblemStatusOf(operation, "412"));
        Assert.Equal(428, ProblemStatusOf(operation, "428"));
        Assert.True(operation.Operation.Responses!["200"].Headers!["ETag"].Required, "The answer carries the new ETag.");
    }

    [Theory]
    [InlineData("createCategory")]
    [InlineData("createProduct")]
    [Trait("Requirement", "REQ-API-004")]
    public void A_create_requires_an_idempotency_key_and_documents_400_409_and_422(string operationId)
    {
        var operation = ContractOperation.Get(operationId);

        Assert.True(operation.HeaderParameters["Idempotency-Key"], "Idempotency-Key is required.");
        Assert.Contains("201", operation.Statuses);
        Assert.Equal(400, ProblemStatusOf(operation, "400"));
        Assert.Equal(409, ProblemStatusOf(operation, "409"));
        Assert.Equal(422, ProblemStatusOf(operation, "422"));

        var created = operation.Operation.Responses!["201"];
        Assert.True(created.Headers!["Location"].Required);
        Assert.True(created.Headers["ETag"].Required);
    }

    [Fact]
    [Trait("Requirement", "REQ-API-004")]
    public void Only_a_create_takes_an_idempotency_key_and_only_a_change_takes_if_match()
    {
        foreach (var operation in ContractOperation.All)
        {
            var creates = operation.OperationId.StartsWith("create", StringComparison.Ordinal);
            var changes = operation.OperationId.StartsWith("update", StringComparison.Ordinal) || operation.OperationId.StartsWith("discontinue", StringComparison.Ordinal);

            Assert.Equal(creates, operation.HeaderParameters.ContainsKey("Idempotency-Key"));
            Assert.Equal(changes, operation.HeaderParameters.ContainsKey("If-Match"));
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-API-004")]
    public void A_replay_of_a_create_is_documented_as_the_first_201()
    {
        foreach (var id in new[] { "createCategory", "createProduct" })
        {
            var operation = ContractOperation.Get(id);

            Assert.Contains("first response", operation.Operation.Responses!["201"].Description, StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(StatusMatrix))]
    [Trait("Requirement", "REQ-API-005")]
    [Trait("Requirement", "REQ-API-003")]
    public void Each_operation_documents_exactly_the_statuses_that_the_conventions_give_it(string operationId, string[] extra)
    {
        string[] expected = [.. Common, .. extra];

        var actual = ContractOperation.Get(operationId).Statuses.Order(StringComparer.Ordinal);

        Assert.Equal(expected.Order(StringComparer.Ordinal), actual);
    }

    public static TheoryData<string, string[]> StatusMatrix() => new()
    {
        { "listCategories", ["200"] },
        { "listProducts", ["200"] },
        { "getCategory", ["200", "304", "404"] },
        { "getProduct", ["200", "304", "404"] },
        { "createCategory", ["201", "409", "413", "415", "422"] },
        { "createProduct", ["201", "409", "413", "415", "422"] },
        { "updateCategory", ["200", "404", "412", "413", "415", "428"] },
        { "updateProduct", ["200", "404", "409", "412", "413", "415", "428"] },
        { "discontinueCategory", ["200", "404", "412", "428"] },
        { "discontinueProduct", ["200", "404", "412", "428"] },
    };

    [Fact]
    [Trait("Requirement", "REQ-API-005")]
    public void A_429_and_a_503_carry_retry_after_and_a_401_carries_the_bearer_challenge()
    {
        var responses = Authored.Document.Components!.Responses!;

        Assert.True(responses["TooManyRequests"].Headers!["Retry-After"].Required);
        Assert.True(responses["ServiceUnavailable"].Headers!["Retry-After"].Required);
        Assert.True(responses["Unauthorized"].Headers!["WWW-Authenticate"].Required);
        Assert.Equal(JsonSchemaType.Integer, Authored.Document.Components!.Headers!["RetryAfter"].Schema!.Type);
    }

    [Fact]
    [Trait("Requirement", "REQ-API-005")]
    public void A_collection_page_can_carry_a_link_header_that_is_optional_because_the_last_page_has_none()
    {
        foreach (var id in new[] { "listCategories", "listProducts" })
        {
            Assert.False(ContractOperation.Get(id).Operation.Responses!["200"].Headers!["Link"].Required);
        }
    }

    private static int ProblemStatusOf(ContractOperation operation, string status)
    {
        var content = operation.Operation.Responses![status].Content!["application/problem+json"];
        var first = content.Examples!.Values.First().Value!;

        return Json.Int(first["status"]);
    }
}
