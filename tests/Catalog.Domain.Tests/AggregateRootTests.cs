using AwesomeAssertions;
using Catalog.Domain.Common;
using Catalog.Domain.Tests.Support;
using Xunit;

namespace Catalog.Domain.Tests;

/// <summary>REQ-DOM-004: the base of every aggregate refuses a missing identifier and a missing event, and it keeps the events that were recorded until they are cleared.</summary>
public sealed class AggregateRootTests
{
    private sealed class Probe(string id) : AggregateRoot<string>(id)
    {
        public void Record(IDomainEvent domainEvent) => Raise(domainEvent);
    }

    private sealed record Happened(DateTimeOffset OccurredAt) : IDomainEvent
    {
        public string EventType => nameof(Happened);

        public int SchemaVersion => 1;
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-004")]
    public void An_aggregate_needs_an_identifier()
    {
        var create = () => new Probe(null!);

        create.Should().Throw<ArgumentNullException>().WithParameterName("id");
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-004")]
    public void Recording_a_missing_event_is_refused()
    {
        var probe = new Probe("a");

        var record = () => probe.Record(null!);

        record.Should().Throw<ArgumentNullException>().WithParameterName("domainEvent");
        probe.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-004")]
    public void Events_are_kept_in_order_until_they_are_cleared()
    {
        var probe = new Probe("a");
        var first = new Happened(Make.Epoch);
        var second = new Happened(Make.Epoch.AddSeconds(1));

        probe.Record(first);
        probe.Record(second);

        probe.Id.Should().Be("a");
        probe.DomainEvents.Should().Equal(first, second);

        probe.ClearDomainEvents();

        probe.DomainEvents.Should().BeEmpty();
    }
}
