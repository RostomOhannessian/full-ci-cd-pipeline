using Catalog.Infrastructure.Outbox;
using Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Catalog.Infrastructure.Health;

/// <summary>
/// The check of the outbox for the readiness probe (plan sections 8.4 and 8.9, ADR-0040). A dead-letter message or a due message that nobody leased means that the guarantee of the invalidation is behind, which the
/// API can still serve through, so the check reports <see cref="HealthStatus.Degraded"/> and the probe stays green. It is unhealthy only when it cannot read the outbox at all. The alerts that watch the counters
/// are WP1.11, and this check shows the same fact to a person who calls the endpoint. A message is overdue when it was due before a whole lease and two polls ago, which is longer than a healthy processor leaves it.
/// </summary>
/// <param name="contexts">Makes a write context.</param>
/// <param name="options">The settings of the outbox.</param>
/// <param name="clock">The source of the time.</param>
/// <param name="logger">The logger.</param>
public sealed partial class OutboxHealthCheck(IDbContextFactory<CatalogDbContext> contexts, IOptions<OutboxOptions> options, TimeProvider clock, ILogger<OutboxHealthCheck> logger) : IHealthCheck
{
    private readonly IDbContextFactory<CatalogDbContext> _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
    private readonly OutboxOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ILogger<OutboxHealthCheck> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(DatabaseHealthCheck.TimeoutSeconds));

        try
        {
            var overdueBefore = _clock.GetUtcNow() - (_options.LeaseDuration + (2 * _options.PollInterval));

            await using var database = await _contexts.CreateDbContextAsync(timeout.Token);
            var deadLetters = await database.OutboxMessages.AsNoTracking().CountAsync(message => message.Status == OutboxStatus.DeadLetter, timeout.Token);
            var overdue = await database.OutboxMessages.AsNoTracking().CountAsync(message => message.Status == OutboxStatus.Pending && message.NextAttemptAt < overdueBefore, timeout.Token);
            var data = new Dictionary<string, object> { ["deadLetters"] = deadLetters, ["overdue"] = overdue };

            return deadLetters == 0 && overdue == 0
                ? HealthCheckResult.Healthy("The outbox has no dead-letter message and no overdue message.", data)
                : HealthCheckResult.Degraded($"The outbox has {deadLetters} dead-letter message(s) and {overdue} overdue message(s).", data: data);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogUnreadable(exception.GetType().Name);

            return HealthCheckResult.Unhealthy("The outbox cannot be read.");
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "The readiness check could not read the outbox ({ExceptionType}).")]
    private partial void LogUnreadable(string exceptionType);
}
