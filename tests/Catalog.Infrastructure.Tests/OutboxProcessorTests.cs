using AwesomeAssertions;
using Catalog.Domain.Tests.Support;
using Catalog.Infrastructure.Outbox;
using Catalog.Infrastructure.Tests.Support;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Catalog.Infrastructure.Tests;

/// <summary>
/// REQ-DATA-003: the processor delivers each message at least once, leases work with <c>UPDLOCK, READPAST, ROWLOCK</c> so two processors never hold one row, retries with a bounded backoff, moves exhausted work to the
/// dead-letter state, and records a receipt for each handler (plan section 8.4, ADR-0012, ADR-0035). Every test drives the processor with a fake clock and moves the clock to end a lease or a backoff, so no test waits.
/// </summary>
public sealed class OutboxProcessorTests(CatalogDatabaseFixture database) : IClassFixture<CatalogDatabaseFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string Connection => database.ConnectionString;

    [Fact]
    [Trait("Requirement", "REQ-DATA-003")]
    public async Task A_pass_delivers_each_due_message_to_every_handler_oldest_first_records_receipts_and_marks_it_done()
    {
        await database.ResetOutboxAsync();
        var first = new RecordingHandler("first");
        var second = new RecordingHandler("second");
        using var rig = new ProcessorRig(database, Make.Clock(), null, first, second);
        var ids = await rig.SeedMessagesAsync(3);

        var result = await rig.Processor.ProcessBatchAsync(Token);

        result.Should().Be(new OutboxBatchResult(3, 3, 0, 0));
        first.Deliveries.Select(delivery => delivery.MessageId).Should().Equal(ids);
        second.Deliveries.Select(delivery => delivery.MessageId).Should().Equal(ids);
        first.Deliveries[0].Should().BeEquivalentTo(new { EventType = "ProductCreated", SchemaVersion = 1, Payload = """{"n":0}""" });
        (await SqlProbe.ScalarAsync<int>(Connection, "SELECT COUNT(*) FROM catalog.OutboxReceipts")).Should().Be(6);
        foreach (var id in ids)
        {
            var row = await rig.RowAsync(id);
            row.Status.Should().Be("Done");
            row.Attempts.Should().Be(1);
            row.LeasedUntil.Should().BeNull();
            row.LeaseId.Should().BeEmpty();
            row.CompletedAt.Should().Be(rig.Clock.GetUtcNow());
            row.LastError.Should().BeEmpty();
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-002")]
    [Trait("Requirement", "REQ-DATA-003")]
    public async Task An_event_that_the_interceptor_wrote_is_delivered_with_its_payload_and_a_rolled_back_event_never_is()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        var handler = new RecordingHandler();
        using var rig = new ProcessorRig(database, clock, null, handler);
        var committed = Make.Category(Counters.Next(), "Keyboards");
        await using (var scope = new CatalogScope(database, clock))
        {
            await scope.SaveAsync(committed);
        }

        await using (var scope = new CatalogScope(database, clock))
        {
            scope.UnitOfWork.BeforeCommit((_, _) => throw new InvalidOperationException("The commit failed."));
            var rolledBack = () => scope.UnitOfWork.ExecuteAsync(
                _ =>
                {
                    scope.Categories.Add(Make.Category(Counters.Next(), "Monitors"));

                    return Task.FromResult(Application.Results.Result.Success(1));
                },
                Token);
            await rolledBack.Should().ThrowAsync<InvalidOperationException>();
        }

        var result = await rig.Processor.ProcessBatchAsync(Token);

        result.Should().Be(new OutboxBatchResult(1, 1, 0, 0));
        var delivery = handler.Deliveries.Should().ContainSingle().Which;
        delivery.EventType.Should().Be("CategoryCreated");
        delivery.SchemaVersion.Should().Be(1);
        delivery.OccurredAt.Should().Be(clock.GetUtcNow());
        delivery.Payload.Should().Be($$"""{"eventType":"CategoryCreated","schemaVersion":1,"occurredAt":"{{clock.GetUtcNow():O}}","categoryId":"{{committed.Id.Value}}","name":"Keyboards"}""");
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-003")]
    public async Task A_message_that_is_not_due_waits_until_its_time()
    {
        await database.ResetOutboxAsync();
        var handler = new RecordingHandler();
        using var rig = new ProcessorRig(database, Make.Clock(), null, handler);
        var id = (await rig.SeedMessagesAsync(1)).Single();
        await SqlServerFixture.ExecuteAsync(Connection, $"UPDATE catalog.OutboxMessages SET NextAttemptAt = DATEADD(MINUTE, 10, NextAttemptAt) WHERE Id = '{id}'");

        (await rig.Processor.ProcessBatchAsync(Token)).Claimed.Should().Be(0);
        rig.Clock.Advance(TimeSpan.FromMinutes(10));
        var result = await rig.Processor.ProcessBatchAsync(Token);

        result.Delivered.Should().Be(1);
        handler.Deliveries.Should().ContainSingle().Which.MessageId.Should().Be(id);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-003")]
    public async Task A_pass_leases_at_most_one_batch()
    {
        await database.ResetOutboxAsync();
        var handler = new RecordingHandler();
        using var rig = new ProcessorRig(database, Make.Clock(), new OutboxOptions { BatchSize = 2, JitterRatio = 0 }, handler);
        var ids = await rig.SeedMessagesAsync(5);

        var passes = new[]
        {
            await rig.Processor.ProcessBatchAsync(Token),
            await rig.Processor.ProcessBatchAsync(Token),
            await rig.Processor.ProcessBatchAsync(Token),
            await rig.Processor.ProcessBatchAsync(Token),
        };

        passes.Select(pass => pass.Claimed).Should().Equal(2, 2, 1, 0);
        handler.Deliveries.Select(delivery => delivery.MessageId).Should().Equal(ids);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-003")]
    public async Task A_message_completes_when_no_handler_is_registered()
    {
        await database.ResetOutboxAsync();
        using var rig = new ProcessorRig(database, Make.Clock());
        var ids = await rig.SeedMessagesAsync(2);

        var result = await rig.Processor.ProcessBatchAsync(Token);

        result.Delivered.Should().Be(2);
        (await rig.RowAsync(ids[0])).Status.Should().Be("Done");
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-003")]
    public async Task A_failing_handler_schedules_a_retry_after_the_backoff_and_the_message_is_not_due_before_it()
    {
        await database.ResetOutboxAsync();
        var handler = new RecordingHandler { Behavior = (_, _) => throw new InvalidOperationException("the-handler-fault") };
        using var rig = new ProcessorRig(database, Make.Clock(), null, handler);
        var id = (await rig.SeedMessagesAsync(1)).Single();
        var failedAt = rig.Clock.GetUtcNow();

        var result = await rig.Processor.ProcessBatchAsync(Token);

        result.Should().Be(new OutboxBatchResult(1, 0, 1, 0));
        var row = await rig.RowAsync(id);
        row.Status.Should().Be("Pending");
        row.Attempts.Should().Be(1);
        row.NextAttemptAt.Should().Be(failedAt + rig.Options.BaseBackoff);
        row.LeasedUntil.Should().BeNull();
        row.LeaseId.Should().BeEmpty();
        row.LastError.Should().Be("InvalidOperationException");
        (await rig.Processor.ProcessBatchAsync(Token)).Claimed.Should().Be(0);

        rig.Clock.Advance(rig.Options.BaseBackoff);
        handler.Behavior = null;
        var retry = await rig.Processor.ProcessBatchAsync(Token);

        retry.Should().Be(new OutboxBatchResult(1, 1, 0, 0));
        var done = await rig.RowAsync(id);
        done.Status.Should().Be("Done");
        done.Attempts.Should().Be(2);
        done.LastError.Should().BeEmpty();
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-003")]
    public async Task The_backoff_doubles_with_each_failure_and_stops_at_the_maximum()
    {
        await database.ResetOutboxAsync();
        var handler = new RecordingHandler { Behavior = (_, _) => throw new InvalidOperationException("fault") };
        var options = new OutboxOptions { BaseBackoff = TimeSpan.FromSeconds(1), MaxBackoff = TimeSpan.FromSeconds(5), JitterRatio = 0, MaxAttempts = 10 };
        using var rig = new ProcessorRig(database, Make.Clock(), options, handler);
        var id = (await rig.SeedMessagesAsync(1)).Single();

        List<TimeSpan> delays = [];
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var before = rig.Clock.GetUtcNow();
            await rig.Processor.ProcessBatchAsync(Token);
            var row = await rig.RowAsync(id);
            delays.Add(row.NextAttemptAt - before);
            rig.Clock.Advance(row.NextAttemptAt - before);
        }

        delays.Should().Equal(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-003")]
    public async Task A_message_that_fails_too_often_moves_to_the_dead_letter_state_with_a_metric_and_an_error_and_is_never_claimed_again()
    {
        await database.ResetOutboxAsync();
        const string eventType = "Outbox.DeadLettered";
        var handler = new RecordingHandler { Behavior = (_, _) => throw new InvalidOperationException("fault") };
        var options = new OutboxOptions { MaxAttempts = 3, JitterRatio = 0 };
        using var rig = new ProcessorRig(database, Make.Clock(), options, handler);
        using var metrics = new MetricCapture(eventType);
        var id = (await rig.SeedMessagesAsync(1, eventType)).Single();

        var results = new List<OutboxBatchResult>();
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            results.Add(await rig.Processor.ProcessBatchAsync(Token));
            rig.Clock.Advance(rig.Options.MaxBackoff);
        }

        results.Should().Equal(new OutboxBatchResult(1, 0, 1, 0), new OutboxBatchResult(1, 0, 1, 0), new OutboxBatchResult(1, 0, 0, 1));
        var row = await rig.RowAsync(id);
        row.Status.Should().Be("DeadLetter");
        row.Attempts.Should().Be(3);
        row.LastError.Should().Be("InvalidOperationException");
        metrics.Total("catalog.outbox.failed").Should().Be(2);
        metrics.Total("catalog.outbox.dead_lettered").Should().Be(1);
        metrics.Total("catalog.outbox.delivered").Should().Be(0);
        rig.Logger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Error).Which.Message.Should().Contain(id.ToString());
        rig.Clock.Advance(TimeSpan.FromDays(30));
        (await rig.Processor.ProcessBatchAsync(Token)).Claimed.Should().Be(0);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-003")]
    public async Task A_delivery_adds_one_to_the_delivered_counter_of_its_event_type()
    {
        await database.ResetOutboxAsync();
        const string eventType = "Outbox.Delivered";
        using var rig = new ProcessorRig(database, Make.Clock(), null, new RecordingHandler());
        using var metrics = new MetricCapture(eventType);
        await rig.SeedMessagesAsync(2, eventType);

        await rig.Processor.ProcessBatchAsync(Token);

        metrics.Total("catalog.outbox.delivered").Should().Be(2);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-003")]
    public async Task A_retry_skips_the_handlers_that_already_took_the_message()
    {
        await database.ResetOutboxAsync();
        var first = new RecordingHandler("first");
        var second = new RecordingHandler("second");
        var secondFails = true;
        second.Behavior = (_, _) => secondFails ? Task.FromException(new InvalidOperationException("fault")) : Task.CompletedTask;
        using var rig = new ProcessorRig(database, Make.Clock(), null, first, second);
        var id = (await rig.SeedMessagesAsync(1)).Single();

        await rig.Processor.ProcessBatchAsync(Token);
        (await SqlProbe.ScalarAsync<int>(Connection, "SELECT COUNT(*) FROM catalog.OutboxReceipts")).Should().Be(1);
        secondFails = false;
        rig.Clock.Advance(rig.Options.BaseBackoff);
        await rig.Processor.ProcessBatchAsync(Token);

        first.Deliveries.Should().HaveCount(1);
        second.Deliveries.Should().HaveCount(2);
        (await SqlProbe.ScalarAsync<int>(Connection, "SELECT COUNT(*) FROM catalog.OutboxReceipts")).Should().Be(2);
        (await rig.RowAsync(id)).Status.Should().Be("Done");
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-003")]
    public async Task The_stored_error_and_the_log_text_name_the_type_of_the_fault_and_never_its_message()
    {
        await database.ResetOutboxAsync();
        const string secret = "Password=correct-horse-battery-staple";
        var handler = new RecordingHandler { Behavior = (_, _) => throw new InvalidOperationException(secret) };
        using var rig = new ProcessorRig(database, Make.Clock(), null, handler);
        var id = (await rig.SeedMessagesAsync(1)).Single();

        await rig.Processor.ProcessBatchAsync(Token);

        (await rig.RowAsync(id)).LastError.Should().Be("InvalidOperationException");
        rig.Logger.Entries.Should().NotBeEmpty().And.OnlyContain(entry => !entry.Message.Contains(secret, StringComparison.Ordinal));
        (await SqlProbe.RowsAsync(Connection, "SELECT Payload, LastError FROM catalog.OutboxMessages")).SelectMany(row => row).Should().NotContain(text => text.Contains(secret, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-003")]
    public async Task A_stop_after_delivery_and_before_the_done_mark_delivers_the_message_again_after_the_lease_ends()
    {
        await database.ResetOutboxAsync();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var handler = new RecordingHandler { Behavior = async (_, _) => await stop.CancelAsync() };
        using var rig = new ProcessorRig(database, Make.Clock(), null, handler);
        var id = (await rig.SeedMessagesAsync(1)).Single();
        var leasedAt = rig.Clock.GetUtcNow();

        var act = () => rig.Processor.ProcessBatchAsync(stop.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        handler.Deliveries.Should().HaveCount(1);
        var row = await rig.RowAsync(id);
        row.Status.Should().Be("Pending");
        row.Attempts.Should().Be(1);
        row.LeasedUntil.Should().Be(leasedAt + rig.Options.LeaseDuration);
        (await rig.Processor.ProcessBatchAsync(Token)).Claimed.Should().Be(0);

        handler.Behavior = null;
        rig.Clock.Advance(rig.Options.LeaseDuration);
        var again = await rig.Processor.ProcessBatchAsync(Token);

        again.Should().Be(new OutboxBatchResult(1, 1, 0, 0));
        handler.Deliveries.Should().HaveCount(2).And.OnlyContain(delivery => delivery.MessageId == id);
        var done = await rig.RowAsync(id);
        done.Status.Should().Be("Done");
        done.Attempts.Should().Be(2);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-003")]
    public async Task A_stop_keeps_the_lease_of_the_whole_batch_and_the_batch_is_delivered_after_the_lease_ends()
    {
        await database.ResetOutboxAsync();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var handler = new RecordingHandler
        {
            Behavior = async (_, token) =>
            {
                await stop.CancelAsync();
                token.ThrowIfCancellationRequested();
            },
        };
        using var rig = new ProcessorRig(database, Make.Clock(), null, handler);
        var ids = await rig.SeedMessagesAsync(3);

        var act = () => rig.Processor.ProcessBatchAsync(stop.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        handler.Deliveries.Should().HaveCount(1);
        foreach (var id in ids)
        {
            var row = await rig.RowAsync(id);
            row.Status.Should().Be("Pending");
            row.Attempts.Should().Be(1);
            row.LeasedUntil.Should().NotBeNull();
        }

        handler.Behavior = null;
        rig.Clock.Advance(rig.Options.LeaseDuration);
        var result = await rig.Processor.ProcessBatchAsync(Token);

        result.Should().Be(new OutboxBatchResult(3, 3, 0, 0));
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-003")]
    public async Task A_processor_that_dies_after_it_leased_a_message_loses_it_to_another_processor_after_the_lease_ends()
    {
        await database.ResetOutboxAsync();
        var clock = Make.Clock();
        var dead = new RecordingHandler("dead");
        var alive = new RecordingHandler("alive");
        using var deadRig = new ProcessorRig(database, clock, null, dead);
        using var aliveRig = new ProcessorRig(database, clock, null, alive);
        var id = (await deadRig.SeedMessagesAsync(1)).Single();

        var lost = await deadRig.Processor.ClaimAsync(Token);
        (await aliveRig.Processor.ProcessBatchAsync(Token)).Claimed.Should().Be(0);
        clock.Advance(deadRig.Options.LeaseDuration - TimeSpan.FromSeconds(1));
        (await aliveRig.Processor.ProcessBatchAsync(Token)).Claimed.Should().Be(0);
        clock.Advance(TimeSpan.FromSeconds(1));
        var result = await aliveRig.Processor.ProcessBatchAsync(Token);

        lost.Messages.Should().ContainSingle().Which.Attempts.Should().Be(1);
        result.Should().Be(new OutboxBatchResult(1, 1, 0, 0));
        alive.Deliveries.Should().ContainSingle().Which.MessageId.Should().Be(id);
        dead.Deliveries.Should().BeEmpty();
        (await deadRig.RowAsync(id)).Attempts.Should().Be(2);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-003")]
    public async Task A_processor_whose_lease_ended_cannot_finish_the_message_that_another_processor_now_holds()
    {
        await database.ResetOutboxAsync();
        var clock = Make.Clock();
        var slowHandler = new RecordingHandler();
        var otherHandler = new RecordingHandler();
        using var slow = new ProcessorRig(database, clock, null, slowHandler);
        using var other = new ProcessorRig(database, clock, null, otherHandler);
        var id = (await slow.SeedMessagesAsync(1)).Single();
        var slowLease = await slow.Processor.ClaimAsync(Token);
        clock.Advance(slow.Options.LeaseDuration);
        var otherLease = await other.Processor.ClaimAsync(Token);

        var late = await slow.Processor.DeliverAsync(slowLease, Token);

        late.Should().Be(new OutboxBatchResult(1, 0, 0, 0));
        var held = await slow.RowAsync(id);
        held.Status.Should().Be("Pending");
        held.LeaseId.Should().Be(otherLease.LeaseId.ToString());
        slow.Logger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Information);

        var result = await other.Processor.DeliverAsync(otherLease, Token);

        result.Should().Be(new OutboxBatchResult(1, 1, 0, 0));
        slowHandler.Deliveries.Should().HaveCount(1);
        otherHandler.Deliveries.Should().BeEmpty("the slow processor recorded the receipt of the handler, so the handler does not run twice");
        (await other.RowAsync(id)).Status.Should().Be("Done");
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-003")]
    public async Task Concurrent_processors_never_lease_the_same_message_twice()
    {
        await database.ResetOutboxAsync();
        var clock = Make.Clock();
        var options = new OutboxOptions { BatchSize = 3, JitterRatio = 0 };
        var rigs = Enumerable.Range(0, 4).Select(_ => new ProcessorRig(database, clock, options, new RecordingHandler())).ToList();
        try
        {
            var ids = await rigs[0].SeedMessagesAsync(12);

            var round = await Task.WhenAll(rigs.Select(rig => rig.Processor.ClaimAsync(Token)));
            var claimed = round.SelectMany(lease => lease.Messages).Select(message => message.Id).ToList();
            while (true)
            {
                var more = await rigs[0].Processor.ClaimAsync(Token);
                if (more.Messages.Count == 0)
                {
                    break;
                }

                claimed.AddRange(more.Messages.Select(message => message.Id));
            }

            claimed.Should().OnlyHaveUniqueItems().And.BeEquivalentTo(ids);
            (await SqlProbe.ScalarAsync<int>(Connection, "SELECT COUNT(*) FROM catalog.OutboxMessages WHERE Attempts = 1")).Should().Be(12);
            round.Select(lease => lease.LeaseId).Should().OnlyHaveUniqueItems();
        }
        finally
        {
            rigs.ForEach(rig => rig.Dispose());
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-003")]
    public async Task A_lease_skips_a_row_that_another_transaction_holds_and_never_waits_for_it()
    {
        await database.ResetOutboxAsync();
        using var rig = new ProcessorRig(database, Make.Clock(), null, new RecordingHandler());
        var ids = await rig.SeedMessagesAsync(3);
        await using var holder = new SqlConnection(Connection);
        await holder.OpenAsync(Token);
        await using var transaction = (SqlTransaction)await holder.BeginTransactionAsync(Token);
        await using (var hold = new SqlCommand("SELECT Id FROM catalog.OutboxMessages WITH (UPDLOCK, ROWLOCK) WHERE Id = @id", holder, transaction))
        {
            hold.Parameters.AddWithValue("@id", ids[0]);
            await hold.ExecuteScalarAsync(Token);
        }

        // The limit is a safety net: a lease that waited for the lock would be cancelled here instead of hanging the run.
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(Token);
        guard.CancelAfter(TimeSpan.FromSeconds(30));
        var skipped = await rig.Processor.ClaimAsync(guard.Token);
        await transaction.RollbackAsync(Token);
        var later = await rig.Processor.ClaimAsync(guard.Token);

        skipped.Messages.Select(message => message.Id).Should().Equal(ids.Skip(1));
        later.Messages.Select(message => message.Id).Should().Equal(ids[0]);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-003")]
    public async Task The_cleanup_deletes_finished_messages_past_the_retention_with_their_receipts_and_keeps_every_other_message()
    {
        await database.ResetOutboxAsync();
        var handler = new RecordingHandler();
        using var rig = new ProcessorRig(database, Make.Clock(), null, handler);
        var oldDone = Guid.NewGuid();
        var recentDone = Guid.NewGuid();
        var oldPending = Guid.NewGuid();
        var oldDead = Guid.NewGuid();
        var now = rig.Clock.GetUtcNow();
        await InsertAsync(oldDone, "Done", now.AddDays(-30), completedAt: now.AddDays(-8));
        await InsertAsync(recentDone, "Done", now.AddDays(-30), completedAt: now.AddDays(-1));
        await InsertAsync(oldPending, "Pending", now.AddDays(-30), completedAt: null);
        await InsertAsync(oldDead, "DeadLetter", now.AddDays(-30), completedAt: null);
        await SqlServerFixture.ExecuteAsync(Connection, $"INSERT INTO catalog.OutboxReceipts (MessageId, Handler, ProcessedAt) VALUES ('{oldDone}', 'recorder', '{now:O}')");

        var deleted = await rig.Maintenance().PurgeAsync(Token);

        deleted.Should().Be(1);
        (await SqlProbe.RowsAsync(Connection, "SELECT Id FROM catalog.OutboxMessages")).Select(row => Guid.Parse(row[0])).Should().BeEquivalentTo([recentDone, oldPending, oldDead]);
        (await SqlProbe.ScalarAsync<int>(Connection, "SELECT COUNT(*) FROM catalog.OutboxReceipts")).Should().Be(0);
    }

    private Task InsertAsync(Guid id, string status, DateTimeOffset createdAt, DateTimeOffset? completedAt)
    {
        const string emptyPayload = "{}";
        var completed = completedAt is { } done ? $"'{done:O}'" : "NULL";

        return SqlServerFixture.ExecuteAsync(
            Connection,
            $"""
            INSERT INTO catalog.OutboxMessages (Id, EventType, SchemaVersion, OccurredAt, Payload, [Status], Attempts, CreatedAt, NextAttemptAt, CompletedAt)
            VALUES ('{id}', 'ProductCreated', 1, '{createdAt:O}', '{emptyPayload}', '{status}', 1, '{createdAt:O}', '{createdAt:O}', {completed})
            """);
    }
}
