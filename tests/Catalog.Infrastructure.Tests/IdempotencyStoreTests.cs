using AwesomeAssertions;
using Catalog.Application.Idempotency;
using Catalog.Application.Results;
using Catalog.Domain.Tests.Support;
using Catalog.Infrastructure.Idempotency;
using Catalog.Infrastructure.Outbox;
using Catalog.Infrastructure.Tests.Support;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Catalog.Infrastructure.Tests;

/// <summary>
/// REQ-DATA-004: the store reserves a key for one caller, replays the first result of a finished request, answers a request that is still running with in-progress, answers a different payload with
/// reused, records the result in the transaction of the command, expires after 24 hours, and lets a second request take over a reservation that never finished (plan section 8.4, ADR-0036). Two requests that
/// begin together get one reservation, which the concurrent test proves against the real database.
/// </summary>
public sealed class IdempotencyStoreTests(CatalogDatabaseFixture database) : IClassFixture<CatalogDatabaseFixture>
{
    private static readonly string FingerprintA = new('a', 64);
    private static readonly string FingerprintB = new('b', 64);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string Connection => database.ConnectionString;

    [Fact]
    [Trait("Requirement", "REQ-DATA-004")]
    public async Task A_new_key_is_reserved_and_the_record_holds_the_caller_the_fingerprint_and_a_24_hour_expiry()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        await using var scope = new CatalogScope(database);

        var decision = await StoreOf(scope, clock).BeginAsync(Request("key-1"), Token);

        decision.Should().Be(new IdempotencyDecision(IdempotencyOutcome.Reserved));
        var row = (await SqlProbe.RowsAsync(Connection, "SELECT CallerId, [Key], Fingerprint, [Status], Response, DATEDIFF(HOUR, CreatedAt, ExpiresAt) FROM catalog.IdempotencyRecords")).Single();
        row.Should().Equal("caller-a", "key-1", FingerprintA, "InProgress", string.Empty, "24");
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-004")]
    public async Task The_same_request_while_the_first_one_runs_is_in_progress()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        await using var first = new CatalogScope(database);
        await using var second = new CatalogScope(database);
        await StoreOf(first, clock).BeginAsync(Request("key-1"), Token);

        var decision = await StoreOf(second, clock).BeginAsync(Request("key-1"), Token);

        decision.Should().Be(new IdempotencyDecision(IdempotencyOutcome.InProgress));
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-004")]
    public async Task The_same_key_with_a_different_payload_is_reused_while_the_first_request_runs_and_after_it_finished()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        await using var scope = new CatalogScope(database);
        var store = StoreOf(scope, clock);
        await store.BeginAsync(Request("key-1"), Token);

        (await store.BeginAsync(Request("key-1", fingerprint: FingerprintB), Token)).Outcome.Should().Be(IdempotencyOutcome.Reused);
        await FinishAsync(scope, store, Request("key-1"), "first-result");

        (await store.BeginAsync(Request("key-1", fingerprint: FingerprintB), Token)).Outcome.Should().Be(IdempotencyOutcome.Reused);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-004")]
    public async Task A_finished_request_replays_its_first_result_for_the_same_key_and_payload()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        await using var scope = new CatalogScope(database);
        var store = StoreOf(scope, clock);
        await store.BeginAsync(Request("key-1"), Token);
        await FinishAsync(scope, store, Request("key-1"), """{"id":"abc","name":"Gründer ✓"}""");
        await using var retry = new CatalogScope(database);

        var decision = await StoreOf(retry, clock).BeginAsync(Request("key-1"), Token);

        decision.Should().Be(new IdempotencyDecision(IdempotencyOutcome.Replay, """{"id":"abc","name":"Gründer ✓"}"""));
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-004")]
    public async Task A_long_response_is_stored_and_replayed_without_a_change()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        var response = string.Concat(Enumerable.Repeat("{\"line\":\"é✓\"}", 2000));
        await using var scope = new CatalogScope(database);
        var store = StoreOf(scope, clock);
        await store.BeginAsync(Request("key-1"), Token);
        await FinishAsync(scope, store, Request("key-1"), response);

