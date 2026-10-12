using AwesomeAssertions;
using Catalog.Domain.Categories;
using Catalog.Domain.Common;
using Catalog.Domain.Tests.Support;
using Xunit;

namespace Catalog.Domain.Tests;

/// <summary>
/// REQ-DOM-004 and REQ-DOM-005: a category records a versioned, time-stamped event for each change that it accepts, takes its times from the injected clock,
/// and stays in the catalog once it is discontinued, where it refuses new products (the check is on <see cref="Products.Product"/>) but not a new name.
/// </summary>
public sealed class CategoryTests
{
    [Fact]
    [Trait("Requirement", "REQ-DOM-004")]
    public void A_new_category_is_active_and_records_its_creation_at_the_time_of_the_clock()
    {
        var clock = Make.Clock();
        var id = TestIds.Category(5);

        var category = Category.Create(id, Make.CategoryName("Keyboards"), clock);

        category.Id.Should().Be(id);
        category.Name.Value.Should().Be("Keyboards");
        category.Status.Should().Be(LifecycleStatus.Active);
        category.IsActive.Should().BeTrue();
        category.CreatedAt.Should().Be(Make.Epoch);
        category.UpdatedAt.Should().Be(Make.Epoch);
        category.DomainEvents.Should().Equal(new CategoryCreated(id, category.Name, Make.Epoch));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-004")]
    public void Creating_a_category_needs_every_argument()
    {
        var clock = Make.Clock();

        var withoutId = () => Category.Create(null!, Make.CategoryName(), clock);
        var withoutName = () => Category.Create(TestIds.Category(), null!, clock);
        var withoutClock = () => Category.Create(TestIds.Category(), Make.CategoryName(), null!);

        withoutId.Should().Throw<ArgumentNullException>();
        withoutName.Should().Throw<ArgumentNullException>();
        withoutClock.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-004")]
    public void Renaming_changes_the_name_and_records_the_old_and_the_new_name()
    {
        var clock = Make.Clock();
        var category = Category.Create(TestIds.Category(), Make.CategoryName("Keyboards"), clock);
        category.ClearDomainEvents();
        clock.Advance(TimeSpan.FromMinutes(5));

        category.Rename(Make.CategoryName("Mechanical keyboards"), clock);

        category.Name.Value.Should().Be("Mechanical keyboards");
        category.UpdatedAt.Should().Be(Make.Epoch.AddMinutes(5));
        category.CreatedAt.Should().Be(Make.Epoch);
        category.DomainEvents.Should().Equal(
            new CategoryRenamed(category.Id, Make.CategoryName("Keyboards"), Make.CategoryName("Mechanical keyboards"), Make.Epoch.AddMinutes(5)));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-004")]
    public void Renaming_to_the_same_name_changes_and_records_nothing()
    {
        var clock = Make.Clock();
        var category = Make.Category();
        clock.Advance(TimeSpan.FromMinutes(5));
        category.ClearDomainEvents();

        category.Rename(Make.CategoryName("Keyboards"), clock);

        category.UpdatedAt.Should().Be(Make.Epoch);
        category.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-004")]
    public void A_discontinued_category_can_still_be_renamed()
    {
        var category = Make.DiscontinuedCategory();

        category.Rename(Make.CategoryName("Old keyboards"), Make.Clock());

        category.Name.Value.Should().Be("Old keyboards");
        category.IsActive.Should().BeFalse();
        category.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<CategoryRenamed>();
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-004")]
    public void Renaming_needs_every_argument()
    {
        var category = Make.Category();

        var withoutName = () => category.Rename(null!, Make.Clock());
        var withoutClock = () => category.Rename(Make.CategoryName("Other"), null!);

        withoutName.Should().Throw<ArgumentNullException>();
        withoutClock.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-005")]
    public void Discontinuing_makes_the_category_inactive_and_records_the_time()
    {
        var clock = Make.Clock();
        var category = Make.Category();
        clock.Advance(TimeSpan.FromHours(2));

        category.Discontinue(clock);

        category.Status.Should().Be(LifecycleStatus.Discontinued);
        category.IsActive.Should().BeFalse();
        category.UpdatedAt.Should().Be(Make.Epoch.AddHours(2));
        category.DomainEvents[^1].Should().Be(new CategoryDiscontinued(category.Id, Make.Epoch.AddHours(2)));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-005")]
    public void Discontinuing_twice_is_idempotent_and_records_one_event()
    {
        var clock = Make.Clock();
        var category = Make.Category();
        category.ClearDomainEvents();

        category.Discontinue(clock);
        clock.Advance(TimeSpan.FromHours(2));
        category.Discontinue(clock);

        category.DomainEvents.Should().ContainSingle();
        category.UpdatedAt.Should().Be(Make.Epoch);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-004")]
    public void Discontinuing_needs_a_clock()
    {
        var category = Make.Category();

        var discontinue = () => category.Discontinue(null!);

        discontinue.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-004")]
    public void Events_are_listed_oldest_first_and_cleared_on_request()
    {
        var clock = Make.Clock();
        var category = Make.Category();

        category.Rename(Make.CategoryName("Renamed"), clock);
        category.Discontinue(clock);

        category.DomainEvents.Select(domainEvent => domainEvent.EventType).Should().Equal("CategoryCreated", "CategoryRenamed", "CategoryDiscontinued");

        category.ClearDomainEvents();

        category.DomainEvents.Should().BeEmpty();
    }
}
