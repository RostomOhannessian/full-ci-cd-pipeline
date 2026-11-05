using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Catalog.Infrastructure.Tests.Support;

/// <summary>One measurement of an instrument, with its tags.</summary>
/// <param name="Instrument">The name of the instrument.</param>
/// <param name="Value">The value.</param>
/// <param name="Tags">The tags of the measurement.</param>
internal sealed record Sample(string Instrument, double Value, IReadOnlyDictionary<string, string?> Tags);

/// <summary>
/// Collects the spans of one activity source and the measurements of one meter, so a test reads what a component reported and what it left out. A listener sees every source and every meter of the process, and test classes
/// run in parallel, so the probe keeps only the spans of the source object that the test names and only the measurements of the meters whose scope is the object that the test names.
/// </summary>
internal sealed class TelemetryProbe : IDisposable
{
    private readonly ActivityListener? _activities;
    private readonly MeterListener _meters = new();
    private readonly ConcurrentQueue<Activity> _spans = new();
    private readonly ConcurrentQueue<Sample> _samples = new();

    public TelemetryProbe(object? meterScope, ActivitySource? source = null)
    {
        if (source is not null)
        {
            _activities = new ActivityListener
            {
                ShouldListenTo = candidate => ReferenceEquals(candidate, source),
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = _spans.Enqueue,
            };
            ActivitySource.AddActivityListener(_activities);
        }

        _meters.InstrumentPublished = (instrument, listener) =>
        {
            if (meterScope is not null && ReferenceEquals(instrument.Meter.Scope, meterScope))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _meters.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
        _meters.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
        _meters.Start();
    }

    /// <summary>Gets the spans that ended, in the order that they ended.</summary>
    public IReadOnlyList<Activity> Spans => [.. _spans];

    /// <summary>Gets the measurements of one instrument, in the order of the reports.</summary>
    /// <param name="instrument">The name of the instrument.</param>
    public IReadOnlyList<Sample> Of(string instrument) => [.. _samples.Where(sample => sample.Instrument == instrument)];

    /// <summary>Adds up the measurements of an instrument that have all the given tags.</summary>
    /// <param name="instrument">The name of the instrument.</param>
    /// <param name="tags">The tags that a measurement must have.</param>
    public double Total(string instrument, params (string Key, string Value)[] tags) =>
        _samples
            .Where(sample => sample.Instrument == instrument && tags.All(tag => sample.Tags.TryGetValue(tag.Key, out var value) && value == tag.Value))
            .Sum(sample => sample.Value);

    /// <summary>Reads a gauge now, and returns null when the gauge reports nothing.</summary>
    /// <param name="instrument">The name of the gauge.</param>
    public double? Gauge(string instrument)
    {
        var before = _samples.Count;
        _meters.RecordObservableInstruments();

        return _samples.Skip(before).Where(sample => sample.Instrument == instrument).Select(sample => (double?)sample.Value).LastOrDefault();
    }

    public void Dispose()
    {
        _activities?.Dispose();
        _meters.Dispose();
    }

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags) =>
        _samples.Enqueue(new Sample(instrument.Name, value, tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value?.ToString())));
}
