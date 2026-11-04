using Catalog.Api.Http;
using Xunit;

namespace Catalog.Contract.Tests;

/// <summary>
/// REQ-API-005 and ADR-0028: the entries of the error catalog that only the API raises are the ones that the API writes, with the same status and the same title. The application raises the other nine, and
/// <see cref="ApplicationAgreementTests"/> holds them, so together the two tables name every entry of the catalog exactly once.
/// </summary>
public sealed class ApiAgreementTests
{
    [Fact]
    [Trait("Requirement", "REQ-API-005")]
    public void Every_row_of_the_api_table_is_an_entry_of_the_catalog_with_the_same_status_and_title()
    {
        foreach (var type in ApiProblems.All)
        {
            Assert.True(ErrorCatalogTests.Entries.TryGetValue(type.Slug, out var entry), $"'{type.Slug}' is not an entry of the error catalog.");
            Assert.Equal(entry.Status, type.Status);
            Assert.Equal(entry.Title, type.Title);
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-API-005")]
    public void The_api_table_and_the_mapping_table_of_the_application_name_every_entry_of_the_catalog_once()
    {
        var owned = ApiProblems.All.Select(type => type.Slug).Concat(Catalog.Application.Results.ProblemMapping.All.Select(type => type.Slug)).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(ErrorCatalogTests.Entries.Keys.Order(StringComparer.Ordinal), owned);
    }

    [Fact]
    [Trait("Requirement", "REQ-API-005")]
    public void The_address_of_an_entry_is_the_published_address_with_its_slug_as_the_fragment()
    {
        Assert.Equal(
            "https://rostomohannessian.github.io/full-ci-cd-pipeline/reference/error-catalog.html#rate-limited",
            ApiProblems.AddressOf(ApiProblems.RateLimited));
        Assert.Equal("about:blank", ApiProblems.AddressOf(ApiProblems.Other(405)));
    }
}
