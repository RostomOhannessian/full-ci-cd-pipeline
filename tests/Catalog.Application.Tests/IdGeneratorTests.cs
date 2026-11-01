using System.Globalization;
using AwesomeAssertions;
using Catalog.Application.Identity;
using Catalog.Application.Tests.Support;
using Catalog.Domain.Tests.Support;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Catalog.Application.Tests;

/// <summary>REQ-DOM-001 and REQ-ARC-003: the default identifier generator makes UUIDs of version 7 from the clock, and the port is small enough for a test to replace.</summary>
public sealed class IdGeneratorTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Requirement", "REQ-DOM-001")]
    public void Generated_identifiers_are_distinct_uuids_of_version_7()
    {
        var generator = new GuidV7IdGenerator(new FakeTimeProvider(Epoch));

        var products = Enumerable.Range(0, 100).Select(_ => generator.NewProductId().Value);
        var categories = Enumerable.Range(0, 100).Select(_ => generator.NewCategoryId().Value);

        products.Concat(categories).ToList().Should().OnlyHaveUniqueItems().And.OnlyContain(id => id.Version == 7);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-001")]
    public void Identifiers_sort_in_the_order_of_the_clock()
    {
        var clock = new FakeTimeProvider(Epoch);
        var generator = new GuidV7IdGenerator(clock);

        var first = generator.NewProductId().Value;
        clock.Advance(TimeSpan.FromMilliseconds(5));
        var second = generator.NewProductId().Value;
        clock.Advance(TimeSpan.FromDays(1));
        var third = generator.NewCategoryId().Value;

        string.CompareOrdinal(Sortable(first), Sortable(second)).Should().BeNegative();
        string.CompareOrdinal(Sortable(second), Sortable(third)).Should().BeNegative();
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-001")]
    public void The_identifier_carries_the_time_of_the_clock_in_milliseconds()
    {
        var generator = new GuidV7IdGenerator(new FakeTimeProvider(Epoch));

        var id = generator.NewProductId().Value;

        // The first 48 bits of a version 7 UUID are the Unix time in milliseconds.
        Convert.ToInt64(id.ToString("N", CultureInfo.InvariantCulture)[..12], 16).Should().Be(Epoch.ToUnixTimeMilliseconds());
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-001")]
    public void The_generator_needs_a_clock()
    {
        var withoutClock = () => new GuidV7IdGenerator(null!);

        withoutClock.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-001")]
    [Trait("Requirement", "REQ-ARC-003")]
    public void A_generator_that_counts_gives_the_same_identifiers_on_every_run()
    {
        var counting = new CountingIdGenerator();

        counting.NewProductId().Should().Be(TestIds.Product(1));
        counting.NewCategoryId().Should().Be(TestIds.Category(2));
        counting.NewProductId().Should().Be(TestIds.Product(3));
    }

    // Guid.CompareTo does not follow the byte order of a UUID, so the identifiers are compared as the text of their 32 hexadecimal digits, which does.
    private static string Sortable(Guid id) => id.ToString("N", CultureInfo.InvariantCulture);
}
