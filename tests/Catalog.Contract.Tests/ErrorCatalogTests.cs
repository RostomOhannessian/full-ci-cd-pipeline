using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Catalog.Contract.Tests.Support;
using Xunit;

namespace Catalog.Contract.Tests;

/// <summary>
/// REQ-API-005: every error type that the contract names is an entry of the published error catalog, and every entry of the catalog is returned by the contract,
/// with the same status and the same title. The linter reads one document, so the agreement between two documents is a test.
/// </summary>
public sealed class ErrorCatalogTests
{
    private const string BaseAddress = "https://rostomohannessian.github.io/full-ci-cd-pipeline/reference/error-catalog.html#";

    private static readonly string Catalog = Authored.ReadText("docs", "reference", "error-catalog.md");

    /// <summary>The entries of the catalog page: a heading that is a slug, followed by its status and its title.</summary>
    private static readonly IReadOnlyDictionary<string, (int Status, string Title)> Entries = ReadEntries();

    /// <summary>Every example of a problem body, with the status code of the response that holds it.</summary>
    private static readonly IReadOnlyList<(string OperationId, string StatusKey, string ExampleName, JsonNode Body)> Examples =
    [
        .. ContractOperation.All.SelectMany(operation => operation.Operation.Responses!
            .Where(response => response.Value.Content is not null && response.Value.Content.ContainsKey("application/problem+json"))
            .SelectMany(response => response.Value.Content!["application/problem+json"].Examples!
                .Select(example => (operation.OperationId, response.Key, example.Key, example.Value.Value!)))),
    ];

    private static string SlugOf(JsonNode body) => ((string)body["type"]!)[BaseAddress.Length..];

