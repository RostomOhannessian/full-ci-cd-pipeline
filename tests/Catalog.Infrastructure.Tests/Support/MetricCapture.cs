using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Catalog.Infrastructure.Outbox;

namespace Catalog.Infrastructure.Tests.Support;

/// <summary>
/// Adds up the measurements of the outbox meter for one event type. A meter listener sees every instrument of the process, and test classes run in parallel, so each test filters by an event type that no other test uses.
/// </summary>
internal sealed class MetricCapture : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly ConcurrentDictionary<string, long> _totals = new();

    public MetricCapture(string eventType)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == OutboxMetrics.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "event_type" && Equals(tag.Value, eventType))
                {
                    _totals.AddOrUpdate(instrument.Name, measurement, (_, total) => total + measurement);
                }
            }
        });

        _listener.Start();
    }

    public long Total(string instrument) => _totals.GetValueOrDefault(instrument);

    public void Dispose() => _listener.Dispose();
}
