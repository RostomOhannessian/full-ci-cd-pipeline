using System.Diagnostics.Metrics;
using Catalog.Application.Idempotency;

namespace Catalog.Infrastructure.Idempotency;

/// <summary>
/// The metrics of the idempotency store, on the meter <c>Catalog.Idempotency</c> (plan sections 8.4 and 8.11, ADR-0036, ADR-0043). <c>catalog.idempotency.decisions</c> counts what the store decided for each request that
/// carried a key, by outcome: <c>reserved</c> (the first request), <c>replayed</c> (the stored result was returned), <c>in_progress</c> (the first request still runs), and <c>reused</c> (the key came with another
/// request). The key and the caller are never labels. The composition root owns one instance.
/// </summary>
public sealed class IdempotencyMetrics : IDisposable
{
    /// <summary>The name of the meter.</summary>
    public const string MeterName = "Catalog.Idempotency";

    private readonly Meter _meter;
    private readonly Counter<long> _decisions;

    /// <summary>Initializes a new instance of the <see cref="IdempotencyMetrics"/> class.</summary>
    public IdempotencyMetrics()
    {
        // The scope is this object, so a listener can tell the metrics of one instance from those of another in the same process, which the tests of several hosts need.
        _meter = new Meter(new MeterOptions(MeterName) { Scope = this });
        _decisions = _meter.CreateCounter<long>("catalog.idempotency.decisions", "{decision}", "What the idempotency store decided for a request with a key, by outcome (reserved, replayed, in_progress, or reused).");
    }

    /// <summary>Counts one decision.</summary>
    /// <param name="outcome">What the store decided.</param>
    public void Decided(IdempotencyOutcome outcome) =>
        _decisions.Add(1, new KeyValuePair<string, object?>("outcome", outcome switch
        {
            IdempotencyOutcome.Reserved => "reserved",
            IdempotencyOutcome.Replay => "replayed",
            IdempotencyOutcome.InProgress => "in_progress",
            IdempotencyOutcome.Reused => "reused",
            _ => "unknown",
        }));

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();
}
