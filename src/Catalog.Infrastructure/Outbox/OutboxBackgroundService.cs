using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Catalog.Infrastructure.Outbox;

/// <summary>
/// The background service that runs the outbox processor in a loop and the cleanup on a schedule (plan section 8.4). It runs a pass, runs another at once when the pass leased a full batch, and otherwise waits for a
/// wake-up from the dispatcher or for the poll interval, so a message is delivered within milliseconds after a commit and still delivered when the wake-up never comes. A fault of a pass (a database that is down, for
/// example) is logged and the loop waits and tries again, so the service never stops for a transient failure. The loop is glue, and the tests of <see cref="OutboxProcessor"/> prove the delivery.
/// </summary>
public sealed partial class OutboxBackgroundService : BackgroundService
{
    private readonly OutboxProcessor _processor;
    private readonly CatalogMaintenance _maintenance;
    private readonly OutboxSignal _signal;
    private readonly OutboxOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<OutboxBackgroundService> _logger;

    /// <summary>Initializes a new instance of the <see cref="OutboxBackgroundService"/> class.</summary>
    /// <param name="processor">The processor.</param>
    /// <param name="maintenance">The cleanup job.</param>
    /// <param name="signal">The signal that the dispatcher raises after a commit.</param>
    /// <param name="options">The settings.</param>
    /// <param name="clock">The source of the time of the poll and of the cleanup schedule.</param>
    /// <param name="logger">The logger.</param>
    public OutboxBackgroundService(
        OutboxProcessor processor,
        CatalogMaintenance maintenance,
        OutboxSignal signal,
        IOptions<OutboxOptions> options,
        TimeProvider clock,
        ILogger<OutboxBackgroundService> logger)
    {
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _maintenance = maintenance ?? throw new ArgumentNullException(nameof(maintenance));
        _signal = signal ?? throw new ArgumentNullException(nameof(signal));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextMaintenance = _clock.GetUtcNow();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var batch = await _processor.ProcessBatchAsync(stoppingToken);

                if (_clock.GetUtcNow() >= nextMaintenance)
                {
                    var purged = await _maintenance.PurgeAsync(stoppingToken);
                    nextMaintenance = _clock.GetUtcNow() + _options.MaintenanceInterval;
                    LogPurged(purged);
                }

                if (batch.Claimed >= _options.BatchSize)
                {
                    continue;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // The loop must outlive a transient fault such as a database that is down, so it logs and waits.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                LogPassFailed(exception);
            }

            try
            {
                await _signal.WaitAsync(_options.PollInterval, _clock, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    [LoggerMessage(EventId = 10, Level = LogLevel.Error, Message = "A pass of the outbox processor failed. The service waits for the next poll and tries again.")]
    private partial void LogPassFailed(Exception exception);

    [LoggerMessage(EventId = 11, Level = LogLevel.Debug, Message = "The cleanup deleted {Rows} expired or finished rows.")]
    private partial void LogPurged(int rows);
}
