using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using AwesomeAssertions;
using Catalog.Application.Results;
using Catalog.Domain.Products;
using Catalog.Domain.Tests.Support;
using Catalog.Infrastructure.Outbox;
using Catalog.Infrastructure.Tests.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Catalog.Infrastructure.Tests;

/// <summary>
/// REQ-DATA-003 and REQ-CACHE-002: after a commit the dispatcher gives each message that the commit wrote to every handler in process, and then it wakes the processor (plan section 8.4, ADR-0039). A handler that fails
/// never fails the dispatch and never stops another handler, the delivery writes no receipt, so the processor delivers the message again right after, and a run that did not commit delivers nothing. The tests use a real
/// SQL Server, a fake clock, and no sleep: the signal and the counters of the handlers say when something happened.
/// </summary>
public sealed class OutboxImmediateDispatchTests(CatalogDatabaseFixture database) : IClassFixture<CatalogDatabaseFixture>
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string Connection => database.ConnectionString;

    private static string Unique(string prefix) => $"{prefix}-{Counters.Next()}";

    // One category and one product, so the commit writes two outbox rows.
    private static async Task<Result<int>> WriteAsync(CatalogScope scope, FakeTimeProvider clock)
    {
        var category = Make.Category(Counters.Next(), "Keyboards");
        var product = Product.Create(TestIds.Product(Counters.Next()), Make.Sku(Counters.Sku()), Make.ProductName(), null, Make.Usd(10m), category, clock).Value;

        return await scope.UnitOfWork.ExecuteAsync(
            _ =>
            {
                scope.Categories.Add(category);
                scope.Products.Add(product);

                return Task.FromResult(Result.Success(1));
            },
            CancellationToken.None);
    }

    private async Task<IReadOnlyList<Guid>> RowIdsAsync() =>
        [.. (await SqlProbe.RowsAsync(Connection, "SELECT Id FROM catalog.OutboxMessages ORDER BY Id")).Select(row => Guid.Parse(row[0]))];

    [Fact]
    [Trait("Requirement", "REQ-DATA-003")]
    [Trait("Requirement", "REQ-CACHE-002")]
    public async Task A_commit_is_delivered_in_process_to_every_handler_once_in_the_order_of_its_rows_and_then_the_processor_is_woken()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        var first = new RecordingHandler(Unique("first"));
        var second = new RecordingHandler(Unique("second"));
        using var signal = new OutboxSignal();
        await using var scope = new CatalogScope(database, clock);
        var dispatcher = DispatcherSupport.Create(scope.Context, signal, first, second);
        (await WriteAsync(scope, clock)).IsSuccess.Should().BeTrue();

        await dispatcher.DispatchCommittedAsync(Token);
        await dispatcher.DispatchCommittedAsync(Token);

        var rows = await RowIdsAsync();
        rows.Should().HaveCount(2);
        foreach (var handler in new[] { first, second })
        {
            handler.Deliveries.Select(message => message.MessageId).Should().Equal(rows, "each handler gets the messages of the rows, in the order of their identifiers, and a second dispatch delivers nothing");
        }

        first.Deliveries.Select(message => message.EventType).Should().BeEquivalentTo(["CategoryCreated", "ProductCreated"]);
        (await signal.WaitAsync(Patience, new FakeTimeProvider(), Token)).Should().BeTrue("the dispatcher wakes the processor after the immediate delivery");
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-003")]
    [Trait("Requirement", "REQ-CACHE-002")]
    public async Task The_envelope_of_the_immediate_delivery_is_the_row_of_the_outbox_so_the_receipts_of_the_processor_line_up()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        var handler = new RecordingHandler(Unique("recorder"));
        using var signal = new OutboxSignal();
        await using var scope = new CatalogScope(database, clock);
        var dispatcher = DispatcherSupport.Create(scope.Context, signal, handler);
        await WriteAsync(scope, clock);

        await dispatcher.DispatchCommittedAsync(Token);

        foreach (var message in handler.Deliveries)
        {
            var stored = (await SqlProbe.RowsAsync(Connection, "SELECT EventType, SchemaVersion, Payload FROM catalog.OutboxMessages WHERE Id = @id", ("@id", message.MessageId))).Single();
            stored[0].Should().Be(message.EventType);
            stored[1].Should().Be(message.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
            stored[2].Should().Be(message.Payload);
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-003")]
    [Trait("Requirement", "REQ-CACHE-002")]
    public async Task A_handler_that_fails_is_counted_and_logged_and_never_stops_the_others_or_fails_the_dispatch()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        var failing = new RecordingHandler(Unique("failing")) { Behavior = (_, _) => throw new InvalidOperationException("The cache is down.") };
        var healthy = new RecordingHandler(Unique("healthy"));
        using var capture = new ImmediateCapture();
        using var signal = new OutboxSignal();
        var log = new CapturingLogger<OutboxDispatcher>();
        await using var scope = new CatalogScope(database, clock);
        var dispatcher = DispatcherSupport.Create(scope.Context, signal, log, failing, healthy);
        await WriteAsync(scope, clock);

        var dispatch = () => dispatcher.DispatchCommittedAsync(Token);

        await dispatch.Should().NotThrowAsync("the commit succeeded and the outbox still holds the messages");
        failing.Deliveries.Should().HaveCount(2, "the failing handler was tried for each message");
        healthy.Deliveries.Should().HaveCount(2, "a failing handler never stops another one");
        capture.Total(failing.Name, "failed").Should().Be(2);
        capture.Total(failing.Name, "delivered").Should().Be(0);
        capture.Total(healthy.Name, "delivered").Should().Be(2);
        log.Entries.Where(entry => entry.Level == LogLevel.Warning).Should().HaveCount(2).And.OnlyContain(entry => entry.Message.Contains(failing.Name, StringComparison.Ordinal));
        (await signal.WaitAsync(Patience, new FakeTimeProvider(), Token)).Should().BeTrue("the processor is woken even when a handler failed");
        (await SqlProbe.ScalarAsync<int>(Connection, "SELECT COUNT(*) FROM catalog.OutboxMessages WHERE [Status] = 'Pending'")).Should().Be(2, "the outbox keeps every message for the processor");
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-003")]
    public async Task The_immediate_delivery_writes_no_receipt_so_the_processor_delivers_each_message_again_and_records_the_receipts()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        var handler = new RecordingHandler(Unique("twice"));
        using var signal = new OutboxSignal();
        using var rig = new ProcessorRig(database, clock, null, handler);
        await using var scope = new CatalogScope(database, clock);
        var dispatcher = DispatcherSupport.Create(scope.Context, signal, handler);
        await WriteAsync(scope, clock);

        await dispatcher.DispatchCommittedAsync(Token);
        (await SqlProbe.ScalarAsync<int>(Connection, "SELECT COUNT(*) FROM catalog.OutboxReceipts")).Should().Be(0);
        var pass = await rig.Processor.ProcessBatchAsync(Token);

        pass.Delivered.Should().Be(2);
        handler.Deliveries.Should().HaveCount(4, "each message reached the handler in process and then through the outbox, which is the second delete of the cache");
        (await SqlProbe.ScalarAsync<int>(Connection, $"SELECT COUNT(*) FROM catalog.OutboxReceipts WHERE Handler = '{handler.Name}'")).Should().Be(2);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-003")]
    [Trait("Requirement", "REQ-CACHE-002")]
    public async Task A_run_that_did_not_commit_delivers_nothing_whether_it_returned_a_refusal_or_failed_after_the_save()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        var handler = new RecordingHandler(Unique("rolled-back"));
        using var signal = new OutboxSignal();
        await using var scope = new CatalogScope(database, clock);
        var dispatcher = DispatcherSupport.Create(scope.Context, signal, handler);

        var refused = await scope.UnitOfWork.ExecuteAsync<int>(
            async token =>
            {
                scope.Categories.Add(Make.Category(Counters.Next()));
                await scope.UnitOfWork.SaveChangesAsync(token);

                return Result.Failure<int>(new NotFound("category"));
            },
            CancellationToken.None);
        await dispatcher.DispatchCommittedAsync(Token);

        scope.UnitOfWork.BeforeCommit((_, _) => throw new InvalidOperationException("The hook failed after the save."));
        var failed = () => scope.UnitOfWork.ExecuteAsync(
            _ =>
            {
                scope.Categories.Add(Make.Category(Counters.Next()));

                return Task.FromResult(Result.Success(1));
            },
            CancellationToken.None);
        await failed.Should().ThrowAsync<InvalidOperationException>();
        await dispatcher.DispatchCommittedAsync(Token);

        refused.IsSuccess.Should().BeFalse();
        handler.Deliveries.Should().BeEmpty("a message of a transaction that rolled back never reaches a handler");
        (await SqlProbe.ScalarAsync<int>(Connection, "SELECT COUNT(*) FROM catalog.OutboxMessages")).Should().Be(0);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-003")]
    public async Task Two_scopes_deliver_only_the_messages_of_their_own_commit()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        var handlerA = new RecordingHandler(Unique("scope-a"));
        var handlerB = new RecordingHandler(Unique("scope-b"));
        using var signal = new OutboxSignal();
        await using var scopeA = new CatalogScope(database, clock);
        await using var scopeB = new CatalogScope(database, clock);
        var dispatcherA = DispatcherSupport.Create(scopeA.Context, signal, handlerA);
        var dispatcherB = DispatcherSupport.Create(scopeB.Context, signal, handlerB);
        await WriteAsync(scopeA, clock);
        await WriteAsync(scopeB, clock);

        await dispatcherA.DispatchCommittedAsync(Token);

        handlerA.Deliveries.Should().HaveCount(2);
        handlerB.Deliveries.Should().BeEmpty("the other scope has not dispatched, and it never receives the messages of the first");

        await dispatcherB.DispatchCommittedAsync(Token);

        handlerB.Deliveries.Should().HaveCount(2);
        handlerB.Deliveries.Select(message => message.MessageId).Should().NotIntersectWith(handlerA.Deliveries.Select(message => message.MessageId));
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-003")]
    public async Task A_cancelled_request_stops_the_delivery_and_still_wakes_the_processor_that_delivers_the_rest()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        using var cancel = new CancellationTokenSource();
        var cancelling = new RecordingHandler(Unique("cancelling"))
        {
            Behavior = async (_, token) =>
            {
                await cancel.CancelAsync();
                token.ThrowIfCancellationRequested();
            },
        };
        var after = new RecordingHandler(Unique("after"));
        using var signal = new OutboxSignal();
        await using var scope = new CatalogScope(database, clock);
        var dispatcher = DispatcherSupport.Create(scope.Context, signal, cancelling, after);
        await WriteAsync(scope, clock);

        var dispatch = () => dispatcher.DispatchCommittedAsync(cancel.Token);

        await dispatch.Should().ThrowAsync<OperationCanceledException>();
        after.Deliveries.Should().BeEmpty("the cancellation ended the delivery, and the outbox holds the messages");
        (await signal.WaitAsync(Patience, new FakeTimeProvider(), Token)).Should().BeTrue();
    }

    /// <summary>Counts the immediate deliveries by handler and outcome, from the meter of the outbox.</summary>
    private sealed class ImmediateCapture : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly ConcurrentDictionary<(string Handler, string Outcome), long> _totals = new();

        public ImmediateCapture()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == OutboxMetrics.MeterName && instrument.Name == "catalog.outbox.immediate")
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            {
                string? handler = null;
                string? outcome = null;

                foreach (var tag in tags)
                {
                    handler = tag.Key == "handler" ? tag.Value?.ToString() : handler;
                    outcome = tag.Key == "outcome" ? tag.Value?.ToString() : outcome;
                }

                _totals.AddOrUpdate((handler ?? string.Empty, outcome ?? string.Empty), value, (_, total) => total + value);
            });
            _listener.Start();
        }

        public long Total(string handler, string outcome) => _totals.GetValueOrDefault((handler, outcome));

        public void Dispose() => _listener.Dispose();
    }
}
