using System.Globalization;
using System.Text.RegularExpressions;
using Catalog.Contract.Tests.Support;
using Xunit;

namespace Catalog.Contract.Tests;

/// <summary>
/// REQ-API-001, REQ-API-002, and REQ-CON-001: the pages that describe the contract say what the contract says. The permission matrix is a table that a reader
/// trusts, so a test reads it and compares it with the operations of the document.
/// </summary>
public sealed class DocumentationAgreementTests
{
    private static readonly string ContractPage = Authored.ReadText("docs", "reference", "api-contract.md");

    private static readonly string StyleGuide = Authored.ReadText("docs", "reference", "api-style-guide.md");

    private static readonly string Adr0028 = Authored.ReadText("docs", "adr", "0028-http-api-conventions-of-v1.md");

    [Fact]
    [Trait("Requirement", "REQ-API-001")]
    public void The_permission_matrix_of_the_contract_page_lists_each_operation_with_its_scope_and_its_success_status()
    {
        var rows = MarkdownTable.After(ContractPage, "## Permission matrix");

        var documented = rows.Select(row => $"{row[0]} {row[1]} {row[2]} {row[3]} {row[4]}").Order(StringComparer.Ordinal);
        var actual = ContractOperation.All.Select(operation =>
            $"{operation.OperationId} {operation.Method} {operation.Path} {operation.Scopes.Single()} {operation.Statuses.First(status => status.StartsWith('2'))}").Order(StringComparer.Ordinal);

        Assert.Equal(actual, documented);
    }

    [Fact]
    [Trait("Requirement", "REQ-API-001")]
    public void The_contract_page_names_the_resources_and_the_filters_of_the_plan()
    {
        foreach (var text in new[] { "/api/v1/categories", "/api/v1/categories/{categoryId}", "/api/v1/products", "/api/v1/products/{productId}", "categoryId", "status", "`q`", "sort", "pageSize", "cursor" })
        {
            Assert.Contains(text, ContractPage, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-CON-001")]
    public void The_contract_page_points_at_the_authored_document_and_says_it_is_the_only_description()
    {
        Assert.Contains("(../../contracts/openapi/catalog/v1/catalog.openapi.yaml)", ContractPage, StringComparison.Ordinal);
        Assert.Contains("only description of the HTTP surface", ContractPage, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-API-005")]
    public void The_style_guide_lists_every_status_code_that_the_contract_documents()
    {
        var documented = MarkdownTable.After(StyleGuide, "## Methods and status codes").Select(row => row[0]).Order(StringComparer.Ordinal);
        var actual = ContractOperation.All.SelectMany(operation => operation.Statuses).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);

        Assert.Equal(actual, documented);
    }

    [Fact]
    [Trait("Requirement", "REQ-API-002")]
    public void The_style_guide_documents_versioning_and_the_deprecation_window_with_the_headers_of_the_contract()
    {
        var section = StyleGuide[StyleGuide.IndexOf("## Versioning and deprecation", StringComparison.Ordinal)..];
        section = section[..section.IndexOf("\n## Errors", StringComparison.Ordinal)];

        foreach (var text in new[] { "api-supported-versions", "api-deprecated-versions", "Deprecation", "Sunset", "RFC 9745", "RFC 8594", "180 days" })
        {
            Assert.Contains(text, section, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-API-002")]
    public void The_deprecation_window_is_the_same_number_in_the_style_guide_and_in_the_adr()
    {
        var guide = Regex.Match(StyleGuide, @"at least (\d+) days", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var adr = Regex.Match(Adr0028, @"at least (\d+) days", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        Assert.True(guide.Success && adr.Success);
        Assert.Equal(180, int.Parse(guide.Groups[1].Value, CultureInfo.InvariantCulture));
        Assert.Equal(guide.Groups[1].Value, adr.Groups[1].Value);
    }

    [Fact]
    [Trait("Requirement", "REQ-API-002")]
    public void The_contract_says_that_the_version_is_a_path_segment_and_every_path_starts_with_it()
    {
        Assert.All(ContractOperation.All, operation => Assert.StartsWith("/api/v1/", operation.Path, StringComparison.Ordinal));
        Assert.Contains("The version is a segment of the path (`/api/v1`)", Authored.ReadText("contracts", "openapi", "catalog", "v1", "catalog.openapi.yaml"), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-CON-001")]
    public void The_style_guide_names_every_catalog_lint_rule_that_it_cites_and_each_rule_exists()
    {
        var ruleset = Authored.ReadText("contracts", "openapi", "spectral.yaml");
        var declared = Regex.Matches(ruleset, "(?m)^  (catalog-[a-z0-9-]+):", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var cited = Regex.Matches(StyleGuide, @"rules? `(catalog-[a-z0-9-]+)`", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Select(match => match.Groups[1].Value).ToList();

        Assert.NotEmpty(cited);
        Assert.All(cited, rule => Assert.Contains(rule, declared));
    }
}
