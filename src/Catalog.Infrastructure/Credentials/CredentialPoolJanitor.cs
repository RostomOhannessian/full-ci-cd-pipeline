using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Catalog.Infrastructure.Credentials;

/// <summary>The metrics and the activity source of the credential rotation, both named <c>Catalog.Credentials</c> (plan sections 8.4 and 8.11, ADR-0043). The values and the spans hold no credential.</summary>
public sealed class CredentialMetrics : IDisposable
{
    /// <summary>The name of the meter and of the activity source.</summary>
    public const string MeterName = "Catalog.Credentials";

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _rotations;
    private readonly Counter<long> _poolClears;

    /// <summary>Initializes a new instance of the <see cref="CredentialMetrics"/> class.</summary>
    public CredentialMetrics()
    {
        Source = new ActivitySource(MeterName);
        _rotations = _meter.CreateCounter<long>("catalog.credentials.rotations", "{rotation}", "Switches to a new database credential.");
        _poolClears = _meter.CreateCounter<long>("catalog.credentials.pool_clears", "{clear}", "Pools of an old credential that were cleared after the grace period.");
    }

    /// <summary>Gets the activity source of the rotation, which the janitor starts its spans from.</summary>
    public ActivitySource Source { get; }

    /// <summary>Counts a rotation.</summary>
    public void Rotated() => _rotations.Add(1);

    /// <summary>Counts a cleared pool.</summary>
    public void PoolCleared() => _poolClears.Add(1);

    /// <inheritdoc />
    public void Dispose()
    {
        Source.Dispose();
        _meter.Dispose();
    }
}

/// <summary>
/// Clears the connection pool of the old credential after a grace period (plan section 8.4, ADR-0037, spike 1.e). When the source rotates, new connections already use the new credential, so the old pool only
/// holds idle connections, and SqlClient never closes them on its own within a useful time. The janitor waits for the grace period, builds the old connection string with the same function that opened the connections,
/// and calls <see cref="SqlConnection.ClearPool"/>, which closes the idle connections at once and marks the connections in use to close when they return. Clearing never interrupts a running command. The order that
/// keeps a rotation safe is: the source rotates, new connections switch, the janitor clears the old pool, and only then does an operator revoke the old login. A revocation that comes earlier kills in-flight work on the
/// old login, and SQL Server refuses to drop a login that still has sessions. A credential that returns to use (A, then B, then A again) is never cleared while it is the current one.
/// </summary>
public sealed partial class CredentialPoolJanitor : IHostedService, IDisposable
{
    private readonly ICredentialSource _source;
    private readonly string _baseConnectionString;
    private readonly TimeSpan _grace;
    private readonly TimeProvider _clock;
    private readonly CredentialMetrics _metrics;
    private readonly ILogger<CredentialPoolJanitor> _logger;
    private readonly ConcurrentDictionary<long, ITimer> _pending = new();
    private long _next;
    private int _started;

    /// <summary>Initializes a new instance of the <see cref="CredentialPoolJanitor"/> class.</summary>
    /// <param name="source">The source whose rotations the janitor follows.</param>
    /// <param name="baseConnectionString">The connection string without a credential, the same one that the interceptor uses.</param>
    /// <param name="grace">How long to wait after a rotation before the old pool is cleared. Zero clears at once.</param>
    /// <param name="clock">The clock of the grace period.</param>
    /// <param name="metrics">The metrics.</param>
    /// <param name="logger">The logger.</param>
    public CredentialPoolJanitor(ICredentialSource source, string baseConnectionString, TimeSpan grace, TimeProvider clock, CredentialMetrics metrics, ILogger<CredentialPoolJanitor> logger)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _baseConnectionString = baseConnectionString ?? throw new ArgumentNullException(nameof(baseConnectionString));
        ArgumentOutOfRangeException.ThrowIfLessThan(grace, TimeSpan.Zero);
        _grace = grace;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Gets the number of clears that wait for their grace period.</summary>
    public int PendingClears => _pending.Count;

    /// <summary>Starts to follow the rotations of the source.</summary>
    /// <param name="cancellationToken">A token that cancels the start.</param>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) == 0)
        {
            _source.Rotated += OnRotated;
        }

        return Task.CompletedTask;
    }

    /// <summary>Stops following the source and drops the clears that still wait, because the process is ending.</summary>
    /// <param name="cancellationToken">A token that cancels the stop.</param>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _started, 0) == 1)
        {
            _source.Rotated -= OnRotated;
        }

        foreach (var key in _pending.Keys)
        {
            if (_pending.TryRemove(key, out var timer))
            {
                timer.Dispose();
            }
        }
    }

    private void OnRotated(object? sender, CredentialRotatedEventArgs args)
    {
        _metrics.Rotated();

        // The span marks the moment of the switch. It carries no credential, and a trace that crosses the switch shows it next to the requests that ran on either side.
        _metrics.Source.StartActivity("credentials.rotate", ActivityKind.Internal)?.Dispose();

        var old = args.Previous;

        if (_grace == TimeSpan.Zero)
        {
            Clear(old);

            return;
        }

        var id = Interlocked.Increment(ref _next);
        var timer = _clock.CreateTimer(
            _ =>
            {
                if (_pending.TryRemove(id, out var finished))
                {
                    finished.Dispose();
                }

                Clear(old);
            },
            null,
            _grace,
            Timeout.InfiniteTimeSpan);
        _pending[id] = timer;
        LogScheduled(_grace.TotalSeconds);
    }

    private void Clear(DatabaseCredential old)
    {
        using var activity = _metrics.Source.StartActivity("credentials.clear_pool", ActivityKind.Internal);

        if (old == _source.Current)
        {
            activity?.SetTag("catalog.outcome", "skipped");
            LogSkipped();

            return;
        }

        try
        {
            using var connection = new SqlConnection(CatalogConnectionString.Build(_baseConnectionString, old));
            SqlConnection.ClearPool(connection);
            _metrics.PoolCleared();
            activity?.SetTag("catalog.outcome", "cleared");
            LogCleared();
        }
#pragma warning disable CA1031 // The clear runs on a timer thread, and a fault there would end the process. It is logged by its type, and the idle connections close with the pool lifetime anyway.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            activity?.SetTag("catalog.outcome", "failed");
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag("error.type", exception.GetType().FullName);
            LogFailed(exception.GetType().Name);
        }
    }

    [LoggerMessage(EventId = 30, Level = LogLevel.Information, Message = "The pool of the old database credential will be cleared in {GraceSeconds} seconds.")]
    private partial void LogScheduled(double graceSeconds);

    [LoggerMessage(EventId = 31, Level = LogLevel.Information, Message = "The pool of the old database credential was cleared.")]
    private partial void LogCleared();

    [LoggerMessage(EventId = 32, Level = LogLevel.Information, Message = "The old database credential is in use again, so its pool was not cleared.")]
    private partial void LogSkipped();

    [LoggerMessage(EventId = 33, Level = LogLevel.Error, Message = "Clearing the pool of the old database credential failed with {ExceptionType}.")]
    private partial void LogFailed(string exceptionType);
}
