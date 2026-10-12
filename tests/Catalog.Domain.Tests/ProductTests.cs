using AwesomeAssertions;
using Catalog.Domain.Categories;
using Catalog.Domain.Common;
using Catalog.Domain.Pricing;
using Catalog.Domain.Products;
using Catalog.Domain.Tests.Support;
using Xunit;

namespace Catalog.Domain.Tests;

/// <summary>
/// REQ-DOM-003, REQ-DOM-004, and REQ-DOM-005: a product is created in an active category, a discontinued product refuses a new price and a new category but
/// not a new name, an update is all or nothing, and every accepted change records one versioned, time-stamped event.
/// </summary>
public sealed class ProductTests
{
    private static readonly DateTimeOffset Later = Make.Epoch.AddMinutes(30);

    private static (Product Product, Category Category, Microsoft.Extensions.Time.Testing.FakeTimeProvider Clock) ActiveProduct()
    {
        var clock = Make.Clock();
        var category = Make.Category();

        return (Make.Product(category, clock), category, clock);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-004")]
    public void A_new_product_is_active_in_its_category_and_records_its_creation()
    {
        var clock = Make.Clock();
        var category = Make.Category(2);
        var id = TestIds.Product(9);

        var result = Product.Create(id, Make.Sku("kb-100"), Make.ProductName("Keyboard"), Make.Description("Tenkeyless."), Make.Usd(89.9m), category, clock);

        var product = result.Value;
        product.Id.Should().Be(id);
        product.Sku.Value.Should().Be("KB-100");
        product.Name.Value.Should().Be("Keyboard");
        product.Description!.Value.Should().Be("Tenkeyless.");
        product.Price.Should().Be(Make.Usd(89.90m));
        product.CategoryId.Should().Be(category.Id);
        product.Status.Should().Be(LifecycleStatus.Active);
        product.IsActive.Should().BeTrue();
        product.CreatedAt.Should().Be(Make.Epoch);
        product.UpdatedAt.Should().Be(Make.Epoch);
        product.DomainEvents.Should().Equal(
            new ProductCreated(id, product.Sku, product.Name, product.Description, product.Price, category.Id, Make.Epoch));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-004")]
    public void A_product_may_have_no_description()
    {
        var product = Product.Create(TestIds.Product(), Make.Sku(), Make.ProductName(), null, Make.Usd(1m), Make.Category(), Make.Clock()).Value;

        product.Description.Should().BeNull();
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-005")]
    public void A_product_cannot_be_created_in_a_discontinued_category()
    {
        var result = Product.Create(TestIds.Product(), Make.Sku(), Make.ProductName(), null, Make.Usd(1m), Make.DiscontinuedCategory(), Make.Clock());

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(new DomainError(DomainErrorCode.CategoryNotActive, "categoryId", "The category is discontinued, so no product can be assigned to it."));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-004")]
    public void Creating_a_product_needs_every_argument()
    {
        var category = Make.Category();
        var clock = Make.Clock();
        var id = TestIds.Product();

        var calls = new Action[]
        {
            () => Product.Create(null!, Make.Sku(), Make.ProductName(), null, Make.Usd(1m), category, clock),
            () => Product.Create(id, null!, Make.ProductName(), null, Make.Usd(1m), category, clock),
            () => Product.Create(id, Make.Sku(), null!, null, Make.Usd(1m), category, clock),
            () => Product.Create(id, Make.Sku(), Make.ProductName(), null, null!, category, clock),
            () => Product.Create(id, Make.Sku(), Make.ProductName(), null, Make.Usd(1m), null!, clock),
            () => Product.Create(id, Make.Sku(), Make.ProductName(), null, Make.Usd(1m), category, null!),
        };

        foreach (var call in calls)
        {
            call.Should().Throw<ArgumentNullException>();
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-004")]
    public void Creating_a_product_needs_an_identifier_even_when_the_category_is_discontinued()
    {
        // The category check comes first, so only the guard on the identifier can throw here.
        var create = () => Product.Create(null!, Make.Sku(), Make.ProductName(), null, Make.Usd(1m), Make.DiscontinuedCategory(), Make.Clock());

        create.Should().Throw<ArgumentNullException>().WithParameterName("id");
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-004")]
    public void Changing_only_the_name_records_one_details_event()
    {
        var (product, category, clock) = ActiveProduct();
        clock.SetUtcNow(Later);

        var result = product.Update(Make.ProductName("Better keyboard"), product.Description, product.Price, category, clock);

        result.IsSuccess.Should().BeTrue();
        product.Name.Value.Should().Be("Better keyboard");
        product.UpdatedAt.Should().Be(Later);
        product.CreatedAt.Should().Be(Make.Epoch);
        product.DomainEvents.Should().Equal(new ProductDetailsChanged(product.Id, Make.ProductName("Better keyboard"), product.Description, Later));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-004")]
    public void Changing_only_the_description_records_one_details_event_and_clearing_it_is_a_change()
    {
        var (product, category, clock) = ActiveProduct();

        product.Update(product.Name, Make.Description("Another text."), product.Price, category, clock);
        product.Update(product.Name, null, product.Price, category, clock);

        product.Description.Should().BeNull();
        product.DomainEvents.Should().HaveCount(2).And.AllBeOfType<ProductDetailsChanged>();
        product.DomainEvents.Cast<ProductDetailsChanged>().Last().Description.Should().BeNull();
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-004")]
    public void Changing_only_the_price_records_the_old_and_the_new_price()
    {
        var (product, category, clock) = ActiveProduct();
        clock.SetUtcNow(Later);

        var result = product.Update(product.Name, product.Description, Make.Usd(79.90m), category, clock);

        result.IsSuccess.Should().BeTrue();
        product.Price.Should().Be(Make.Usd(79.90m));
        product.DomainEvents.Should().Equal(new ProductPriceChanged(product.Id, Make.Usd(89.90m), Make.Usd(79.90m), Later));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-004")]
    public void Changing_only_the_currency_is_a_price_change()
    {
        var (product, category, clock) = ActiveProduct();

        product.Update(product.Name, product.Description, Money.Create(89.90m, Currency.Eur).Value, category, clock);

        product.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ProductPriceChanged>();
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-005")]
    public void Moving_to_another_active_category_records_the_old_and_the_new_category()
    {
        var (product, _, clock) = ActiveProduct();
        var target = Make.Category(2, "Mice");
        clock.SetUtcNow(Later);

        var result = product.Update(product.Name, product.Description, product.Price, target, clock);

        result.IsSuccess.Should().BeTrue();
        product.CategoryId.Should().Be(target.Id);
        product.DomainEvents.Should().Equal(new ProductCategoryChanged(product.Id, TestIds.Category(1), target.Id, Later));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-004")]
    public void Changing_everything_at_once_records_the_events_in_a_fixed_order_with_one_time()
    {
        var (product, _, clock) = ActiveProduct();
        var target = Make.Category(2, "Mice");
        clock.SetUtcNow(Later);

        var result = product.Update(Make.ProductName("Mouse"), null, Make.Usd(19.90m), target, clock);

        result.IsSuccess.Should().BeTrue();
        product.DomainEvents.Select(domainEvent => domainEvent.EventType).Should().Equal("ProductDetailsChanged", "ProductPriceChanged", "ProductCategoryChanged");
        product.DomainEvents.Select(domainEvent => domainEvent.OccurredAt).Should().OnlyContain(time => time == Later);
        product.UpdatedAt.Should().Be(Later);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-004")]
    public void An_update_that_changes_nothing_records_nothing_and_keeps_the_time()
    {
        var (product, category, clock) = ActiveProduct();
        clock.SetUtcNow(Later);

        var result = product.Update(Make.ProductName(), Make.Description(), Make.Usd(89.9m), category, clock);

        result.IsSuccess.Should().BeTrue();
        product.DomainEvents.Should().BeEmpty();
        product.UpdatedAt.Should().Be(Make.Epoch);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-005")]
    public void Moving_to_a_discontinued_category_is_refused_and_changes_nothing_even_if_other_parts_changed()
    {
        var (product, _, clock) = ActiveProduct();
        var target = Make.DiscontinuedCategory(2, "Old mice");
        clock.SetUtcNow(Later);

        var result = product.Update(Make.ProductName("Mouse"), null, Make.Usd(19.90m), target, clock);

        result.Error.Should().Be(new DomainError(DomainErrorCode.CategoryNotActive, "categoryId", "The category is discontinued, so no product can be assigned to it."));
        product.Name.Value.Should().Be("Mechanical keyboard");
        product.Description.Should().NotBeNull();
        product.Price.Should().Be(Make.Usd(89.90m));
        product.CategoryId.Should().Be(TestIds.Category(1));
        product.DomainEvents.Should().BeEmpty();
        product.UpdatedAt.Should().Be(Make.Epoch);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-005")]
    public void A_product_keeps_a_category_that_was_discontinued_after_it_was_assigned_and_can_still_change()
    {
        var (product, category, clock) = ActiveProduct();
        category.Discontinue(clock);

        var result = product.Update(Make.ProductName("Renamed"), product.Description, Make.Usd(10m), category, clock);

        result.IsSuccess.Should().BeTrue();
        product.Name.Value.Should().Be("Renamed");
        product.Price.Should().Be(Make.Usd(10m));
        product.CategoryId.Should().Be(category.Id);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-003")]
    public void A_discontinued_product_can_change_its_name_and_description()
    {
        var (product, category, clock) = ActiveProduct();
        product.Discontinue(clock);
        product.ClearDomainEvents();
        clock.SetUtcNow(Later);

        var result = product.Update(Make.ProductName("Legacy keyboard"), null, product.Price, category, clock);

        result.IsSuccess.Should().BeTrue();
        product.Name.Value.Should().Be("Legacy keyboard");
        product.Description.Should().BeNull();
        product.IsActive.Should().BeFalse();
        product.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ProductDetailsChanged>();
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-003")]
    public void A_discontinued_product_refuses_a_new_price()
    {
        var (product, category, clock) = ActiveProduct();
        product.Discontinue(clock);
        product.ClearDomainEvents();

        var result = product.Update(Make.ProductName("Renamed"), product.Description, Make.Usd(1m), category, clock);

        result.Error.Should().Be(new DomainError(DomainErrorCode.ProductDiscontinued, string.Empty, "The product is discontinued. Only its name and description can change."));
        product.Price.Should().Be(Make.Usd(89.90m));
        product.Name.Value.Should().Be("Mechanical keyboard", "the update is all or nothing");
        product.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-003")]
    public void A_discontinued_product_refuses_a_new_category_whether_or_not_it_is_active()
    {
        var (product, category, clock) = ActiveProduct();
        product.Discontinue(clock);
        product.ClearDomainEvents();

        var toActive = product.Update(product.Name, product.Description, product.Price, Make.Category(2, "Mice"), clock);
        var toDiscontinued = product.Update(product.Name, product.Description, product.Price, Make.DiscontinuedCategory(3, "Old mice"), clock);

        toActive.Error!.Code.Should().Be(DomainErrorCode.ProductDiscontinued);
        toDiscontinued.Error!.Code.Should().Be(DomainErrorCode.ProductDiscontinued, "the product refuses first");
        product.CategoryId.Should().Be(category.Id);
        product.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-003")]
    public void Discontinuing_a_product_records_the_time_and_is_idempotent()
    {
        var (product, _, clock) = ActiveProduct();
        clock.SetUtcNow(Later);

        product.Discontinue(clock);
        clock.Advance(TimeSpan.FromHours(1));
        product.Discontinue(clock);

        product.Status.Should().Be(LifecycleStatus.Discontinued);
        product.UpdatedAt.Should().Be(Later);
        product.DomainEvents.Should().Equal(new ProductDiscontinued(product.Id, Later));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-004")]
    public void Updating_and_discontinuing_need_every_argument()
    {
        var (product, category, clock) = ActiveProduct();

        var calls = new Action[]
        {
            () => product.Update(null!, null, product.Price, category, clock),
            () => product.Update(product.Name, null, null!, category, clock),
            () => product.Update(product.Name, null, product.Price, null!, clock),
            () => product.Update(product.Name, null, product.Price, category, null!),
            () => product.Discontinue(null!),
        };

        foreach (var call in calls)
        {
            call.Should().Throw<ArgumentNullException>();
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-004")]
    public void The_sku_and_the_identifier_never_change()
    {
        var (product, _, clock) = ActiveProduct();
        var id = product.Id;
        var sku = product.Sku;

        product.Update(Make.ProductName("Other"), null, Make.Usd(1m), Make.Category(2), clock);
        product.Discontinue(clock);

        product.Id.Should().Be(id);
        product.Sku.Should().Be(sku);
    }
}
