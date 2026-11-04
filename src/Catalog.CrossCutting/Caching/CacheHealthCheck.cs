using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Catalog.CrossCutting.Caching;

/// <summary>
/// The check of the cache for the readiness probe (plan sections 8.5 and 8.9, ADR-0040). A node whose shared cache or backplane is down still answers every request, from memory and from the database, so the check
/// reports <see cref="HealthStatus.Degraded"/> and never <see cref="HealthStatus.Unhealthy"/>: a degraded cache is not an outage, and the probe stays green. The result is there so a person who calls the management
/// endpoint sees the state. The alerts that watch the gauges are WP1.11.
/// </summary>
/// <param name="metrics">The metrics, which hold what the circuit breakers reported.</param>
public sealed class CacheHealthCheck(CacheMetrics metrics) : IHealthCheck
{
    private readonly CacheMetrics _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var state = _metrics.Degradation;
        var data = new Dictionary<string, object> { ["sharedCacheDown"] = state.SharedCacheDown, ["backplaneDown"] = state.BackplaneDown };

        return Task.FromResult(state.Degraded
            ? HealthCheckResult.Degraded("The shared cache or the backplane is down. Reads use memory and the database, and a stale read is bounded by the memory time to live.", data: data)
            : HealthCheckResult.Healthy("The shared cache and the backplane answer.", data));
    }
}
