using System.Diagnostics.Metrics;

namespace Catalog.CrossCutting.Caching;

/// <summary>
/// What the cache of one node knows about the health of the shared cache and of the backplane, which the circuit breakers of FusionCache report. While either is open, the node serves what it holds in memory, and an
/// invalidation does not reach the other nodes, so the staleness of a read widens from the latency of the backplane to the time to live of the memory (plan section 8.5). The adapter reads the state to tell an
/// invalidation that is complete from one that is not, and the metrics report it.
/// </summary>
public sealed class CacheDegradation
{
    private int _sharedOpen;
    private int _backplaneOpen;

    /// <summary>Gets a value indicating whether the circuit breaker of the shared cache is open.</summary>
    public bool SharedCacheDown => Volatile.Read(ref _sharedOpen) != 0;

    /// <summary>Gets a value indicating whether the circuit breaker of the backplane is open.</summary>
    public bool BackplaneDown => Volatile.Read(ref _backplaneOpen) != 0;

    /// <summary>Gets a value indicating whether a read of this node can be stale for longer than the latency of the backplane, which is true when either level is down.</summary>
    public bool Degraded => SharedCacheDown || BackplaneDown;

    internal void SetSharedCacheDown(bool down) => Volatile.Write(ref _sharedOpen, down ? 1 : 0);

    internal void SetBackplaneDown(bool down) => Volatile.Write(ref _backplaneOpen, down ? 1 : 0);
}

/// <summary>
/// The metrics of the cache, on the meter <c>Catalog.Cache</c> (plan sections 8.5 and 8.11, ADR-0038). A tag of a measurement holds a fixed word and never a key, an identifier, or a value of an entry, because a key
/// can name a product and a tag with many values makes a series for each. The hit ratio of the plan is the hits divided by the hits and misses of <c>catalog.cache.requests</c>. The gauges report the circuit breakers,
/// which is the signal that the alerts of WP1.11 watch. The composition root owns one instance, and a test listens to the meter with a <see cref="MeterListener"/>.
/// </summary>
public sealed class CacheMetrics : IDisposable
{
    /// <summary>The name of the meter.</summary>
    public const string MeterName = "Catalog.Cache";

    private readonly Meter _meter;
    private readonly Counter<long> _requests;
    private readonly Counter<long> _invalidations;
    private readonly Counter<long> _faults;
    private readonly Counter<long> _serializationErrors;
    private readonly Counter<long> _circuitChanges;

    /// <summary>Initializes a new instance of the <see cref="CacheMetrics"/> class.</summary>
    public CacheMetrics()
    {
        // The scope is this object, so a listener can tell the metrics of one node from those of another node in the same process, which the tests of two nodes need.
        _meter = new Meter(new MeterOptions(MeterName) { Scope = this });
        _requests = _meter.CreateCounter<long>("catalog.cache.requests", "{request}", "Reads through the cache, by result (hit or miss) and by whether this node was degraded when it answered.");
        _invalidations = _meter.CreateCounter<long>("catalog.cache.invalidations", "{invalidation}", "Removals of a tag, by outcome (complete, or incomplete when the shared cache or the backplane was not reached).");
        _faults = _meter.CreateCounter<long>("catalog.cache.faults", "{fault}", "Faults of the cache that a read absorbed by running the loader.");
        _serializationErrors = _meter.CreateCounter<long>("catalog.cache.serialization_errors", "{error}", "Entries that the cache could not write to or read from the shared cache.");
        _circuitChanges = _meter.CreateCounter<long>("catalog.cache.circuit_changes", "{change}", "Changes of a circuit breaker, by level (shared or backplane) and state (open or closed).");
        _meter.CreateObservableGauge("catalog.cache.shared.down", () => Degradation.SharedCacheDown ? 1 : 0, "{state}", "1 while the circuit breaker of the shared cache is open, otherwise 0.");
        _meter.CreateObservableGauge("catalog.cache.backplane.down", () => Degradation.BackplaneDown ? 1 : 0, "{state}", "1 while the circuit breaker of the backplane is open, otherwise 0. The staleness widens to the memory time to live.");
    }

    /// <summary>Gets what this node knows about the health of the shared cache and of the backplane.</summary>
    public CacheDegradation Degradation { get; } = new();

    /// <summary>Counts a read.</summary>
    /// <param name="hit">Whether an entry answered it. A miss ran the loader.</param>
    public void Request(bool hit) =>
        _requests.Add(
            1,
            new KeyValuePair<string, object?>("result", hit ? "hit" : "miss"),
            new KeyValuePair<string, object?>("degraded", Degradation.Degraded ? "true" : "false"));

    /// <summary>Counts the removal of a tag.</summary>
    /// <param name="complete">Whether the removal reached the shared cache and the backplane.</param>
    public void Invalidation(bool complete) =>
        _invalidations.Add(1, new KeyValuePair<string, object?>("outcome", complete ? "complete" : "incomplete"));

    /// <summary>Counts a fault of the cache that a read absorbed.</summary>
    public void Fault() => _faults.Add(1);

    /// <summary>Counts an entry that could not be written or read for its format.</summary>
    public void SerializationError() => _serializationErrors.Add(1);

    internal void SharedCacheChanged(bool closed)
    {
        Degradation.SetSharedCacheDown(!closed);
        _circuitChanges.Add(1, new KeyValuePair<string, object?>("level", "shared"), new KeyValuePair<string, object?>("state", closed ? "closed" : "open"));
    }

    internal void BackplaneChanged(bool closed)
    {
        Degradation.SetBackplaneDown(!closed);
        _circuitChanges.Add(1, new KeyValuePair<string, object?>("level", "backplane"), new KeyValuePair<string, object?>("state", closed ? "closed" : "open"));
    }

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();
}