        var decision = await store.BeginAsync(Request("key-1"), Token);

        decision.Response.Should().Be(response);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-004")]
    public async Task A_key_belongs_to_one_caller_so_two_callers_can_use_the_same_text()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        await using var scope = new CatalogScope(database);
        var store = StoreOf(scope, clock);

        var first = await store.BeginAsync(Request("key-1", caller: "caller-a"), Token);
        var second = await store.BeginAsync(Request("key-1", caller: "caller-b"), Token);

        first.Outcome.Should().Be(IdempotencyOutcome.Reserved);
        second.Outcome.Should().Be(IdempotencyOutcome.Reserved);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-004")]
    public async Task Keys_and_callers_are_compared_exactly_so_a_change_of_case_makes_a_different_key()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        await using var scope = new CatalogScope(database);
        var store = StoreOf(scope, clock);

        var outcomes = new[]
        {
            (await store.BeginAsync(Request("Key-A"), Token)).Outcome,
            (await store.BeginAsync(Request("key-a"), Token)).Outcome,
            (await store.BeginAsync(Request("Key-A", caller: "Caller-A"), Token)).Outcome,
            (await store.BeginAsync(Request("Key-A"), Token)).Outcome,
        };

        outcomes.Should().Equal(IdempotencyOutcome.Reserved, IdempotencyOutcome.Reserved, IdempotencyOutcome.Reserved, IdempotencyOutcome.InProgress);
        (await SqlProbe.ScalarAsync<int>(Connection, "SELECT COUNT(*) FROM catalog.IdempotencyRecords")).Should().Be(3);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-004")]
    public async Task A_key_with_quotes_and_sql_text_is_stored_as_data_and_changes_nothing_else()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        const string hostile = "x'; DROP TABLE catalog.Products; --\" [Key] % _ \\";
        await using var scope = new CatalogScope(database);
        var store = StoreOf(scope, clock);

        var first = await store.BeginAsync(Request(hostile, caller: "caller'; DELETE FROM catalog.Categories; --"), Token);
        var second = await store.BeginAsync(Request(hostile, caller: "caller'; DELETE FROM catalog.Categories; --"), Token);

        first.Outcome.Should().Be(IdempotencyOutcome.Reserved);
        second.Outcome.Should().Be(IdempotencyOutcome.InProgress);
        (await SqlProbe.ScalarAsync<int>(Connection, "SELECT COUNT(*) FROM sys.tables WHERE name IN ('Products', 'Categories') AND schema_id = SCHEMA_ID('catalog')")).Should().Be(2);
        (await SqlProbe.ScalarAsync<string>(Connection, "SELECT [Key] FROM catalog.IdempotencyRecords")).Should().Be(hostile);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-004")]
    public async Task Releasing_a_reservation_lets_the_caller_send_the_same_request_again_and_never_removes_a_finished_record()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        await using var scope = new CatalogScope(database);
        var store = StoreOf(scope, clock);
        await store.BeginAsync(Request("failed"), Token);

        await store.ReleaseAsync(Request("failed"), Token);

        (await store.BeginAsync(Request("failed"), Token)).Outcome.Should().Be(IdempotencyOutcome.Reserved);
        await store.BeginAsync(Request("done"), Token);
        await FinishAsync(scope, store, Request("done"), "result");

        await store.ReleaseAsync(Request("done"), Token);

        (await store.BeginAsync(Request("done"), Token)).Should().Be(new IdempotencyDecision(IdempotencyOutcome.Replay, "result"));
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-004")]
    public async Task A_release_by_a_request_with_another_fingerprint_keeps_the_reservation_of_the_first()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        await using var scope = new CatalogScope(database);
        var store = StoreOf(scope, clock);
        await store.BeginAsync(Request("key-1"), Token);

