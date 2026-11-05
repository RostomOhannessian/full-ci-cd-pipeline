using System.Diagnostics;
using System.Diagnostics.Metrics;
using Catalog.Application.Results;
using Catalog.Application.Telemetry;

namespace Catalog.CrossCutting.Telemetry;

/// <summary>
/// The adapter of <see cref="IUseCaseTelemetry"/> over an <see cref="ActivitySource"/> and a <see cref="Meter"/>, both named <c>Catalog.UseCases</c> (plan section 8.11, REQ-OBS-001, REQ-OBS-002, ADR-0043). A run
/// of a use case is one span, a child of the request that called it, and one measurement of the duration histogram <c>catalog.usecase.duration</c>. The names of the span, the tags, and the labels are fixed words:
/// the name of the use case, whether it is a command or a query, and how it ended. No value of a request, no identifier, and no text of an exception ever reaches either signal, because an exception adds its type
/// and nothing else. The time comes from the injected clock, so a test measures a duration that it set.
/// </summary>
public sealed class UseCaseTelemetry : IUseCaseTelemetry, IDisposable
{
    /// <summary>The name of the activity source and of the meter.</summary>
    public const string Name = "Catalog.UseCases";

    /// <summary>The name of the histogram of durations, in seconds.</summary>
    public const string DurationInstrument = "catalog.usecase.duration";

    private static readonly double[] DurationBoundaries = [0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.15, 0.2, 0.3, 0.5, 0.75, 1, 2.5, 5, 10];

    private readonly TimeProvider _clock;
    private readonly ActivitySource _source = new(Name);
    private readonly Meter _meter;
    private readonly Histogram<double> _duration;

    /// <summary>Initializes a new instance of the <see cref="UseCaseTelemetry"/> class.</summary>
    /// <param name="clock">The source of the time that a duration is measured with.</param>
    public UseCaseTelemetry(TimeProvider clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));

        // The scope is this object, so a listener can tell the metrics of one instance from those of another instance in the same process, which the tests of several hosts need.
        _meter = new Meter(new MeterOptions(Name) { Scope = this });
        _duration = _meter.CreateHistogram(
            DurationInstrument,
            "s",
            "How long a use case ran, by use case, kind (command or query), and outcome (success, refused, fault, or canceled).",
            tags: null,
            new InstrumentAdvice<double> { HistogramBucketBoundaries = DurationBoundaries });
    }

    /// <inheritdoc />
    public IUseCaseObservation Begin(string useCase, UseCaseKind kind)
    {
        ArgumentException.ThrowIfNullOrEmpty(useCase);

        var activity = _source.StartActivity(useCase, ActivityKind.Internal);
        var label = kind == UseCaseKind.Command ? "command" : "query";
        activity?.SetTag("catalog.use_case", useCase);
        activity?.SetTag("catalog.use_case.kind", label);

        return new Observation(this, activity, useCase, label, _clock.GetTimestamp());
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _source.Dispose();
        _meter.Dispose();
    }

    private sealed class Observation(UseCaseTelemetry owner, Activity? activity, string useCase, string kind, long started) : IUseCaseObservation
    {
        private string _outcome = "unknown";

        public void Succeeded()
        {
            _outcome = "success";
            activity?.SetStatus(ActivityStatusCode.Ok);
        }

        public void Refused(ApplicationError reason)
        {
            ArgumentNullException.ThrowIfNull(reason);

            // A refusal is an expected outcome, so the status of the span stays unset. The kind of the refusal is the name of its type, which is a fixed word.
            _outcome = "refused";
            activity?.SetTag("catalog.refusal", reason.GetType().Name);
        }

        public void Faulted(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);

            if (exception is OperationCanceledException)
            {
                _outcome = "canceled";

                return;
            }

            // The text of an exception can hold a value of the request or a connection string, so the span gets the type and a status, and never the message (REQ-SEC-006).
            _outcome = "fault";
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag("error.type", exception.GetType().FullName);
        }

        public void Dispose()
        {
            var elapsed = owner._clock.GetElapsedTime(started);

            owner._duration.Record(
                elapsed.TotalSeconds,
                new KeyValuePair<string, object?>("use_case", useCase),
                new KeyValuePair<string, object?>("kind", kind),
                new KeyValuePair<string, object?>("outcome", _outcome));

            if (activity is not null)
            {
                activity.SetTag("catalog.outcome", _outcome);
                activity.Dispose();
            }
        }
    }
}
