using System.Threading.Channels;
using AwesomeAssertions;
using Catalog.Domain.Tests.Support;
using Catalog.Infrastructure.Outbox;
using Catalog.Infrastructure.Persistence;
using Catalog.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Catalog.Infrastructure.Tests;

/// <summary>
/// REQ-DATA-003: the background service runs the processor in a loop, wakes on a commit, leases the next batch at once when a batch was full, survives a fault of a pass, runs the cleanup, and stops with its token (plan
/// section 8.4, ADR-0035). The handler writes each delivery into a channel, and a test waits on the channel, so no test sleeps. The clock is fake and never moves, so the poll interval never ends a wait.
/// </summary>
public sealed class OutboxBackgroundServiceTests(CatalogDatabaseFixture database) : IClassFixture<CatalogDatabaseFixture>
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string Connection => database.ConnectionString;

    [Fact]
    [Trait("Requirement", "REQ-DATA-003")]
    public async Task A_wake_up_delivers_a_committed_message_and_the_first_pass_runs_the_cleanup()
    {
        await database.ResetOutboxAsync();
        var arrivals = Channel.CreateUnbounded<Guid>();
        var handler = new RecordingHandler { Behavior = (message, _) => arrivals.Writer.WriteAsync(message.MessageId, Token).AsTask() };
        using var rig = new ProcessorRig(database, Make.Clock(), null, handler);
        using var signal = new OutboxSignal();
        var dispatcher = new OutboxDispatcher(signal);
        var expired = Guid.NewGuid();
        var now = rig.Clock.GetUtcNow();
        await SqlServerFixture.ExecuteAsync(
            Connection,
            $"""
            INSERT INTO catalog.OutboxMessages (Id, EventType, SchemaVersion, OccurredAt, Payload, [Status], Attempts, CreatedAt, NextAttemptAt, CompletedAt)
            VALUES ('{expired}', 'ProductCreated', 1, '{now.AddDays(-30):O}', '[]', 'Done', 1, '{now.AddDays(-30):O}', '{now.AddDays(-30):O}', '{now.AddDays(-20):O}')
            """);
        using var service = NewService(rig, signal);
        await service.StartAsync(Token);

        var first = (await rig.SeedMessagesAsync(1)).Single();
        await dispatcher.DispatchCommittedAsync(Token);
        (await arrivals.Reader.ReadAsync(Token).AsTask().WaitAsync(Patience, Token)).Should().Be(first);

        // The second message is claimed by a later pass than the first, and the cleanup of the first pass has run by then.
        var second = (await rig.SeedMessagesAsync(1)).Single();
        await dispatcher.DispatchCommittedAsync(Token);
        (await arrivals.Reader.ReadAsync(Token).AsTask().WaitAsync(Patience, Token)).Should().Be(second);
        await service.StopAsync(Token);

        (await rig.RowAsync(first)).Status.Should().Be("Done");
        (await SqlProbe.ScalarAsync<int>(Connection, $"SELECT COUNT(*) FROM catalog.OutboxMessages WHERE Id = '{expired}'")).Should().Be(0);
        service.ExecuteTask!.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-003")]
    public async Task A_full_batch_makes_the_service_lease_the_next_one_without_waiting_for_a_wake_up()
    {
        await database.ResetOutboxAsync();
        var arrivals = Channel.CreateUnbounded<Guid>();
        var handler = new RecordingHandler { Behavior = (message, _) => arrivals.Writer.WriteAsync(message.MessageId, Token).AsTask() };
        using var rig = new ProcessorRig(database, Make.Clock(), new OutboxOptions { BatchSize = 2, JitterRatio = 0 }, handler);
        using var signal = new OutboxSignal();
        var ids = await rig.SeedMessagesAsync(5);
        using var service = NewService(rig, signal);

        await service.StartAsync(Token);
        List<Guid> delivered = [];
        for (var index = 0; index < ids.Count; index++)
        {
            delivered.Add(await arrivals.Reader.ReadAsync(Token).AsTask().WaitAsync(Patience, Token));
        }
        await service.StopAsync(Token);

        delivered.Should().Equal(ids);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-003")]
    public async Task A_fault_of_a_pass_is_logged_and_the_service_delivers_on_the_next_wake_up()
    {
        await database.ResetOutboxAsync();
        var arrivals = Channel.CreateUnbounded<Guid>();
        var handler = new RecordingHandler { Behavior = (message, _) => arrivals.Writer.WriteAsync(message.MessageId, Token).AsTask() };
        using var rig = new ProcessorRig(database, Make.Clock(), null, handler);
        using var signal = new OutboxSignal();
        var id = (await rig.SeedMessagesAsync(1)).Single();
        var processor = new OutboxProcessor(
            new FlakyContextFactory(database.NewContextFactory(), failures: 1),
            [handler],
            Microsoft.Extensions.Options.Options.Create(rig.Options),
            rig.Clock,
            rig.Metrics,
            rig.Logger);
        var logger = new CapturingLogger<OutboxBackgroundService>();
        using var service = NewService(rig, signal, processor, logger);

        await service.StartAsync(Token);
        signal.Notify();
        (await arrivals.Reader.ReadAsync(Token).AsTask().WaitAsync(Patience, Token)).Should().Be(id);
        await service.StopAsync(Token);

        logger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Error).Which.Exception.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-003")]
    public async Task Stopping_the_service_ends_the_loop_after_it_has_started_to_work()
    {
        await database.ResetOutboxAsync();
        var arrivals = Channel.CreateUnbounded<Guid>();
        var handler = new RecordingHandler { Behavior = (message, _) => arrivals.Writer.WriteAsync(message.MessageId, Token).AsTask() };
        using var rig = new ProcessorRig(database, Make.Clock(), null, handler);
        using var signal = new OutboxSignal();
        var id = (await rig.SeedMessagesAsync(1)).Single();
        using var service = NewService(rig, signal);
        await service.StartAsync(Token);
        (await arrivals.Reader.ReadAsync(Token).AsTask().WaitAsync(Patience, Token)).Should().Be(id);

        await service.StopAsync(Token);

        // The framework may cancel a task that had not started when the stop came, so the loop must have run for this check to mean what it says.
        service.ExecuteTask!.Status.Should().Be(TaskStatus.RanToCompletion);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-003")]
    public void A_missing_dependency_is_refused()
    {
        using var rig = new ProcessorRig(database, Make.Clock());
        using var signal = new OutboxSignal();
        var options = Microsoft.Extensions.Options.Options.Create(rig.Options);
        var logger = new CapturingLogger<OutboxBackgroundService>();

        var noProcessor = () => new OutboxBackgroundService(null!, rig.Maintenance(), signal, options, rig.Clock, logger);
        var noMaintenance = () => new OutboxBackgroundService(rig.Processor, null!, signal, options, rig.Clock, logger);
        var noSignal = () => new OutboxBackgroundService(rig.Processor, rig.Maintenance(), null!, options, rig.Clock, logger);
        var noOptions = () => new OutboxBackgroundService(rig.Processor, rig.Maintenance(), signal, null!, rig.Clock, logger);
        var noClock = () => new OutboxBackgroundService(rig.Processor, rig.Maintenance(), signal, options, null!, logger);
        var noLogger = () => new OutboxBackgroundService(rig.Processor, rig.Maintenance(), signal, options, rig.Clock, null!);

        noProcessor.Should().Throw<ArgumentNullException>();
        noMaintenance.Should().Throw<ArgumentNullException>();
        noSignal.Should().Throw<ArgumentNullException>();
        noOptions.Should().Throw<ArgumentNullException>();
        noClock.Should().Throw<ArgumentNullException>();
        noLogger.Should().Throw<ArgumentNullException>();
    }

    private static OutboxBackgroundService NewService(ProcessorRig rig, OutboxSignal signal, OutboxProcessor? processor = null, CapturingLogger<OutboxBackgroundService>? logger = null) =>
        new(processor ?? rig.Processor, rig.Maintenance(), signal, Microsoft.Extensions.Options.Options.Create(rig.Options), rig.Clock, logger ?? new CapturingLogger<OutboxBackgroundService>());

    private sealed class FlakyContextFactory(IDbContextFactory<CatalogDbContext> inner, int failures) : IDbContextFactory<CatalogDbContext>
    {
        private int _remaining = failures;

        public CatalogDbContext CreateDbContext() =>
            Interlocked.Decrement(ref _remaining) >= 0 ? throw new InvalidOperationException("The database is down.") : inner.CreateDbContext();
    }
}