        await store.ReleaseAsync(Request("key-1", fingerprint: FingerprintB), Token);

        (await store.BeginAsync(Request("key-1"), Token)).Outcome.Should().Be(IdempotencyOutcome.InProgress);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-004")]
    public async Task A_record_expires_after_24_hours_and_the_key_can_be_used_again()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        await using var scope = new CatalogScope(database);
        var store = StoreOf(scope, clock);
        await store.BeginAsync(Request("key-1"), Token);
        await FinishAsync(scope, store, Request("key-1"), "first-result");

        clock.Advance(TimeSpan.FromHours(24) - TimeSpan.FromSeconds(1));
        (await store.BeginAsync(Request("key-1"), Token)).Outcome.Should().Be(IdempotencyOutcome.Replay);
        clock.Advance(TimeSpan.FromSeconds(1));
        var again = await store.BeginAsync(Request("key-1", fingerprint: FingerprintB), Token);

        again.Should().Be(new IdempotencyDecision(IdempotencyOutcome.Reserved));
        (await SqlProbe.RowsAsync(Connection, "SELECT Fingerprint, [Status], Response FROM catalog.IdempotencyRecords")).Single().Should().Equal(FingerprintB, "InProgress", string.Empty);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-004")]
    public async Task A_reservation_that_never_finished_blocks_the_key_until_the_timeout_and_then_another_request_takes_it_over()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        var options = new IdempotencyOptions();
        await using var crashed = new CatalogScope(database);
        await using var retry = new CatalogScope(database);
        await StoreOf(crashed, clock, options).BeginAsync(Request("key-1"), Token);

        clock.Advance(options.InProgressTimeout - TimeSpan.FromSeconds(1));
        (await StoreOf(retry, clock, options).BeginAsync(Request("key-1"), Token)).Outcome.Should().Be(IdempotencyOutcome.InProgress);
        clock.Advance(TimeSpan.FromSeconds(1));
        var taken = await StoreOf(retry, clock, options).BeginAsync(Request("key-1"), Token);

        taken.Outcome.Should().Be(IdempotencyOutcome.Reserved);
        (await SqlProbe.ScalarAsync<int>(Connection, "SELECT COUNT(*) FROM catalog.IdempotencyRecords")).Should().Be(1);
        (await StoreOf(retry, clock, options).BeginAsync(Request("key-1"), Token)).Outcome.Should().Be(IdempotencyOutcome.InProgress);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-004")]
    public async Task After_a_takeover_only_one_of_the_two_requests_can_commit_its_effect()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        var options = new IdempotencyOptions();
        var request = Request("key-1");
        await using var slow = new CatalogScope(database);
        await using var retry = new CatalogScope(database);
        var slowStore = StoreOf(slow, clock, options);
        var retryStore = StoreOf(retry, clock, options);
        (await slowStore.BeginAsync(request, Token)).Outcome.Should().Be(IdempotencyOutcome.Reserved);
        clock.Advance(options.InProgressTimeout);
        (await retryStore.BeginAsync(request, Token)).Outcome.Should().Be(IdempotencyOutcome.Reserved);

        var slowResult = await CommitAsync(slow, Make.Category(Counters.Next(), "Keyboards"), token => slowStore.CompleteAsync(request, "slow", token));
        var retryCommit = () => CommitAsync(retry, Make.Category(Counters.Next(), "Monitors"), token => retryStore.CompleteAsync(request, "retry", token));

        slowResult.IsSuccess.Should().BeTrue();
        await retryCommit.Should().ThrowAsync<InvalidOperationException>();
        (await SqlProbe.RowsAsync(Connection, "SELECT Name FROM catalog.Categories")).Select(row => row[0]).Should().Equal("Keyboards");
        (await SqlProbe.RowsAsync(Connection, "SELECT [Status], Response FROM catalog.IdempotencyRecords")).Single().Should().Equal("Completed", "slow");
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-004")]
    public async Task Eight_requests_that_begin_together_get_one_reservation()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        var request = Request("key-1");

