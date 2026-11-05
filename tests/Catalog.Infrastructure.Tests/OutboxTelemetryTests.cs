using System.Diagnostics;
using AwesomeAssertions;
using Catalog.Domain.Tests.Support;
using Catalog.Infrastructure.Outbox;
using Catalog.Infrastructure.Tests.Support;
using Xunit;

namespace Catalog.Infrastructure.Tests;

/// <summary>
/// REQ-OBS-001, REQ-OBS-002, and REQ-OBS-003: the outbox reports a pass as spans with their parent and child links, reports its backlog, the age of its oldest message, its dead letters, the age of a delivery, and the time of
/// each handler as metrics, and never lets the text of an exception or a value of a message reach either (plan sections 8.4 and 8.11, ADR-0035, ADR-0043). The clock is a fake one, so every age and every duration is a
/// number that the test set. A listener sees every source and every meter of the process, so a probe keeps only the source and the meter of the rig of its own test.
/// </summary>
public sealed class OutboxTelemetryTests(CatalogDatabaseFixture database) : IClassFixture<CatalogDatabaseFixture>
{
    private const string Sentinel = "Pw-Secret-outbox";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Requirement", "REQ-OBS-001")]
    public async Task A_pass_is_one_span_with_a_child_for_each_delivery_and_a_child_of_that_for_each_handler()
    {
        await database.ResetOutboxAsync();
        var first = new RecordingHandler("first");
        var second = new RecordingHandler("second");
        using var rig = new ProcessorRig(database, Make.Clock(), null, first, second);
        using var probe = new TelemetryProbe(rig.Metrics, rig.Metrics.Source);
        await rig.SeedMessagesAsync(2);

        await rig.Processor.ProcessBatchAsync(Token);

        var spans = probe.Spans;
        var batch = spans.Should().ContainSingle(span => span.OperationName == "outbox.process_batch").Subject;
        var deliveries = spans.Where(span => span.OperationName == "outbox.deliver").ToList();
        var handlers = spans.Where(span => span.OperationName == "outbox.handle").ToList();
        batch.ParentId.Should().BeNull();
        batch.GetTagItem("catalog.outbox.batch_size").Should().Be(2);
        deliveries.Should().HaveCount(2).And.OnlyContain(span => span.ParentId == batch.Id && (string?)span.GetTagItem("catalog.outcome") == "delivered" && (string?)span.GetTagItem("catalog.outbox.event_type") == "ProductCreated");
        handlers.Should().HaveCount(4);
        handlers.Select(span => (string?)span.GetTagItem("catalog.outbox.handler")).Should().BeEquivalentTo("first", "first", "second", "second");
        handlers.Should().OnlyContain(span => deliveries.Any(delivery => delivery.Id == span.ParentId));
        spans.Select(span => span.TraceId).Distinct().Should().ContainSingle();
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-001")]
    public async Task A_pass_that_finds_nothing_to_deliver_leaves_no_span()
    {
        await database.ResetOutboxAsync();
        using var rig = new ProcessorRig(database, Make.Clock(), null, new RecordingHandler());
        using var probe = new TelemetryProbe(rig.Metrics, rig.Metrics.Source);

        var result = await rig.Processor.ProcessBatchAsync(Token);

        result.Claimed.Should().Be(0);
        probe.Spans.Should().BeEmpty();
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-001")]
    [Trait("Requirement", "REQ-SEC-006")]
    public async Task A_handler_that_fails_is_an_error_span_that_holds_the_type_of_the_exception_and_never_its_text()
    {
        await database.ResetOutboxAsync();
        var handler = new RecordingHandler { Behavior = (_, _) => throw new InvalidOperationException($"Server=db;Password={Sentinel}") };
        using var rig = new ProcessorRig(database, Make.Clock(), null, handler);
        using var probe = new TelemetryProbe(rig.Metrics, rig.Metrics.Source);
        await rig.SeedMessagesAsync(1);

        var result = await rig.Processor.ProcessBatchAsync(Token);

        result.Failed.Should().Be(1);
        var delivery = probe.Spans.Single(span => span.OperationName == "outbox.deliver");
        var handling = probe.Spans.Single(span => span.OperationName == "outbox.handle");
        foreach (var span in new[] { delivery, handling })
        {
            span.Status.Should().Be(ActivityStatusCode.Error);
            span.GetTagItem("error.type").Should().Be("System.InvalidOperationException");
            span.Events.Should().BeEmpty();
            string.Join(' ', span.TagObjects.Select(tag => $"{tag.Key}={tag.Value}")).Should().NotContain(Sentinel);
        }

        delivery.GetTagItem("catalog.outcome").Should().Be("failed");
        probe.Total("catalog.outbox.failed", ("event_type", "ProductCreated")).Should().Be(1);
        probe.Of("catalog.outbox.handler.duration").Should().ContainSingle().Which.Tags.Should().BeEquivalentTo(new Dictionary<string, string?> { ["handler"] = "recorder", ["outcome"] = "failed" });
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-002")]
    public async Task The_delivery_age_and_the_duration_of_a_handler_are_measured_with_the_clock()
    {
        await database.ResetOutboxAsync();
        var clock = Make.Clock();
        var handler = new RecordingHandler { Behavior = (_, _) => { clock.Advance(TimeSpan.FromMilliseconds(250)); return Task.CompletedTask; } };
        using var rig = new ProcessorRig(database, clock, null, handler);
        using var probe = new TelemetryProbe(rig.Metrics);
        await rig.SeedMessagesAsync(1);

        await rig.Processor.ProcessBatchAsync(Token);

        // The seed moves the clock by one second after it writes the row, and the handler moves it by 250 milliseconds, so the message was a second and a quarter old when it was delivered.
        var age = probe.Of("catalog.outbox.delivery_age").Should().ContainSingle().Subject;
        age.Value.Should().BeApproximately(1.25, 1e-9);
        age.Tags.Should().BeEquivalentTo(new Dictionary<string, string?> { ["event_type"] = "ProductCreated" });
        var duration = probe.Of("catalog.outbox.handler.duration").Should().ContainSingle().Subject;
        duration.Value.Should().BeApproximately(0.25, 1e-9);
        duration.Tags.Should().BeEquivalentTo(new Dictionary<string, string?> { ["handler"] = "recorder", ["outcome"] = "ok" });
        probe.Total("catalog.outbox.delivered", ("event_type", "ProductCreated")).Should().Be(1);
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-002")]
    public async Task The_gauges_report_nothing_before_the_first_reading_and_then_what_the_outbox_holds()
    {
        await database.ResetOutboxAsync();
        using var rig = new ProcessorRig(database, Make.Clock(), null, new RecordingHandler());
        using var probe = new TelemetryProbe(rig.Metrics);

        probe.Gauge("catalog.outbox.backlog").Should().BeNull("a series that does not exist must not be read as zero");

        await rig.SeedMessagesAsync(3);
        var observed = await rig.Processor.ObserveBacklogAsync(Token);

        observed.Should().BeTrue();
        probe.Gauge("catalog.outbox.backlog").Should().Be(3);
        probe.Gauge("catalog.outbox.dead_letters").Should().Be(0);

        // The oldest message was due when the first row was written, which is three seconds before the clock now.
        probe.Gauge("catalog.outbox.oldest_pending_age").Should().BeApproximately(3, 1e-9);
        probe.Gauge("catalog.outbox.observation_age").Should().Be(0);
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-002")]
    public async Task The_age_of_the_oldest_message_and_of_the_reading_grow_with_the_clock_and_the_reading_is_throttled()
    {
        await database.ResetOutboxAsync();
        var clock = Make.Clock();
        using var rig = new ProcessorRig(database, clock, new OutboxOptions { JitterRatio = 0, ObservationInterval = TimeSpan.FromSeconds(30) }, new RecordingHandler());
        using var probe = new TelemetryProbe(rig.Metrics);
        await rig.SeedMessagesAsync(1);
        await rig.Processor.ObserveBacklogAsync(Token);

        clock.Advance(TimeSpan.FromSeconds(10));

        (await rig.Processor.ObserveBacklogAsync(Token)).Should().BeFalse("the last reading is fresher than the interval");
        probe.Gauge("catalog.outbox.oldest_pending_age").Should().BeApproximately(11, 1e-9);
        probe.Gauge("catalog.outbox.observation_age").Should().BeApproximately(10, 1e-9);

        clock.Advance(TimeSpan.FromSeconds(20));

        (await rig.Processor.ObserveBacklogAsync(Token)).Should().BeTrue();
        probe.Gauge("catalog.outbox.observation_age").Should().Be(0);
        probe.Gauge("catalog.outbox.oldest_pending_age").Should().BeApproximately(31, 1e-9);
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-002")]
    public async Task A_message_that_waits_for_a_backoff_is_pending_but_not_overdue()
    {
        await database.ResetOutboxAsync();
        var clock = Make.Clock();
        var handler = new RecordingHandler { Behavior = (_, _) => throw new TimeoutException("later") };
        using var rig = new ProcessorRig(database, clock, new OutboxOptions { JitterRatio = 0, BaseBackoff = TimeSpan.FromMinutes(5), MaxBackoff = TimeSpan.FromMinutes(5) }, handler);
        using var probe = new TelemetryProbe(rig.Metrics);
        await rig.SeedMessagesAsync(1);
        await rig.Processor.ProcessBatchAsync(Token);

        await rig.Processor.ObserveBacklogAsync(Token);

        probe.Gauge("catalog.outbox.backlog").Should().Be(1);
        probe.Gauge("catalog.outbox.oldest_pending_age").Should().Be(0, "the message is not due for five minutes");
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-002")]
    [Trait("Requirement", "REQ-DATA-003")]
    public async Task A_dead_letter_message_is_counted_and_shown_by_the_gauge_at_once_and_again_by_the_next_reading()
    {
        await database.ResetOutboxAsync();
        var clock = Make.Clock();
        var handler = new RecordingHandler { Behavior = (_, _) => throw new TimeoutException("never") };
        using var rig = new ProcessorRig(database, clock, new OutboxOptions { JitterRatio = 0, MaxAttempts = 1, ObservationInterval = TimeSpan.FromSeconds(30) }, handler);
        using var probe = new TelemetryProbe(rig.Metrics);
        await rig.SeedMessagesAsync(1);
        await rig.Processor.ObserveBacklogAsync(Token);
        probe.Gauge("catalog.outbox.dead_letters").Should().Be(0);

        var result = await rig.Processor.ProcessBatchAsync(Token);

        result.DeadLettered.Should().Be(1);
        probe.Total("catalog.outbox.dead_lettered", ("event_type", "ProductCreated")).Should().Be(1);
        probe.Gauge("catalog.outbox.dead_letters").Should().Be(1);

        clock.Advance(TimeSpan.FromSeconds(31));
        await rig.Processor.ObserveBacklogAsync(Token);

        probe.Gauge("catalog.outbox.dead_letters").Should().Be(1);
        probe.Gauge("catalog.outbox.backlog").Should().Be(0);
        probe.Gauge("catalog.outbox.oldest_pending_age").Should().Be(0);
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-002")]
    public void The_interval_of_the_reading_must_be_positive_and_a_gauge_needs_a_reading_to_observe()
    {
        new OutboxOptions { ObservationInterval = TimeSpan.Zero }.Validate().Should().ContainSingle().Which.Should().Contain("ObservationInterval");
        new OutboxOptions().Validate().Should().BeEmpty();

        using var metrics = new OutboxMetrics();
        var none = () => metrics.Observe(null!);

        none.Should().Throw<ArgumentNullException>();
    }
}
