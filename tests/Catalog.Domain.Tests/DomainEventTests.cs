using AwesomeAssertions;
using Catalog.Domain.Common;
using Catalog.Domain.Products;
using Catalog.Domain.Tests.Support;
using Xunit;

namespace Catalog.Domain.Tests;

/// <summary>
/// REQ-DOM-004: every domain event names its type, carries a schema version, and carries the time of the injected clock. The outbox (WP1.8) stores these
/// events as JSON, so the shape of an event is a contract with the rows that are already stored. The golden list below fails when a property is added,
/// renamed, or removed, which is the moment to raise <see cref="IDomainEvent.SchemaVersion"/> and to write the upgrade for the old payloads.
/// </summary>
public sealed class DomainEventTests
{
    private static readonly Dictionary<string, string[]> GoldenShapes = new()
    {
        ["CategoryCreated"] = ["CategoryId", "Name", "OccurredAt"],
        ["CategoryRenamed"] = ["CategoryId", "OldName", "NewName", "OccurredAt"],
        ["CategoryDiscontinued"] = ["CategoryId", "OccurredAt"],
        ["ProductCreated"] = ["ProductId", "Sku", "Name", "Description", "Price", "CategoryId", "OccurredAt"],
        ["ProductDetailsChanged"] = ["ProductId", "Name", "Description", "OccurredAt"],
        ["ProductPriceChanged"] = ["ProductId", "OldPrice", "NewPrice", "OccurredAt"],
        ["ProductCategoryChanged"] = ["ProductId", "OldCategoryId", "NewCategoryId", "OccurredAt"],
        ["ProductDiscontinued"] = ["ProductId", "OccurredAt"],
    };

    private static List<Type> EventTypes() =>
        [.. typeof(IDomainEvent).Assembly.GetTypes().Where(type => type.IsClass && typeof(IDomainEvent).IsAssignableFrom(type)).OrderBy(type => type.Name, StringComparer.Ordinal)];

    /// <summary>Runs every operation that records an event, with a clock that advances, and returns what was recorded.</summary>
    private static List<IDomainEvent> RecordEveryEvent()
    {
        var clock = Make.Clock();
        var category = Make.Category(1, "Keyboards");
        var other = Make.Category(2, "Mice");
        var product = Make.Product(category, clock);
        List<IDomainEvent> events = [.. category.DomainEvents];

        clock.Advance(TimeSpan.FromMinutes(1));
        category.Rename(Make.CategoryName("Mechanical keyboards"), clock);
        clock.Advance(TimeSpan.FromMinutes(1));
        product.Update(Make.ProductName("Mouse"), null, Make.Usd(19.90m), other, clock);
        clock.Advance(TimeSpan.FromMinutes(1));
        product.Discontinue(clock);
        clock.Advance(TimeSpan.FromMinutes(1));
        category.Discontinue(clock);
        var created = Product.Create(TestIds.Product(2), Make.Sku("MS-200"), Make.ProductName("Mouse"), null, Make.Usd(1m), other, clock).Value;

        events.AddRange(category.DomainEvents.Skip(1));
        events.AddRange(product.DomainEvents);
        events.AddRange(created.DomainEvents);

        return events;
    }

    [Fact]
    [Trait("Requirement", "REQ-QUA-004")]
    public void Running_the_same_scenario_twice_gives_the_same_events_so_a_failure_can_be_replayed()
    {
        // The identifiers come from a counter and the times from a fake clock, so nothing in the scenario depends on the host or the moment.
        RecordEveryEvent().Should().Equal(RecordEveryEvent());
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-004")]
    public void The_scenario_records_every_event_type_of_the_model()
    {
        var recorded = RecordEveryEvent().Select(domainEvent => domainEvent.GetType()).Distinct().OrderBy(type => type.Name, StringComparer.Ordinal).ToList();

        recorded.Should().Equal(EventTypes(), "a new event type needs a scenario here, so that its name, its version, and its time are checked");
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-004")]
    public void Each_event_names_its_own_type_and_has_schema_version_1()
    {
        foreach (var domainEvent in RecordEveryEvent())
        {
            domainEvent.EventType.Should().Be(domainEvent.GetType().Name);
            domainEvent.SchemaVersion.Should().Be(1, domainEvent.EventType);
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-004")]
    public void The_times_of_the_events_come_from_the_clock_in_the_order_of_the_changes()
    {
        var times = RecordEveryEvent().Select(domainEvent => domainEvent.OccurredAt).ToList();

        times.Should().OnlyContain(time => time >= Make.Epoch && time <= Make.Epoch.AddMinutes(4));
        times.Should().Contain(Make.Epoch).And.Contain(Make.Epoch.AddMinutes(4));
        times.Select(time => time.Offset).Should().OnlyContain(offset => offset == TimeSpan.Zero);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-004")]
    public void The_event_types_are_the_golden_list_and_each_is_a_sealed_public_record()
    {
        EventTypes().Select(type => type.Name).Should().BeEquivalentTo(GoldenShapes.Keys);

        foreach (var type in EventTypes())
        {
            type.IsPublic.Should().BeTrue(type.Name);
            type.IsSealed.Should().BeTrue(type.Name);
            type.GetMethod("<Clone>$").Should().NotBeNull($"{type.Name} is a record, so it compares by value");
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-004")]
    public void The_properties_of_each_event_are_the_golden_ones()
    {
        foreach (var type in EventTypes())
        {
            var properties = type.GetProperties().Where(property => property.Name is not ("EventType" or "SchemaVersion" or "EqualityContract")).Select(property => property.Name);

            properties.Should().Equal(GoldenShapes[type.Name], $"the shape of {type.Name} changed, so raise its SchemaVersion and write the upgrade for stored payloads");
        }
    }
}
