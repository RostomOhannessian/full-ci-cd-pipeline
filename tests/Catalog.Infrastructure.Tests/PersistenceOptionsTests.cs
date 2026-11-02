using AwesomeAssertions;
using Catalog.Infrastructure.Idempotency;
using Catalog.Infrastructure.Outbox;
using Xunit;

namespace Catalog.Infrastructure.Tests;

/// <summary>REQ-DATA-003 and REQ-DATA-004: the settings of the outbox and of the idempotency store have bounds, and the registration stops the host at startup when a value is outside them (plan section 8.4).</summary>
public sealed class PersistenceOptionsTests
{
    [Fact]
    [Trait("Requirement", "REQ-DATA-003")]
    public void The_default_outbox_settings_are_valid() => new OutboxOptions().Validate().Should().BeEmpty();

    [Fact]
    [Trait("Requirement", "REQ-DATA-004")]
    public void The_default_idempotency_settings_are_valid_and_keep_a_record_for_24_hours()
    {
        var options = new IdempotencyOptions();

        options.Validate().Should().BeEmpty();
        options.Retention.Should().Be(TimeSpan.FromHours(24));
    }

    [Theory]
    [Trait("Requirement", "REQ-DATA-003")]
    [InlineData(0, "BatchSize")]
    [InlineData(1001, "BatchSize")]
    public void A_batch_size_outside_its_bounds_is_refused(int batchSize, string name) =>
        Problems(options => options.BatchSize = batchSize).Should().ContainSingle().Which.Should().Contain(name);

    [Fact]
    [Trait("Requirement", "REQ-DATA-003")]
    public void Every_other_outbox_bound_is_checked()
    {
        Problems(options => options.LeaseDuration = TimeSpan.Zero).Should().ContainSingle().Which.Should().Contain("LeaseDuration");
        Problems(options => options.MaxAttempts = 0).Should().ContainSingle().Which.Should().Contain("MaxAttempts");
        Problems(options => options.BaseBackoff = TimeSpan.Zero).Should().ContainSingle().Which.Should().Contain("BaseBackoff");
        Problems(options => options.MaxBackoff = options.BaseBackoff - TimeSpan.FromMilliseconds(1)).Should().ContainSingle().Which.Should().Contain("MaxBackoff");
        Problems(options => options.JitterRatio = -0.1).Should().ContainSingle().Which.Should().Contain("JitterRatio");
        Problems(options => options.JitterRatio = 1.1).Should().ContainSingle().Which.Should().Contain("JitterRatio");
        Problems(options => options.PollInterval = TimeSpan.Zero).Should().ContainSingle().Which.Should().Contain("PollInterval");
        Problems(options => options.Retention = TimeSpan.Zero).Should().ContainSingle().Which.Should().Contain("Retention");
        Problems(options => options.MaintenanceInterval = TimeSpan.Zero).Should().ContainSingle().Which.Should().Contain("MaintenanceInterval");
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-004")]
    public void Every_idempotency_bound_is_checked()
    {
        var retention = new IdempotencyOptions { Retention = TimeSpan.Zero };
        var timeout = new IdempotencyOptions { InProgressTimeout = TimeSpan.Zero };
        var longTimeout = new IdempotencyOptions { InProgressTimeout = TimeSpan.FromHours(24) };

        retention.Validate().Should().NotBeEmpty().And.Contain(problem => problem.Contains("Retention", StringComparison.Ordinal));
        timeout.Validate().Should().ContainSingle().Which.Should().Contain("InProgressTimeout");
        longTimeout.Validate().Should().ContainSingle().Which.Should().Contain("InProgressTimeout");
    }

    private static IReadOnlyList<string> Problems(Action<OutboxOptions> change)
    {
        var options = new OutboxOptions();
        change(options);

        return options.Validate();
    }
}
