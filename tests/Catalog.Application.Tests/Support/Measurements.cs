using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Catalog.CrossCutting.Caching;

namespace Catalog.Application.Tests.Support;

/// <summary>One measurement of an instrument, with its tags.</summary>
/// <param name="Instrument">The name of the instrument.</param>
/// <param name="Value">The value.</param>
/// <param name="Tags">The tags of the measurement.</param>
internal sealed record Measurement(string Instrument, long Value, IReadOnlyDictionary<string, string?> Tags);

/// <summary>Collects the measurements of the cache metrics of one node, so a test reads what the node reported and what it left out. Another test project links this file.</summary>
internal sealed class Measurements : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly ConcurrentQueue<Measurement> _measurements = new();

    public Measurements(CacheMetrics metrics)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (ReferenceEquals(instrument.Meter.Scope, metrics))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>(Record);
        _listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) => Record(instrument, value, tags, null));
        _listener.Start();
    }

    /// <summary>Gets every measurement so far, in the order of the reports.</summary>
    public IReadOnlyList<Measurement> All => [.. _measurements];

    /// <summary>Adds up the measurements of a counter that have all the given tags.</summary>
    /// <param name="instrument">The name of the counter.</param>
    /// <param name="tags">The tags that a measurement must have.</param>
    public long Total(string instrument, params (string Key, string Value)[] tags) =>
        _measurements
            .Where(measurement => measurement.Instrument == instrument && tags.All(tag => measurement.Tags.TryGetValue(tag.Key, out var value) && value == tag.Value))
            .Sum(measurement => measurement.Value);

    /// <summary>Reads the value of a gauge now.</summary>
    /// <param name="instrument">The name of the gauge.</param>
    public long Gauge(string instrument)
    {
        var before = _measurements.Count;
        _listener.RecordObservableInstruments();

        return _measurements.Skip(before).Last(measurement => measurement.Instrument == instrument).Value;
    }

    public void Dispose() => _listener.Dispose();

    private void Record(Instrument instrument, long value, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    {
        _ = state;
        _measurements.Enqueue(new Measurement(instrument.Name, value, tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value?.ToString())));
    }
}
