using AwesomeAssertions;
using Catalog.Application.Results;
using Catalog.Domain.Common;
using Catalog.Domain.Pricing;
using Catalog.Domain.Products;
using Catalog.Domain.Tests.Support;
using Catalog.Infrastructure.Tests.Support;
using Xunit;

namespace Catalog.Infrastructure.Tests;

/// <summary>
/// The aggregates make a round trip through SQL Server and come back as they went in (plan section 8.4). The Domain has private constructors and value objects that make themselves, so these tests
/// are the proof that the mappings rebuild every value through the model and that a loaded aggregate is not a new one: it records no event.
/// </summary>
public sealed class AggregateRoundTripTests(CatalogDatabaseFixture database) : IClassFixture<CatalogDatabaseFixture>
{
    [Fact]
    [Trait("Requirement", "REQ-DATA-001")]
    public async Task A_product_comes_back_with_every_value_that_it_was_saved_with()
    {
        var clock = Make.Clock();
        var category = Make.Category(Counters.Next(), "Keyboards");
        var product = Make.Product(category, clock, Counters.Next(), Counters.Sku(), 89.90m);

        await using (var writer = new CatalogScope(database))
        {
            await writer.SaveAsync(category);
            await writer.SaveAsync(product);
        }

        await using var reader = new CatalogScope(database);
        var loaded = (await reader.Products.FindAsync(product.Id, CancellationToken.None))!.Entity;

        loaded.Id.Should().Be(product.Id);
        loaded.Sku.Should().Be(product.Sku);
        loaded.Name.Should().Be(product.Name);
        loaded.Description.Should().Be(product.Description);
        loaded.Price.Should().Be(product.Price);
        loaded.CategoryId.Should().Be(category.Id);
        loaded.Status.Should().Be(LifecycleStatus.Active);
        loaded.CreatedAt.Should().Be(product.CreatedAt);
        loaded.UpdatedAt.Should().Be(product.UpdatedAt);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-001")]
    public async Task A_product_without_a_description_comes_back_with_none()
    {
        var clock = Make.Clock();
        var category = Make.Category(Counters.Next());
        var product = Product.Create(TestIds.Product(Counters.Next()), Make.Sku(Counters.Sku()), Make.ProductName(), null, Make.Usd(10m), category, clock).Value;

        await using (var writer = new CatalogScope(database))
        {
            await writer.SaveAsync(category);
            await writer.SaveAsync(product);
        }

        await using var reader = new CatalogScope(database);
        var loaded = (await reader.Products.FindAsync(product.Id, CancellationToken.None))!.Entity;

        loaded.Description.Should().BeNull();
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-001")]
    public async Task A_price_in_yen_comes_back_with_no_decimal_places_although_the_column_keeps_two()
    {
        var clock = Make.Clock();
        var category = Make.Category(Counters.Next());
        var product = Product.Create(TestIds.Product(Counters.Next()), Make.Sku(Counters.Sku()), Make.ProductName(), null, Make.Jpy(1500m), category, clock).Value;

        await using (var writer = new CatalogScope(database))
        {
            await writer.SaveAsync(category);
            await writer.SaveAsync(product);
        }

        await using var reader = new CatalogScope(database);
        var loaded = (await reader.Products.FindAsync(product.Id, CancellationToken.None))!.Entity;

        loaded.Price.Should().Be(Make.Jpy(1500m));
        loaded.Price.Currency.Should().Be(Currency.Jpy);
        loaded.Price.ToString().Should().Be("1500 JPY");
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-001")]
    public async Task A_category_comes_back_with_every_value_that_it_was_saved_with()
    {
        var category = Make.Category(Counters.Next(), "Keyboards");

        await using (var writer = new CatalogScope(database))
        {
            await writer.SaveAsync(category);
        }

        await using var reader = new CatalogScope(database);
        var loaded = (await reader.Categories.FindAsync(category.Id, CancellationToken.None))!.Entity;

        loaded.Id.Should().Be(category.Id);
        loaded.Name.Should().Be(category.Name);
        loaded.Status.Should().Be(LifecycleStatus.Active);
        loaded.CreatedAt.Should().Be(category.CreatedAt);
        loaded.UpdatedAt.Should().Be(category.UpdatedAt);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-001")]
    public async Task A_discontinued_product_and_a_discontinued_category_keep_their_status()
    {
        var clock = Make.Clock();
        var category = Make.Category(Counters.Next());
        var product = Make.Product(category, clock, Counters.Next(), Counters.Sku());
        product.Discontinue(clock);
        category.Discontinue(clock);

        await using (var writer = new CatalogScope(database))
        {
            await writer.SaveAsync(category);
            await writer.SaveAsync(product);
        }

        await using var reader = new CatalogScope(database);

        (await reader.Products.FindAsync(product.Id, CancellationToken.None))!.Entity.Status.Should().Be(LifecycleStatus.Discontinued);
        (await reader.Categories.FindAsync(category.Id, CancellationToken.None))!.Entity.Status.Should().Be(LifecycleStatus.Discontinued);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-001")]
    public async Task An_update_of_every_part_is_written_and_a_later_load_sees_it()
    {
        var clock = Make.Clock();
        var first = Make.Category(Counters.Next(), "Keyboards");
        var second = Make.Category(Counters.Next(), "Mice");
        var product = Make.Product(first, clock, Counters.Next(), Counters.Sku(), 89.90m);

        await using (var writer = new CatalogScope(database))
        {
            await writer.SaveAsync(first);
            await writer.SaveAsync(second);
            await writer.SaveAsync(product);
        }

        clock.Advance(TimeSpan.FromMinutes(5));
        await using (var editor = new CatalogScope(database))
        {
            var loaded = (await editor.Products.FindAsync(product.Id, CancellationToken.None))!.Entity;
            var moved = (await editor.Categories.FindAsync(second.Id, CancellationToken.None))!.Entity;
            loaded.Update(Make.ProductName("Wireless mouse"), Make.Description("A quiet mouse."), Make.Usd(24.50m), moved, clock).IsSuccess.Should().BeTrue();

            var result = await editor.UnitOfWork.ExecuteAsync(_ => Task.FromResult(Result.Success(1)), CancellationToken.None);
            result.IsSuccess.Should().BeTrue();
        }

        await using var reader = new CatalogScope(database);
        var stored = (await reader.Products.FindAsync(product.Id, CancellationToken.None))!.Entity;

        stored.Name.Value.Should().Be("Wireless mouse");
        stored.Description!.Value.Should().Be("A quiet mouse.");
        stored.Price.Should().Be(Make.Usd(24.50m));
        stored.CategoryId.Should().Be(second.Id);
        stored.UpdatedAt.Should().Be(clock.GetUtcNow());
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-004")]
    public async Task A_loaded_aggregate_records_no_event_because_loading_is_not_a_change()
    {
        var clock = Make.Clock();
        var category = Make.Category(Counters.Next());
        var product = Make.Product(category, clock, Counters.Next(), Counters.Sku());

        await using (var writer = new CatalogScope(database))
        {
            await writer.SaveAsync(category);
            await writer.SaveAsync(product);
        }

        await using var reader = new CatalogScope(database);

        (await reader.Products.FindAsync(product.Id, CancellationToken.None))!.Entity.DomainEvents.Should().BeEmpty();
        (await reader.Categories.FindAsync(category.Id, CancellationToken.None))!.Entity.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-005")]
    public async Task A_stored_category_that_was_discontinued_refuses_a_new_product_after_it_is_loaded()
    {
        var clock = Make.Clock();
        var category = Make.Category(Counters.Next());
        category.Discontinue(clock);

        await using (var writer = new CatalogScope(database))
        {
            await writer.SaveAsync(category);
        }

        await using var reader = new CatalogScope(database);
        var loaded = (await reader.Categories.FindAsync(category.Id, CancellationToken.None))!.Entity;

        var created = Product.Create(TestIds.Product(Counters.Next()), Make.Sku(Counters.Sku()), Make.ProductName(), null, Make.Usd(10m), loaded, clock);

        created.IsSuccess.Should().BeFalse();
        Make.ErrorOf(created).Code.Should().Be(DomainErrorCode.CategoryNotActive);
    }
}
