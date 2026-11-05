using System.Diagnostics;
using System.Diagnostics.Metrics;
using Catalog.Application.Events;

namespace Catalog.Infrastructure.Outbox;

/// <summary>
/// What the outbox held when it was last looked at: how many messages are pending, how many are in the dead-letter state, and since when the oldest pending message has been due. A pass of the processor reads it with
/// one query (<see cref="OutboxProcessor.ObserveBacklogAsync"/>), and the gauges of <see cref="OutboxMetrics"/> report it.
/// </summary>
/// <param name="Pending">The messages that wait for a delivery, including the ones that wait for a backoff.</param>
/// <param name="DeadLetters">The messages that moved to the dead-letter state and need a person.</param>
/// <param name="OldestDueAt">When the oldest pending message became due, or null when no message is pending.</param>
/// <param name="ObservedAt">When the numbers were read.</param>
public sealed record OutboxBacklog(long Pending, long DeadLetters, DateTimeOffset? OldestDueAt, DateTimeOffset ObservedAt);

/// <summary>
/// The metrics and the activity source of the outbox, both named <c>Catalog.Outbox</c> (plan sections 8.4 and 8.11, ADR-0035, ADR-0043). A message that reaches the dead-letter state increments
/// <c>catalog.outbox.dead_lettered</c>, and the gauges (<c>catalog.outbox.backlog</c>, <c>catalog.outbox.oldest_pending_age</c>, <c>catalog.outbox.dead_letters</c>, and <c>catalog.outbox.observation_age</c>) report the
/// state that the alerts of the Compose stack watch. A label holds a fixed word, the stable name of a handler, or the event type of a message, and never an identifier. The processor owns one instance, and a test listens to
/// the meter with a <see cref="MeterListener"/> and to the source with an <see cref="ActivityListener"/>.
/// </summary>
public sealed class OutboxMetrics : IDisposable
{
    /// <summary>The name of the meter and of the activity source.</summary>
    public const string MeterName = "Catalog.Outbox";

    private static readonly double[] DeliveryAgeBoundaries = [0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60, 300];
    private static readonly double[] HandlerBoundaries = [0.001, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10];

    private readonly TimeProvider _clock;
    private readonly Meter _meter;
    private readonly Counter<long> _delivered;
    private readonly Counter<long> _failed;
    private readonly Counter<long> _deadLettered;
    private readonly Counter<long> _immediate;
    private readonly Histogram<double> _deliveryAge;
    private readonly Histogram<double> _handlerDuration;
    private OutboxBacklog? _backlog;

    /// <summary>Initializes a new instance of the <see cref="OutboxMetrics"/> class.</summary>
    /// <param name="clock">The source of the time that the age of the oldest message is measured with, or null for the system clock.</param>
    public OutboxMetrics(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;

        // The scope is this object, so a listener can tell the metrics of one processor from those of another in the same process, which the tests of several hosts need.
        _meter = new Meter(new MeterOptions(MeterName) { Scope = this });
        Source = new ActivitySource(MeterName);
        _delivered = _meter.CreateCounter<long>("catalog.outbox.delivered", "{message}", "Messages that every handler took.");
        _failed = _meter.CreateCounter<long>("catalog.outbox.failed", "{message}", "Deliveries that failed and wait for a new attempt.");
        _deadLettered = _meter.CreateCounter<long>("catalog.outbox.dead_lettered", "{message}", "Messages that failed too often and moved to the dead-letter state.");
        _immediate = _meter.CreateCounter<long>("catalog.outbox.immediate", "{delivery}", "Deliveries that the writing node made in process right after a commit, by handler and outcome.");
        _deliveryAge = _meter.CreateHistogram(
            "catalog.outbox.delivery_age",
            "s",
            "How long a message waited from the commit of its event to the delivery by the processor, by event type.",
            tags: null,
            new InstrumentAdvice<double> { HistogramBucketBoundaries = DeliveryAgeBoundaries });
        _handlerDuration = _meter.CreateHistogram(
            "catalog.outbox.handler.duration",
            "s",
            "How long a handler took for one message, by handler and outcome (ok or failed).",
            tags: null,
            new InstrumentAdvice<double> { HistogramBucketBoundaries = HandlerBoundaries });
        _meter.CreateObservableGauge("catalog.outbox.backlog", () => Read(backlog => backlog.Pending), "{message}", "Messages that wait for a delivery, including the ones that wait for a backoff.");
        _meter.CreateObservableGauge("catalog.outbox.dead_letters", () => Read(backlog => backlog.DeadLetters), "{message}", "Messages in the dead-letter state, which need a person.");
        _meter.CreateObservableGauge("catalog.outbox.oldest_pending_age", () => Read(OldestPendingAge), "s", "How long the oldest pending message has been due, which is 0 when none is due. It grows while the processor is stuck.");
        _meter.CreateObservableGauge("catalog.outbox.observation_age", () => Read(backlog => (_clock.GetUtcNow() - backlog.ObservedAt).TotalSeconds), "s", "How long ago the numbers of the other gauges were read. It grows while the database cannot be read.");
    }

