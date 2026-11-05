using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using AwesomeAssertions;
using Catalog.Application.Results;
using Catalog.Application.Telemetry;
using Catalog.Application.Tests.Support;
using Catalog.CrossCutting.Decorators;
using Catalog.CrossCutting.Telemetry;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Catalog.Application.Tests;

/// <summary>
/// REQ-OBS-001 and REQ-OBS-002: the adapter of the telemetry port reports each run of a use case as one span and one measurement of a duration, with fixed words for the names, the tags, and the labels, and it never lets
/// the text of an exception or a value of a request reach either (REQ-SEC-006). The clock is a fake one, so a duration is a number that the test set. The listeners see every source and every meter of the process, so each test
/// uses a use case name of its own and reads the meter of its own instance.
/// </summary>
public sealed class UseCaseTelemetryTests
{
    private const string Sentinel = "Pw-Secret-usecase";

    private static Probe Listen(UseCaseTelemetry telemetry, string useCase, bool spans = true) => new(telemetry, useCase, spans);

    [Fact]
    [Trait("Requirement", "REQ-OBS-001")]
    [Trait("Requirement", "REQ-OBS-002")]
    public void A_use_case_that_succeeds_is_a_span_with_an_ok_status_and_a_measurement_of_its_duration()
    {
        var clock = new FakeTimeProvider();
        using var telemetry = new UseCaseTelemetry(clock);
        using var probe = Listen(telemetry, "Test.Succeeds");

        using (var observation = telemetry.Begin("Test.Succeeds", UseCaseKind.Command))
        {
            clock.Advance(TimeSpan.FromMilliseconds(250));
            observation.Succeeded();
        }

        var span = probe.Spans.Should().ContainSingle().Subject;
        span.OperationName.Should().Be("Test.Succeeds");
        span.Kind.Should().Be(ActivityKind.Internal);
        span.Status.Should().Be(ActivityStatusCode.Ok);
        span.TagObjects.ToDictionary(tag => tag.Key, tag => tag.Value?.ToString()).Should().BeEquivalentTo(new Dictionary<string, string?>
        {
            ["catalog.use_case"] = "Test.Succeeds",
            ["catalog.use_case.kind"] = "command",
            ["catalog.outcome"] = "success",
        });

        var duration = probe.Durations.Should().ContainSingle().Subject;
        duration.Value.Should().BeApproximately(0.25, 1e-9);
        duration.Tags.Should().BeEquivalentTo(new Dictionary<string, string?> { ["use_case"] = "Test.Succeeds", ["kind"] = "command", ["outcome"] = "success" });
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-001")]
    [Trait("Requirement", "REQ-OBS-002")]
    public void A_refusal_names_its_kind_and_leaves_the_status_unset_because_it_is_an_expected_outcome()
    {
        var clock = new FakeTimeProvider();
        using var telemetry = new UseCaseTelemetry(clock);
        using var probe = Listen(telemetry, "Test.Refuses");

        using (var observation = telemetry.Begin("Test.Refuses", UseCaseKind.Query))
        {
            observation.Refused(new NotFound("product"));
        }

        var span = probe.Spans.Should().ContainSingle().Subject;
        span.Status.Should().Be(ActivityStatusCode.Unset);
        span.GetTagItem("catalog.outcome").Should().Be("refused");
        span.GetTagItem("catalog.refusal").Should().Be("NotFound");
        span.GetTagItem("catalog.use_case.kind").Should().Be("query");
        probe.Durations.Should().ContainSingle().Which.Tags["outcome"].Should().Be("refused");
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-001")]
    [Trait("Requirement", "REQ-SEC-006")]
    public void A_fault_is_an_error_span_that_holds_the_type_of_the_exception_and_never_its_text()
    {
        using var telemetry = new UseCaseTelemetry(new FakeTimeProvider());
        using var probe = Listen(telemetry, "Test.Faults");

        using (var observation = telemetry.Begin("Test.Faults", UseCaseKind.Command))
        {
            observation.Faulted(new InvalidOperationException($"Server=db;Password={Sentinel}"));
        }

        var span = probe.Spans.Should().ContainSingle().Subject;
        span.Status.Should().Be(ActivityStatusCode.Error);
        span.GetTagItem("catalog.outcome").Should().Be("fault");
        span.GetTagItem("error.type").Should().Be("System.InvalidOperationException");
        span.StatusDescription.Should().BeNullOrEmpty();
        span.Events.Should().BeEmpty();
        string.Join(' ', span.TagObjects.Select(tag => $"{tag.Key}={tag.Value}")).Should().NotContain(Sentinel);
        probe.Durations.Should().ContainSingle().Which.Tags["outcome"].Should().Be("fault");
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-001")]
    [Trait("Requirement", "REQ-OBS-002")]
    public void A_cancellation_is_not_a_fault_so_a_client_that_goes_away_raises_no_error()
    {
        using var telemetry = new UseCaseTelemetry(new FakeTimeProvider());
        using var probe = Listen(telemetry, "Test.Cancels");

        using (var observation = telemetry.Begin("Test.Cancels", UseCaseKind.Query))
        {
            observation.Faulted(new OperationCanceledException());
        }

        var span = probe.Spans.Should().ContainSingle().Subject;
        span.Status.Should().Be(ActivityStatusCode.Unset);
        span.GetTagItem("catalog.outcome").Should().Be("canceled");
        probe.Durations.Should().ContainSingle().Which.Tags["outcome"].Should().Be("canceled");
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-001")]
    public void The_span_of_a_use_case_is_a_child_of_the_span_that_called_it()
    {
        using var telemetry = new UseCaseTelemetry(new FakeTimeProvider());
        using var probe = Listen(telemetry, "Test.Child");
        using var outer = new ActivitySource("Test.Outer");
        using var outerListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Test.Outer",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(outerListener);

        using var request = outer.StartActivity("request")!;
        using (var observation = telemetry.Begin("Test.Child", UseCaseKind.Query))
        {
            observation.Succeeded();
        }

        var span = probe.Spans.Should().ContainSingle().Subject;
        span.ParentId.Should().Be(request.Id);
        span.TraceId.Should().Be(request.TraceId);
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-002")]
    public void A_use_case_is_measured_whether_or_not_a_span_was_recorded()
    {
        using var telemetry = new UseCaseTelemetry(new FakeTimeProvider());
        using var probe = Listen(telemetry, "Test.NoSpan", spans: false);

        using (var observation = telemetry.Begin("Test.NoSpan", UseCaseKind.Command))
        {
            observation.Succeeded();
        }

        probe.Spans.Should().BeEmpty();
        probe.Durations.Should().ContainSingle();
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-001")]
    [Trait("Requirement", "REQ-OBS-002")]
    public async Task The_telemetry_decorator_reports_one_outcome_for_each_run_through_the_real_adapter()
    {
        using var telemetry = new UseCaseTelemetry(new FakeTimeProvider());
        using var probe = Listen(telemetry, nameof(SampleCommand));
        var journal = new Journal();
        var refusal = Result.Failure<SampleView>(new NotFound("product"));

        var succeeds = new TelemetryCommandDecorator<SampleCommand, SampleView>(new FakeCommandHandler(journal), telemetry);
        var refuses = new TelemetryCommandDecorator<SampleCommand, SampleView>(new FakeCommandHandler(journal, _ => refusal), telemetry);
        var faults = new TelemetryCommandDecorator<SampleCommand, SampleView>(new FakeCommandHandler(journal, _ => throw new TimeoutException($"Password={Sentinel}")), telemetry);

        await succeeds.HandleAsync(new SampleCommand("Keyboard"), CancellationToken.None);
        await refuses.HandleAsync(new SampleCommand("Keyboard"), CancellationToken.None);
        var act = () => faults.HandleAsync(new SampleCommand("Keyboard"), CancellationToken.None);
        await act.Should().ThrowAsync<TimeoutException>();

        probe.Spans.Select(span => span.GetTagItem("catalog.outcome")).Should().Equal("success", "refused", "fault");
        probe.Durations.Select(duration => duration.Tags["outcome"]).Should().Equal("success", "refused", "fault");
        probe.Durations.SelectMany(duration => duration.Tags.Values).Should().NotContain(value => value != null && value.Contains(Sentinel, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-002")]
    public void The_adapter_needs_a_clock_and_a_name()
    {
        var noClock = () => new UseCaseTelemetry(null!);

        noClock.Should().Throw<ArgumentNullException>();

        using var telemetry = new UseCaseTelemetry(new FakeTimeProvider());
        var noName = () => telemetry.Begin(string.Empty, UseCaseKind.Command);

        noName.Should().Throw<ArgumentException>();
    }

    private sealed record Duration(double Value, IReadOnlyDictionary<string, string?> Tags);

    // Listens to the spans of one use case name and to the meter of one adapter, and keeps what it saw.
    private sealed class Probe : IDisposable
    {
        private readonly ActivityListener? _activities;
        private readonly MeterListener _meters = new();
        private readonly ConcurrentQueue<Activity> _spans = new();
        private readonly ConcurrentQueue<Duration> _durations = new();

        public Probe(UseCaseTelemetry telemetry, string useCase, bool spans = true)
        {
            if (spans)
            {
                _activities = new ActivityListener
                {
                    ShouldListenTo = source => source.Name == UseCaseTelemetry.Name,
                    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                    ActivityStopped = activity =>
                    {
                        if (Equals(activity.GetTagItem("catalog.use_case"), useCase))
                        {
                            _spans.Enqueue(activity);
                        }
                    },
                };
                ActivitySource.AddActivityListener(_activities);
            }

            _meters.InstrumentPublished = (instrument, listener) =>
            {
                if (ReferenceEquals(instrument.Meter.Scope, telemetry) && instrument.Name == UseCaseTelemetry.DurationInstrument)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _meters.SetMeasurementEventCallback<double>((_, value, tags, _) =>
                _durations.Enqueue(new Duration(value, tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value?.ToString()))));
            _meters.Start();
        }

        public IReadOnlyList<Activity> Spans => [.. _spans];

        public IReadOnlyList<Duration> Durations => [.. _durations];

        public void Dispose()
        {
            _activities?.Dispose();
            _meters.Dispose();
        }
    }
}
