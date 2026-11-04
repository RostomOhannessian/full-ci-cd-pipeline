using System.Collections.Concurrent;
using Catalog.Application.Events;
using Catalog.Domain.Tests.Support;
using Catalog.Infrastructure.Outbox;
using Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Catalog.Infrastructure.Tests.Support;

/// <summary>A handler for tests: it records every delivery, and a test makes it fail, wait, or cancel.</summary>
/// <param name="name">The stable name of the handler.</param>
internal sealed class RecordingHandler(string name = "recorder") : IOutboxMessageHandler
{
    private readonly ConcurrentQueue<OutboxMessageEnvelope> _deliveries = new();

    public string Name { get; } = name;

    /// <summary>Gets or sets what the handler does after it recorded a delivery. A test sets it to throw or to cancel.</summary>
    public Func<OutboxMessageEnvelope, CancellationToken, Task>? Behavior { get; set; }

    public IReadOnlyList<OutboxMessageEnvelope> Deliveries => [.. _deliveries];

    public async Task HandleAsync(OutboxMessageEnvelope message, CancellationToken cancellationToken)
    {
        _deliveries.Enqueue(message);

        if (Behavior is { } behavior)
        {
            await behavior(message, cancellationToken);
        }
    }
}

/// <summary>A logger that keeps what it is given, so a test can read the entries and prove what is in them and what is not.</summary>
/// <typeparam name="T">The category.</typeparam>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception)> _entries = new();

    public IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> Entries => [.. _entries];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        _entries.Enqueue((logLevel, formatter(state, exception), exception));
}

/// <summary>An outbox processor with a fake clock, fixed options, a seeded jitter source, and leases that count, over the database of a test class.</summary>
internal sealed class ProcessorRig : IDisposable
{
    public ProcessorRig(CatalogDatabaseFixture database, FakeTimeProvider clock, OutboxOptions? options = null, params RecordingHandler[] handlers)
    {
        Clock = clock;
        Options = options ?? new OutboxOptions { JitterRatio = 0 };
        Handlers = handlers;
        Database = database;
        Metrics = new OutboxMetrics();
        Processor = new OutboxProcessor(
            database.NewContextFactory(),
            handlers,
            Microsoft.Extensions.Options.Options.Create(Options),
            clock,
            Metrics,
            Logger,
            new Random(7),
            () => TestIds.V7(Counters.Next()));
    }

    public CatalogDatabaseFixture Database { get; }

    public FakeTimeProvider Clock { get; }

    public OutboxOptions Options { get; }

    public RecordingHandler[] Handlers { get; }

    public OutboxMetrics Metrics { get; }

    public CapturingLogger<OutboxProcessor> Logger { get; } = new();

    public OutboxProcessor Processor { get; }

    public CatalogMaintenance Maintenance() => new(Database.NewContextFactory(), Microsoft.Extensions.Options.Options.Create(Options), Clock);

    public void Dispose() => Metrics.Dispose();

    /// <summary>Writes pending outbox rows directly, one for each event type name, due at the time of the clock, and returns their identifiers in the order of their creation. The clock moves one second after each row, so the order of the rows is the order of their time.</summary>
    public async Task<IReadOnlyList<Guid>> SeedMessagesAsync(int count, string eventType = "ProductCreated")
    {
        List<Guid> ids = [];
        for (var index = 0; index < count; index++)
        {
            var id = TestIds.V7(Counters.Next());
            ids.Add(id);
            var now = Clock.GetUtcNow().ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            await SqlServerFixture.ExecuteAsync(
                Database.ConnectionString,
                $$"""
                INSERT INTO catalog.OutboxMessages (Id, EventType, SchemaVersion, OccurredAt, Payload, [Status], Attempts, CreatedAt, NextAttemptAt)
                VALUES ('{{id}}', '{{eventType}}', 1, '{{now}}', '{"n":{{index}}}', 'Pending', 0, '{{now}}', '{{now}}')
                """);
            Clock.Advance(TimeSpan.FromSeconds(1));
        }

        return ids;
    }

    /// <summary>Reads one outbox row as the columns that the tests check.</summary>
    public async Task<OutboxRow> RowAsync(Guid id)
    {
        var rows = await SqlProbe.RowsAsync(
            Database.ConnectionString,
            "SELECT [Status], Attempts, NextAttemptAt, LeasedUntil, LeaseId, CompletedAt, LastError FROM catalog.OutboxMessages WHERE Id = @id",
            ("@id", id));
        var row = rows.Single();

        return new OutboxRow(row[0], int.Parse(row[1], System.Globalization.CultureInfo.InvariantCulture), ParseTime(row[2]), ParseNullableTime(row[3]), row[4], ParseNullableTime(row[5]), row[6]);
    }

    private static DateTimeOffset ParseTime(string text) => DateTimeOffset.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseNullableTime(string text) => text.Length == 0 ? null : ParseTime(text);
}

/// <summary>The columns of an outbox row that the processor tests check.</summary>
internal sealed record OutboxRow(string Status, int Attempts, DateTimeOffset NextAttemptAt, DateTimeOffset? LeasedUntil, string LeaseId, DateTimeOffset? CompletedAt, string LastError);

/// <summary>Makes the dispatcher of a scope with the handlers, the metrics, and the log that a test names.</summary>
internal static class DispatcherSupport
{
    /// <summary>Makes a context that has no database behind it, for a test that dispatches nothing that was written.</summary>
    public static CatalogDbContext NoDatabase()
    {
        var builder = new DbContextOptionsBuilder<CatalogDbContext>();
        builder.UseCatalogSqlServer("Server=127.0.0.1,1;Database=unused");

        return new CatalogDbContext(builder.Options);
    }

    public static OutboxDispatcher Create(CatalogDbContext context, OutboxSignal signal, params IOutboxMessageHandler[] handlers) =>
        new(context, handlers, signal, new OutboxMetrics(), new CapturingLogger<OutboxDispatcher>());

    public static OutboxDispatcher Create(CatalogDbContext context, OutboxSignal signal, CapturingLogger<OutboxDispatcher> logger, params IOutboxMessageHandler[] handlers) =>
        new(context, handlers, signal, new OutboxMetrics(), logger);
}