    /// <summary>Gets the activity source of the outbox, which the processor and the dispatcher start their spans from.</summary>
    public ActivitySource Source { get; }

    /// <summary>Counts one delivery that the writing node made in process right after a commit.</summary>
    /// <param name="handler">The stable name of the handler.</param>
    /// <param name="delivered">Whether the handler took the message. A failure leaves the message to the outbox processor.</param>
    public void Immediate(string handler, bool delivered) =>
        _immediate.Add(1, new KeyValuePair<string, object?>("handler", handler), new KeyValuePair<string, object?>("outcome", delivered ? "delivered" : "failed"));

    /// <summary>Counts a message that every handler took.</summary>
    /// <param name="eventType">The event type of the message.</param>
    public void Delivered(string eventType) => _delivered.Add(1, new KeyValuePair<string, object?>("event_type", eventType));

    /// <summary>Records how long a delivered message waited, which is the figure of the outbox service level (plan section 8.11).</summary>
    /// <param name="eventType">The event type of the message.</param>
    /// <param name="age">The time from the commit of the event to the delivery.</param>
    public void DeliveryAge(string eventType, TimeSpan age) =>
        _deliveryAge.Record(Math.Max(0, age.TotalSeconds), new KeyValuePair<string, object?>("event_type", eventType));

    /// <summary>Records how long a handler took for one message.</summary>
    /// <param name="handler">The stable name of the handler.</param>
    /// <param name="elapsed">The time that the handler took.</param>
    /// <param name="succeeded">Whether the handler returned without a fault.</param>
    public void HandlerRan(string handler, TimeSpan elapsed, bool succeeded) =>
        _handlerDuration.Record(elapsed.TotalSeconds, new KeyValuePair<string, object?>("handler", handler), new KeyValuePair<string, object?>("outcome", succeeded ? "ok" : "failed"));

    /// <summary>
    /// Gives a message to a handler inside a span named <c>outbox.handle</c> and records how long the handler took. The span is a child of whatever runs the delivery: the span of the pass of the processor, or the span of
    /// the request when the writing node delivers in process. A fault is recorded by its type, never by its text, and thrown again. A cancellation that the caller asked for is thrown again and recorded as nothing.
    /// </summary>
    /// <param name="handler">The handler.</param>
    /// <param name="message">The message.</param>
    /// <param name="cancellationToken">A token that stops the handler.</param>
    public async Task ObserveHandlerAsync(IOutboxMessageHandler handler, OutboxMessageEnvelope message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(message);

        using var activity = Source.StartActivity("outbox.handle", ActivityKind.Internal);
        activity?.SetTag("catalog.outbox.handler", handler.Name);
        activity?.SetTag("catalog.outbox.event_type", message.EventType);
        var started = _clock.GetTimestamp();

        try
        {
            await handler.HandleAsync(message, cancellationToken);
            HandlerRan(handler.Name, _clock.GetElapsedTime(started), succeeded: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // The fault is thrown again. This block only records it.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            HandlerRan(handler.Name, _clock.GetElapsedTime(started), succeeded: false);
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag("error.type", exception.GetType().FullName);
            throw;
        }
    }

    /// <summary>Counts a delivery that failed and will be tried again.</summary>
    /// <param name="eventType">The event type of the message.</param>
    public void Failed(string eventType) => _failed.Add(1, new KeyValuePair<string, object?>("event_type", eventType));

    /// <summary>Counts a message that moved to the dead-letter state, and adds it to the dead letters that the gauge reports until the next reading.</summary>
    /// <param name="eventType">The event type of the message.</param>
    public void DeadLettered(string eventType)
    {
        _deadLettered.Add(1, new KeyValuePair<string, object?>("event_type", eventType));

        var current = Volatile.Read(ref _backlog);

        if (current is not null)
        {
            Volatile.Write(ref _backlog, current with { DeadLetters = current.DeadLetters + 1 });
        }
    }

    /// <summary>Gives the gauges a new reading of the outbox.</summary>
    /// <param name="backlog">What the outbox held.</param>
    public void Observe(OutboxBacklog backlog)
    {
        ArgumentNullException.ThrowIfNull(backlog);
        Volatile.Write(ref _backlog, backlog);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Source.Dispose();
        _meter.Dispose();
    }

    private double OldestPendingAge(OutboxBacklog backlog) =>
        backlog.OldestDueAt is { } due ? Math.Max(0, (_clock.GetUtcNow() - due).TotalSeconds) : 0;

    // Before the first reading no gauge reports anything, so a series that does not exist is never read as zero.
    private IEnumerable<Measurement<double>> Read(Func<OutboxBacklog, double> value) =>
        Volatile.Read(ref _backlog) is { } backlog ? [new Measurement<double>(value(backlog))] : [];
}