        var decisions = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var scope = new CatalogScope(database);

            return await StoreOf(scope, clock).BeginAsync(request, Token);
        }));

        decisions.Count(decision => decision.Outcome == IdempotencyOutcome.Reserved).Should().Be(1);
        decisions.Count(decision => decision.Outcome == IdempotencyOutcome.InProgress).Should().Be(7);
        (await SqlProbe.ScalarAsync<int>(Connection, "SELECT COUNT(*) FROM catalog.IdempotencyRecords")).Should().Be(1);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-004")]
    public async Task Requests_with_two_payloads_that_begin_together_get_one_reservation_and_the_other_payload_is_reused()
    {
        await database.ResetAsync();
        var clock = Make.Clock();

        var decisions = await Task.WhenAll(Enumerable.Range(0, 8).Select(async index =>
        {
            await using var scope = new CatalogScope(database);
            var request = Request("key-1", fingerprint: index % 2 == 0 ? FingerprintA : FingerprintB);

            return (Request: request, Decision: await StoreOf(scope, clock).BeginAsync(request, Token));
        }));

        var winner = decisions.Single(entry => entry.Decision.Outcome == IdempotencyOutcome.Reserved).Request.Fingerprint;
        decisions.Where(entry => entry.Request.Fingerprint != winner).Should().OnlyContain(entry => entry.Decision.Outcome == IdempotencyOutcome.Reused);
        decisions.Where(entry => entry.Request.Fingerprint == winner && entry.Decision.Outcome != IdempotencyOutcome.Reserved).Should().OnlyContain(entry => entry.Decision.Outcome == IdempotencyOutcome.InProgress);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-004")]
    [Trait("Requirement", "REQ-DATA-002")]
    public async Task The_record_and_the_effect_commit_together()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        var request = Request("key-1");
        await using var scope = new CatalogScope(database, clock);
        var store = StoreOf(scope, clock);
        await store.BeginAsync(request, Token);

        var result = await CommitAsync(scope, Make.Category(Counters.Next(), "Keyboards"), token => store.CompleteAsync(request, "created", token));

        result.IsSuccess.Should().BeTrue();
        (await SqlProbe.RowsAsync(Connection, "SELECT [Status], Response FROM catalog.IdempotencyRecords")).Single().Should().Equal("Completed", "created");
        (await SqlProbe.ScalarAsync<int>(Connection, "SELECT COUNT(*) FROM catalog.Categories")).Should().Be(1);
        (await SqlProbe.ScalarAsync<int>(Connection, "SELECT COUNT(*) FROM catalog.OutboxMessages")).Should().Be(1);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-004")]
    [Trait("Requirement", "REQ-DATA-002")]
    public async Task A_rollback_after_the_record_was_written_removes_the_record_the_effect_and_the_outbox_row_together()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        var request = Request("key-1");
        await using var scope = new CatalogScope(database, clock);
        var store = StoreOf(scope, clock);
        await store.BeginAsync(request, Token);
        scope.UnitOfWork.BeforeCommit((_, token) => store.CompleteAsync(request, "created", token));
        scope.UnitOfWork.BeforeCommit((_, _) => throw new InvalidOperationException("The commit failed after the record was written."));

        var act = () => scope.UnitOfWork.ExecuteAsync(
            _ =>
            {
                scope.Categories.Add(Make.Category(Counters.Next(), "Keyboards"));

                return Task.FromResult(Result.Success(1));
            },
            Token);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await SqlProbe.RowsAsync(Connection, "SELECT [Status], Response FROM catalog.IdempotencyRecords")).Single().Should().Equal("InProgress", string.Empty);
        (await SqlProbe.ScalarAsync<int>(Connection, "SELECT COUNT(*) FROM catalog.Categories")).Should().Be(0);
        (await SqlProbe.ScalarAsync<int>(Connection, "SELECT COUNT(*) FROM catalog.OutboxMessages")).Should().Be(0);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-004")]
    public async Task Completing_a_request_that_holds_no_reservation_fails_and_rolls_the_effect_back()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        var request = Request("never-reserved");
        await using var scope = new CatalogScope(database);
        var store = StoreOf(scope, clock);

        var act = () => CommitAsync(scope, Make.Category(Counters.Next(), "Keyboards"), token => store.CompleteAsync(request, "created", token));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*reservation*");
        (await SqlProbe.ScalarAsync<int>(Connection, "SELECT COUNT(*) FROM catalog.Categories")).Should().Be(0);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-004")]
    public async Task The_cleanup_deletes_the_records_that_expired_and_keeps_the_others()
    {
        await database.ResetAsync();
        var clock = Make.Clock();
        await using var scope = new CatalogScope(database);
        var store = StoreOf(scope, clock);
        await store.BeginAsync(Request("old"), Token);
        clock.Advance(TimeSpan.FromHours(23));
        await store.BeginAsync(Request("fresh"), Token);
        clock.Advance(TimeSpan.FromHours(1));
        var maintenance = new CatalogMaintenance(database.NewContextFactory(), Microsoft.Extensions.Options.Options.Create(new OutboxOptions()), clock);

        var deleted = await maintenance.PurgeAsync(Token);

        deleted.Should().Be(1);
        (await SqlProbe.RowsAsync(Connection, "SELECT [Key] FROM catalog.IdempotencyRecords")).Select(row => row[0]).Should().Equal("fresh");
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-004")]
    public async Task A_missing_request_response_or_dependency_is_refused()
    {
        await using var scope = new CatalogScope(database);
        var store = StoreOf(scope, Make.Clock());
        var options = Microsoft.Extensions.Options.Options.Create(new IdempotencyOptions());

        await ((Func<Task>)(() => store.BeginAsync(null!, Token))).Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => store.CompleteAsync(null!, "response", Token))).Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => store.CompleteAsync(Request("key-1"), null!, Token))).Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => store.ReleaseAsync(null!, Token))).Should().ThrowAsync<ArgumentNullException>();
        ((Action)(() => _ = new IdempotencyStore(null!, Make.Clock(), options))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new IdempotencyStore(scope.Context, null!, options))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new IdempotencyStore(scope.Context, Make.Clock(), null!))).Should().Throw<ArgumentNullException>();
    }

    private static IdempotencyRequest Request(string key, string caller = "caller-a", string? fingerprint = null) =>
        IdempotencyKey.TryCreate(key, out var created)
            ? new IdempotencyRequest(created, caller, fingerprint ?? FingerprintA)
            : throw new InvalidOperationException($"'{key}' is not a valid idempotency key.");

    private static IdempotencyStore StoreOf(CatalogScope scope, FakeTimeProvider clock, IdempotencyOptions? options = null) =>
        new(scope.Context, clock, Microsoft.Extensions.Options.Options.Create(options ?? new IdempotencyOptions()));

    // The decorator pipeline does the same: it writes the effect and records the result in one unit of work.
    private static async Task<Result<int>> CommitAsync(CatalogScope scope, Catalog.Domain.Categories.Category effect, Func<CancellationToken, Task> record)
    {
        scope.UnitOfWork.BeforeCommit((_, token) => record(token));

        return await scope.UnitOfWork.ExecuteAsync(
            _ =>
            {
                scope.Categories.Add(effect);

                return Task.FromResult(Result.Success(1));
            },
            Token);
    }

    private static async Task FinishAsync(CatalogScope scope, IdempotencyStore store, IdempotencyRequest request, string response)
    {
        scope.UnitOfWork.BeforeCommit((_, token) => store.CompleteAsync(request, response, token));
        var result = await scope.UnitOfWork.ExecuteAsync(_ => Task.FromResult(Result.Success(1)), Token);
        result.IsSuccess.Should().BeTrue();
    }
}
