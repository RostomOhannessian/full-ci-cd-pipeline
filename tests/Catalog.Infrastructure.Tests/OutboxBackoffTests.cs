using AwesomeAssertions;
using Catalog.Infrastructure.Outbox;
using Xunit;

namespace Catalog.Infrastructure.Tests;

/// <summary>REQ-DATA-003: the delay after a failed delivery is an exponential backoff with jitter that never passes the maximum (plan section 8.4). The function is pure, so these tests need no clock, no random source, and no database.</summary>
public sealed class OutboxBackoffTests
{
    private static readonly OutboxOptions Defaults = new() { BaseBackoff = TimeSpan.FromSeconds(2), MaxBackoff = TimeSpan.FromMinutes(5), JitterRatio = 0.2 };

    [Theory]
    [Trait("Requirement", "REQ-DATA-003")]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    [InlineData(8, 256)]
    [InlineData(9, 300)]
    [InlineData(31, 300)]
    [InlineData(int.MaxValue, 300)]
    public void The_delay_doubles_with_each_attempt_and_stops_at_the_maximum(int attempt, double seconds) =>
        OutboxBackoff.Delay(attempt, Defaults, 0.5).Should().Be(TimeSpan.FromSeconds(seconds));

    [Theory]
    [Trait("Requirement", "REQ-DATA-003")]
    [InlineData(0.0, 1.6)]
    [InlineData(0.25, 1.8)]
    [InlineData(0.5, 2.0)]
    [InlineData(0.75, 2.2)]
    [InlineData(1.0, 2.4)]
    public void Jitter_moves_the_delay_by_up_to_its_ratio_in_both_directions(double unitRandom, double seconds) =>
        OutboxBackoff.Delay(1, Defaults, unitRandom).Should().BeCloseTo(TimeSpan.FromSeconds(seconds), TimeSpan.FromMilliseconds(1));

    [Theory]
    [Trait("Requirement", "REQ-DATA-003")]
    [InlineData(0.0, 240)]
    [InlineData(1.0, 300)]
    public void Jitter_never_pushes_the_delay_past_the_maximum(double unitRandom, double seconds) =>
        OutboxBackoff.Delay(9, Defaults, unitRandom).Should().Be(TimeSpan.FromSeconds(seconds));

    [Theory]
    [Trait("Requirement", "REQ-DATA-003")]
    [InlineData(-5.0, 1.6)]
    [InlineData(5.0, 2.4)]
    public void A_random_number_outside_the_range_is_clamped(double unitRandom, double seconds) =>
        OutboxBackoff.Delay(1, Defaults, unitRandom).Should().BeCloseTo(TimeSpan.FromSeconds(seconds), TimeSpan.FromMilliseconds(1));

    [Fact]
    [Trait("Requirement", "REQ-DATA-003")]
    public void Without_jitter_every_random_number_gives_the_same_delay()
    {
        var options = new OutboxOptions { JitterRatio = 0 };

        OutboxBackoff.Delay(3, options, 0).Should().Be(OutboxBackoff.Delay(3, options, 0.9));
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-003")]
    public void An_attempt_below_one_and_a_missing_option_are_refused()
    {
        var attempt = () => OutboxBackoff.Delay(0, Defaults, 0.5);
        var options = () => OutboxBackoff.Delay(1, null!, 0.5);

        attempt.Should().Throw<ArgumentOutOfRangeException>();
        options.Should().Throw<ArgumentNullException>();
    }
}
