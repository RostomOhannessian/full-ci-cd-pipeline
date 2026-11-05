using ZiggyCreatures.Caching.Fusion;

namespace Catalog.CrossCutting.Caching;

/// <summary>
/// The names of the activity sources and meters that FusionCache emits, so the composition root can subscribe to them without naming the library (plan sections 8.5 and 8.11, ADR-0043). FusionCache needs no
/// package of OpenTelemetry for this: it publishes through <see cref="System.Diagnostics.ActivitySource"/> and <see cref="System.Diagnostics.Metrics.Meter"/>, and a listener that names them receives the spans and
/// the metrics. The spans of the memory level are left out, because every read of the memory would make one, and the span of the operation already says whether it was a hit.
/// </summary>
public static class CacheTelemetryNames
{
    /// <summary>Gets the activity sources of FusionCache that the traces include: the operations, the shared cache, and the backplane.</summary>
    public static IReadOnlyList<string> ActivitySources { get; } =
    [
        FusionCacheDiagnostics.ActivitySourceName,
        FusionCacheDiagnostics.ActivitySourceNameDistributedLevel,
        FusionCacheDiagnostics.ActivitySourceNameBackplane,
    ];

    /// <summary>Gets the meters of FusionCache that the metrics include: the operations (hits, misses, and stale hits), the memory, the shared cache, and the backplane.</summary>
    public static IReadOnlyList<string> Meters { get; } =
    [
        FusionCacheDiagnostics.MeterName,
        FusionCacheDiagnostics.MeterNameMemoryLevel,
        FusionCacheDiagnostics.MeterNameDistributedLevel,
        FusionCacheDiagnostics.MeterNameBackplane,
    ];
}