    private static Dictionary<string, (int Status, string Title)> ReadEntries()
    {
        Dictionary<string, (int Status, string Title)> entries = [];
        var lines = Catalog.Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            var heading = Regex.Match(lines[index], "^## ([a-z0-9]+(?:-[a-z0-9]+)*)$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (!heading.Success)
            {
                continue;
            }

            var status = Regex.Match(string.Join('\n', lines.Skip(index + 1).Take(4)), @"- \*\*Status:\*\* (\d{3})", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            var title = Regex.Match(string.Join('\n', lines.Skip(index + 1).Take(4)), @"- \*\*Title:\*\* (.+)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            Assert.True(status.Success && title.Success, $"The entry '{heading.Groups[1].Value}' needs a Status line and a Title line right after its heading.");
            entries.Add(heading.Groups[1].Value, (int.Parse(status.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), title.Groups[1].Value.Trim()));
        }

        return entries;
    }

    [Fact]
    [Trait("Requirement", "REQ-API-005")]
    public void The_catalog_has_an_entry_for_each_error_type_of_adr_0028()
    {
        string[] expected =
        [
            "validation-failed", "malformed-request", "idempotency-key-required", "unauthorized", "forbidden", "not-found", "sku-already-exists", "product-discontinued",
            "idempotency-request-in-progress", "precondition-failed", "payload-too-large", "unsupported-media-type", "idempotency-key-reused", "precondition-required",
            "rate-limited", "internal-error", "service-unavailable",
        ];

        Assert.Equal(expected.Order(StringComparer.Ordinal), Entries.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Requirement", "REQ-API-005")]
    public void Every_error_example_of_the_contract_names_an_entry_of_the_catalog_through_the_published_address()
    {
        Assert.NotEmpty(Examples);

        foreach (var (operationId, statusKey, name, body) in Examples)
        {
            var type = (string)body["type"]!;

            Assert.StartsWith(BaseAddress, type, StringComparison.Ordinal);
            Assert.True(Entries.ContainsKey(SlugOf(body)), $"{operationId} {statusKey} example '{name}' names '{type}', which is not an entry of the error catalog.");
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-API-005")]
    public void Every_entry_of_the_catalog_is_returned_by_at_least_one_example_of_the_contract()
    {
        var returned = Examples.Select(example => SlugOf(example.Body)).ToHashSet(StringComparer.Ordinal);

        Assert.All(Entries.Keys, slug => Assert.True(returned.Contains(slug), $"The catalog lists '{slug}', and no response of the contract has an example of it."));
    }

    [Fact]
    [Trait("Requirement", "REQ-API-005")]
    public void An_example_has_the_status_of_its_response_and_of_its_catalog_entry_and_the_title_of_the_entry()
    {
        foreach (var (operationId, statusKey, name, body) in Examples)
        {
            var entry = Entries[SlugOf(body)];
            var where = $"{operationId} {statusKey} example '{name}'";

            Assert.True(statusKey == Json.Int(body["status"]).ToString(System.Globalization.CultureInfo.InvariantCulture), $"{where}: the example status differs from the response status.");
            Assert.True(entry.Status == Json.Int(body["status"]), $"{where}: the example status differs from the catalog status {entry.Status}.");
            Assert.True(entry.Title == (string)body["title"]!, $"{where}: the example title differs from the catalog title '{entry.Title}'.");
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-API-005")]
    public void An_example_follows_the_problem_details_schema_of_the_contract()
    {
        var schema = Authored.Document.Components!.Schemas!["ProblemDetails"];
        var typePattern = schema.Properties!["type"].Pattern!;
        var tracePattern = schema.Properties["traceId"].Pattern!;

        Assert.Equal(["status", "title", "traceId", "type"], schema.Required!.Order(StringComparer.Ordinal));

        foreach (var (operationId, statusKey, name, body) in Examples)
        {
            var where = $"{operationId} {statusKey} example '{name}'";

            Assert.True(Regex.IsMatch((string)body["type"]!, typePattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)), $"{where}: type.");
            Assert.True(Regex.IsMatch((string)body["traceId"]!, tracePattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)), $"{where}: traceId.");
            Assert.StartsWith("/api/v1/", (string)body["instance"]!, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace((string?)body["detail"]), $"{where}: detail.");
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-API-005")]
    public void Only_a_validation_failure_lists_fields_and_each_field_has_at_least_one_message()
    {
        foreach (var (operationId, statusKey, name, body) in Examples)
        {
            var where = $"{operationId} {statusKey} example '{name}'";
            var isValidation = SlugOf(body) == "validation-failed";

            Assert.Equal(isValidation, body["errors"] is not null);
            if (isValidation)
            {
                var errors = body["errors"]!.AsObject();
                Assert.NotEmpty(errors);
                Assert.All(errors, field => Assert.True(field.Value!.AsArray().Count > 0 && field.Value.AsArray().All(message => !string.IsNullOrWhiteSpace((string?)message)), $"{where}: {field.Key} needs messages."));
            }
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-API-005")]
    public void An_example_detail_never_leaks_internals()
    {
        string[] leaks = ["Exception", "StackTrace", " at ", "SELECT ", "System.", "Microsoft.", "password", "secret"];

        foreach (var (operationId, statusKey, name, body) in Examples)
        {
            var detail = (string)body["detail"]!;

            Assert.All(leaks, leak => Assert.False(detail.Contains(leak, StringComparison.OrdinalIgnoreCase), $"{operationId} {statusKey} example '{name}' has '{leak}' in its detail."));
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-API-005")]
    public void Every_error_response_is_problem_json_and_the_validation_body_adds_the_errors_member()
    {
        foreach (var operation in ContractOperation.All)
        {
            foreach (var response in operation.Operation.Responses!.Where(response => int.Parse(response.Key, System.Globalization.CultureInfo.InvariantCulture) >= 400))
            {
                Assert.Equal(["application/problem+json"], response.Value.Content!.Keys);
            }
        }

        Assert.NotNull(Authored.Document.Components!.Schemas!["ValidationProblemDetails"].AllOf);
    }

    [Fact]
    [Trait("Requirement", "REQ-API-005")]
    public void The_catalog_page_says_that_the_api_does_not_exist_yet_and_when_the_addresses_resolve()
    {
        Assert.Contains("WP1.10", Catalog, StringComparison.Ordinal);
        Assert.Contains("WP1.13", Catalog, StringComparison.Ordinal);
        Assert.Contains(BaseAddress, Catalog, StringComparison.Ordinal);
    }
}
