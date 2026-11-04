using Catalog.Infrastructure.Credentials;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Catalog.Infrastructure.Health;

/// <summary>
/// The check of the startup probe (plan section 8.9, ADR-0040): the configuration is valid, which the host proved before it started, and the source of the database credential holds a credential. A source that cannot
/// read its first credential throws when the container creates it, so this check is unhealthy until the credential is there. The result says that a credential is loaded and never names it.
/// </summary>
/// <param name="source">The source of the database credential.</param>
public sealed class CredentialHealthCheck(ICredentialSource source) : IHealthCheck
{
    private readonly ICredentialSource _source = source ?? throw new ArgumentNullException(nameof(source));

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(_source.Current is null
            ? HealthCheckResult.Unhealthy("The database credential is not loaded.")
            : HealthCheckResult.Healthy("The database credential is loaded."));
}
