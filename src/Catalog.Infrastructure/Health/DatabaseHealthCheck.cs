using Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Catalog.Infrastructure.Health;

/// <summary>
/// The check of the database for the readiness probe (plan section 8.9, ADR-0040). It opens a connection with the credential that the source holds now and compares the migrations of this build with the history of the
/// database, so it is healthy only when SQL Server answers, the login works, and the schema has every migration that this version of the code expects. A schema that is ahead of the code is healthy, because the
/// expand and contract discipline keeps the previous release working (plan section 8.10). The result never repeats the text of an exception, because the message of a connection error can name a server.
/// </summary>
/// <param name="contexts">Makes a write context, which carries the credential interceptor.</param>
/// <param name="logger">The logger.</param>
public sealed partial class DatabaseHealthCheck(IDbContextFactory<CatalogDbContext> contexts, ILogger<DatabaseHealthCheck> logger) : IHealthCheck
{
    /// <summary>The longest time that the check waits for the database, in seconds. A probe must answer before its own timeout.</summary>
    public const int TimeoutSeconds = 5;

    private readonly IDbContextFactory<CatalogDbContext> _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
    private readonly ILogger<DatabaseHealthCheck> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

        try
        {
            await using var database = await _contexts.CreateDbContextAsync(timeout.Token);
            var pending = (await database.Database.GetPendingMigrationsAsync(timeout.Token)).Count();

            return pending == 0
                ? HealthCheckResult.Healthy("The database answers and the schema is current.")
                : HealthCheckResult.Unhealthy($"The schema is behind the code: {pending} migration(s) are not applied.", data: new Dictionary<string, object> { ["pendingMigrations"] = pending });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogUnreachable(exception.GetType().Name);

            return HealthCheckResult.Unhealthy("The database cannot be reached with the current credential.");
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "The readiness check could not reach the database ({ExceptionType}).")]
    private partial void LogUnreachable(string exceptionType);
}
