using System.Text.Json;
using AwesomeAssertions;
using Catalog.Domain.Categories;
using Catalog.Domain.Common;
using Catalog.Domain.Products;
using Catalog.Domain.Tests.Support;
using Catalog.Infrastructure.Outbox;
using Xunit;

namespace Catalog.Infrastructure.Tests;

/// <summary>
/// REQ-DOM-004 and REQ-DATA-002: the JSON of each domain event is a stored contract, so each shape is held as a snapshot with every value written out (plan sections 8.3 and 8.4, ADR-0035). A rename of a property of the
/// Domain, a reordered field, or a new event without a shape fails a test here before a row is written. The tests need no database.
/// </summary>
public sealed class OutboxPayloadTests
{
    private const string Occurred = "2026-01-15T09:30:00.0000000+00:00";
    private const string Product1 = "00000000-0001-7001-8000-00000000002a";
    private const string Category1 = "00000000-0001-7001-8000-00000000002a";
    private const string Category2 = "00000000-0002-7001-8000-00000000002a";

    private static readonly Dictionary<string, (IDomainEvent Event, string Json)> Snapshots = new()
    {
        ["ProductCreated with a description"] = (
            new ProductCreated(TestIds.Product(1), Make.Sku("KB-100"), Make.ProductName("Mechanical keyboard"), Make.Description("A tenkeyless keyboard."), Make.Usd(89.90m), TestIds.Category(2), Make.Epoch),
            $$"""{"eventType":"ProductCreated","schemaVersion":1,"occurredAt":"{{Occurred}}","productId":"{{Product1}}","sku":"KB-100","name":"Mechanical keyboard","description":"A tenkeyless keyboard.","price":{"amount":89.90,"currency":"USD"},"categoryId":"{{Category2}}"}"""),
        ["ProductCreated without a description"] = (
            new ProductCreated(TestIds.Product(1), Make.Sku("KB-100"), Make.ProductName("Mechanical keyboard"), null, Make.Jpy(12000m), TestIds.Category(2), Make.Epoch),
            $$"""{"eventType":"ProductCreated","schemaVersion":1,"occurredAt":"{{Occurred}}","productId":"{{Product1}}","sku":"KB-100","name":"Mechanical keyboard","description":null,"price":{"amount":12000,"currency":"JPY"},"categoryId":"{{Category2}}"}"""),
        ["ProductDetailsChanged"] = (
            new ProductDetailsChanged(TestIds.Product(1), Make.ProductName("Mechanical keyboard"), null, Make.Epoch),
            $$"""{"eventType":"ProductDetailsChanged","schemaVersion":1,"occurredAt":"{{Occurred}}","productId":"{{Product1}}","name":"Mechanical keyboard","description":null}"""),
        ["ProductPriceChanged"] = (
            new ProductPriceChanged(TestIds.Product(1), Make.Usd(89.90m), Make.Usd(79m), Make.Epoch),
            $$$"""{"eventType":"ProductPriceChanged","schemaVersion":1,"occurredAt":"{{{Occurred}}}","productId":"{{{Product1}}}","oldPrice":{"amount":89.90,"currency":"USD"},"newPrice":{"amount":79.00,"currency":"USD"}}"""),
        ["ProductCategoryChanged"] = (
            new ProductCategoryChanged(TestIds.Product(1), TestIds.Category(1), TestIds.Category(2), Make.Epoch),
            $$"""{"eventType":"ProductCategoryChanged","schemaVersion":1,"occurredAt":"{{Occurred}}","productId":"{{Product1}}","oldCategoryId":"{{Category1}}","newCategoryId":"{{Category2}}"}"""),
        ["ProductDiscontinued"] = (
            new ProductDiscontinued(TestIds.Product(1), Make.Epoch),
            $$"""{"eventType":"ProductDiscontinued","schemaVersion":1,"occurredAt":"{{Occurred}}","productId":"{{Product1}}"}"""),
        ["CategoryCreated"] = (
            new CategoryCreated(TestIds.Category(1), Make.CategoryName("Keyboards"), Make.Epoch),
            $$"""{"eventType":"CategoryCreated","schemaVersion":1,"occurredAt":"{{Occurred}}","categoryId":"{{Category1}}","name":"Keyboards"}"""),
        ["CategoryRenamed"] = (
            new CategoryRenamed(TestIds.Category(1), Make.CategoryName("Keyboards"), Make.CategoryName("Input devices"), Make.Epoch),
            $$"""{"eventType":"CategoryRenamed","schemaVersion":1,"occurredAt":"{{Occurred}}","categoryId":"{{Category1}}","oldName":"Keyboards","newName":"Input devices"}"""),
        ["CategoryDiscontinued"] = (
            new CategoryDiscontinued(TestIds.Category(1), Make.Epoch),
            $$"""{"eventType":"CategoryDiscontinued","schemaVersion":1,"occurredAt":"{{Occurred}}","categoryId":"{{Category1}}"}"""),
    };

