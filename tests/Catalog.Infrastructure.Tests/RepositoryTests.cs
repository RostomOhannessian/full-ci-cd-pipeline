using AwesomeAssertions;
using Catalog.Application.Results;
using Catalog.Domain.Products;
using Catalog.Domain.Tests.Support;
using Catalog.Infrastructure.Tests.Support;
using Xunit;

namespace Catalog.Infrastructure.Tests;

/// <summary>REQ-ARC-003 and REQ-DATA-001: the repositories load aggregates with their version, answer whether a SKU is taken whatever the status of its product, and give the version as of the last save.</summary>
public sealed class RepositoryTests(CatalogDatabaseFixture database) : IClassFixture<CatalogDatabaseFixture>
{
    [Fact]
    [Trait("Requirement", "REQ-DATA-001")]
    public async Task An_identifier_that_no_aggregate_has_finds_nothing()
    {
        await using var scope = new CatalogScope(database);

        (await scope.Products.FindAsync(TestIds.Product(Counters.Next()), CancellationToken.None)).Should().BeNull();
        (await scope.Categories.FindAsync(TestIds.Category(Counters.Next()), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-001")]
    public async Task A_sku_is_taken_when_any_product_has_it_even_a_discontinued_one()
    {
        var clock = Make.Clock();
        var category = Make.Category(Counters.Next());
        var sku = Counters.Sku();
        var product = Make.Product(category, clock, Counters.Next(), sku);
        product.Discontinue(clock);
        await using var scope = new CatalogScope(database);
        await scope.SaveAsync(category);
        await scope.SaveAsync(product);

        (await scope.Products.SkuExistsAsync(product.Sku, CancellationToken.None)).Should().BeTrue();
        (await scope.Products.SkuExistsAsync(Sku.Create(Counters.Sku()).Value, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-001")]
    public async Task A_sku_in_lower_case_is_the_same_sku_because_the_model_upper_cases_it()
    {
        var clock = Make.Clock();
        var category = Make.Category(Counters.Next());
        var sku = Counters.Sku();
        var product = Make.Product(category, clock, Counters.Next(), sku);
        await using var scope = new CatalogScope(database);
        await scope.SaveAsync(category);
        await scope.SaveAsync(product);

        (await scope.Products.SkuExistsAsync(Sku.Create(sku.ToLowerInvariant()).Value, CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-001")]
    public async Task The_version_of_an_added_product_exists_only_after_the_first_save()
    {
        var clock = Make.Clock();
        var category = Make.Category(Counters.Next());
        var product = Make.Product(category, clock, Counters.Next(), Counters.Sku());
        await using var scope = new CatalogScope(database);
        await scope.SaveAsync(category);

        scope.Products.Add(product);
        var before = () => scope.Products.TagOf(product);
        before.Should().Throw<InvalidOperationException>().WithMessage("*SaveChangesAsync*");

        await scope.UnitOfWork.SaveChangesAsync(CancellationToken.None);

        scope.Products.TagOf(product).Value.Should().NotBeNullOrEmpty();
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-001")]
    public async Task A_save_that_changes_a_product_gives_it_a_new_version_and_a_save_that_changes_nothing_keeps_it()
    {
        var clock = Make.Clock();
        var category = Make.Category(Counters.Next());
        var product = Make.Product(category, clock, Counters.Next(), Counters.Sku());
        await using var scope = new CatalogScope(database);
        await scope.SaveAsync(category);
        var created = await scope.SaveAsync(product);

        await scope.UnitOfWork.SaveChangesAsync(CancellationToken.None);
        scope.Products.TagOf(product).Should().Be(created, "nothing changed, so SQL Server did not write the row");

        clock.Advance(TimeSpan.FromMinutes(1));
        product.Discontinue(clock);
        await scope.UnitOfWork.ExecuteAsync(_ => Task.FromResult(Result.Success(1)), CancellationToken.None);

        scope.Products.TagOf(product).Should().NotBe(created);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-001")]
    public async Task The_version_of_a_loaded_product_is_the_version_that_the_save_gave_it()
    {
        var clock = Make.Clock();
        var category = Make.Category(Counters.Next());
        var product = Make.Product(category, clock, Counters.Next(), Counters.Sku());
        await using var writer = new CatalogScope(database);
        await writer.SaveAsync(category);
        var saved = await writer.SaveAsync(product);

        await using var reader = new CatalogScope(database);
        var loaded = await reader.Products.FindAsync(product.Id, CancellationToken.None);

        loaded!.Tag.Should().Be(saved);
        reader.Products.TagOf(loaded.Entity).Should().Be(saved);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-001")]
    public async Task A_product_that_this_repository_never_tracked_has_no_version()
    {
        var clock = Make.Clock();
        var product = Make.Product(Make.Category(Counters.Next()), clock, Counters.Next(), Counters.Sku());
        await using var scope = new CatalogScope(database);

        var act = () => scope.Products.TagOf(product);

        act.Should().Throw<InvalidOperationException>().WithMessage("*not tracked*");
    }

    [Fact]
    [Trait("Requirement", "REQ-ARC-003")]
    public async Task The_repositories_refuse_a_null_argument_because_that_is_a_programming_error()
    {
        await using var scope = new CatalogScope(database);

        await scope.Products.Invoking(repository => repository.FindAsync(null!, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
        await scope.Categories.Invoking(repository => repository.FindAsync(null!, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
        await scope.Products.Invoking(repository => repository.SkuExistsAsync(null!, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
        scope.Products.Invoking(repository => repository.Add(null!)).Should().Throw<ArgumentNullException>();
        scope.Categories.Invoking(repository => repository.Add(null!)).Should().Throw<ArgumentNullException>();
        scope.Products.Invoking(repository => repository.TagOf(null!)).Should().Throw<ArgumentNullException>();
        scope.Categories.Invoking(repository => repository.TagOf(null!)).Should().Throw<ArgumentNullException>();
    }
}
