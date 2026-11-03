using System.Diagnostics.Metrics;

namespace Catalog.Infrastructure.Outbox;

/// <summary>
/// The metrics of the outbox, on the meter <c>Catalog.Outbox</c> (plan section 8.4). A message that reaches the dead-letter state increments <c>catalog.outbox.dead_lettered</c>, which is the signal that the alert of
/// WP1.11 watches. The processor owns one instance, and a test listens to the meter with a <see cref="MeterListener"/>.
/// </summary>
public sealed class OutboxMetrics : IDisposable
{
    /// <summary>The name of the meter.</summary>
    public const string MeterName = "Catalog.Outbox";

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _delivered;
    private readonly Counter<long> _failed;
    private readonly Counter<long> _deadLettered;

    /// <summary>Initializes a new instance of the <see cref="OutboxMetrics"/> class.</summary>
    public OutboxMetrics()
    {
        _delivered = _meter.CreateCounter<long>("catalog.outbox.delivered", "{message}", "Messages that every handler took.");
        _failed = _meter.CreateCounter<long>("catalog.outbox.failed", "{message}", "Deliveries that failed and wait for a new attempt.");
        _deadLettered = _meter.CreateCounter<long>("catalog.outbox.dead_lettered", "{message}", "Messages that failed too often and moved to the dead-letter state.");
    }

    /// <summary>Counts a message that every handler took.</summary>
    /// <param name="eventType">The event type of the message.</param>
    public void Delivered(string eventType) => _delivered.Add(1, new KeyValuePair<string, object?>("event_type", eventType));

    /// <summary>Counts a delivery that failed and will be tried again.</summary>
    /// <param name="eventType">The event type of the message.</param>
    public void Failed(string eventType) => _failed.Add(1, new KeyValuePair<string, object?>("event_type", eventType));

    /// <summary>Counts a message that moved to the dead-letter state.</summary>
    /// <param name="eventType">The event type of the message.</param>
    public void DeadLettered(string eventType) => _deadLettered.Add(1, new KeyValuePair<string, object?>("event_type", eventType));

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();
}