    public static TheoryData<string> Names => [.. Snapshots.Keys];

    [Theory]
    [MemberData(nameof(Names))]
    [Trait("Requirement", "REQ-DOM-004")]
    [Trait("Requirement", "REQ-DATA-002")]
    public void The_payload_of_each_event_matches_its_snapshot(string name)
    {
        var (domainEvent, json) = Snapshots[name];

        OutboxPayloads.Serialize(domainEvent).Should().Be(json);
    }

    [Theory]
    [MemberData(nameof(Names))]
    [Trait("Requirement", "REQ-DOM-004")]
    public void Every_payload_is_valid_json_that_starts_with_the_type_the_version_and_the_time(string name)
    {
        var (domainEvent, _) = Snapshots[name];

        using var document = JsonDocument.Parse(OutboxPayloads.Serialize(domainEvent));
        var properties = document.RootElement.EnumerateObject().ToList();

        properties.Take(3).Select(property => property.Name).Should().Equal("eventType", "schemaVersion", "occurredAt");
        properties[0].Value.GetString().Should().Be(domainEvent.EventType);
        properties[1].Value.GetInt32().Should().Be(domainEvent.SchemaVersion);
        properties[2].Value.GetDateTimeOffset().Should().Be(domainEvent.OccurredAt);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-004")]
    public void Every_event_type_of_the_domain_has_a_snapshot()
    {
        var domainEvents = typeof(IDomainEvent).Assembly.GetTypes()
            .Where(type => typeof(IDomainEvent).IsAssignableFrom(type) && type is { IsClass: true, IsAbstract: false })
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal);
        var snapshotted = Snapshots.Values.Select(snapshot => snapshot.Event.GetType().Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);

        snapshotted.Should().Equal(domainEvents, "a new event needs its shape in OutboxPayloads and a snapshot here before it can be saved");
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-004")]
    public void Text_is_escaped_as_json_and_stays_readable()
    {
        var domainEvent = new ProductDetailsChanged(TestIds.Product(1), Make.ProductName("Mechanical keyboard"), Make.Description("A \"quoted\" <b>tag</b> & Grün ✓"), Make.Epoch);

        var json = OutboxPayloads.Serialize(domainEvent);

        json.Should().Contain("\"description\":\"A \\\"quoted\\\" <b>tag</b> & Grün ✓\"");
        JsonDocument.Parse(json).RootElement.GetProperty("description").GetString().Should().Be("A \"quoted\" <b>tag</b> & Grün ✓");
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-004")]
    public void An_event_without_a_shape_is_refused_so_it_cannot_reach_the_outbox()
    {
        var act = () => OutboxPayloads.Serialize(new UnshapedEvent(Make.Epoch));

        act.Should().Throw<InvalidOperationException>().WithMessage("*UnshapedEvent*OutboxPayloads*");
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-004")]
    public void A_missing_event_is_refused()
    {
        var act = () => OutboxPayloads.Serialize(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private sealed record UnshapedEvent(DateTimeOffset OccurredAt) : IDomainEvent
    {
        public string EventType => nameof(UnshapedEvent);

        public int SchemaVersion => 1;
    }
}
