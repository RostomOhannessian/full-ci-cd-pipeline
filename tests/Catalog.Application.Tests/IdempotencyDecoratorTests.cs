using AwesomeAssertions;
using Catalog.Application.Idempotency;
using Catalog.Application.Results;
using Catalog.Application.Tests.Support;
using Catalog.CrossCutting.Decorators;
using Xunit;

namespace Catalog.Application.Tests;

/// <summary>
/// REQ-API-004, REQ-APP-002, and ADR-0028: a command with a key runs once for that key. The first request reserves the key and records its result in the transaction of the command, the same
/// request replays that result, a different request with the same key is refused, and a request that arrives while the first one runs is told to retry.
/// </summary>
public sealed class IdempotencyDecoratorTests
{
    private static readonly IdempotencyKey Key = Make.Key("key-1");

    [Fact]
    [Trait("Requirement", "REQ-API-004")]
    public async Task The_first_result_is_stored_as_json_when_the_transaction_commits()
    {
        var rig = new CommandRig();

        await rig.Pipeline.HandleAsync(new SampleCommand("Keyboard", Key), CancellationToken.None);

        rig.Store.StoredResponse("caller-1", "key-1").Should().Be("""{"Name":"Keyboard"}""");
    }

    [Fact]
    [Trait("Requirement", "REQ-API-004")]
    public async Task A_second_request_with_the_same_key_and_body_gets_the_first_result_and_runs_nothing()
    {
        var rig = new CommandRig();
        var first = await rig.Pipeline.HandleAsync(new SampleCommand("Keyboard", Key), CancellationToken.None);

        var second = await rig.Pipeline.HandleAsync(new SampleCommand("Keyboard", Key), CancellationToken.None);

        second.Value.Should().Be(first.Value);
        rig.Handler.Calls.Should().Be(1);
        rig.UnitOfWork.Saves.Should().Be(0);
    }

    [Fact]
    [Trait("Requirement", "REQ-API-004")]
    public async Task The_same_key_with_a_different_body_is_refused_as_a_reuse()
    {
        var rig = new CommandRig();
        await rig.Pipeline.HandleAsync(new SampleCommand("Keyboard", Key), CancellationToken.None);

        var result = await rig.Pipeline.HandleAsync(new SampleCommand("Keyboard 2", Key), CancellationToken.None);

        result.Error.Should().Be(new IdempotencyKeyReused());
        rig.Handler.Calls.Should().Be(1);
    }

    [Fact]
    [Trait("Requirement", "REQ-API-004")]
    public async Task A_request_that_arrives_while_the_first_runs_is_told_that_it_is_in_progress()
    {
        var journal = new Journal();
        var principal = TestPrincipal.With([], []);
        var decorator = new IdempotencyCommandDecorator<SampleCommand, SampleView>(new FakeCommandHandler(journal), new StubStore(IdempotencyOutcome.InProgress), new FakeUnitOfWork(journal), principal);

        var result = await decorator.HandleAsync(new SampleCommand("Keyboard", Key), CancellationToken.None);

        result.Error.Should().Be(new Conflict(ConflictReason.IdempotencyRequestInProgress));
    }

    [Fact]
    [Trait("Requirement", "REQ-API-004")]
    public async Task Two_callers_can_use_the_same_key_text()
    {
        var journal = new Journal();
        var store = new FakeIdempotencyStore(journal);
        var handler = new FakeCommandHandler(journal);
        var one = Pipeline(journal, store, handler, "caller-1");
        var two = Pipeline(journal, store, handler, "caller-2");

        await one.HandleAsync(new SampleCommand("Keyboard", Key), CancellationToken.None);
        await two.HandleAsync(new SampleCommand("Keyboard", Key), CancellationToken.None);

        handler.Calls.Should().Be(2);
        store.Requests.Select(request => request.CallerId).Should().Equal("caller-1", "caller-2");
    }

    [Fact]
    [Trait("Requirement", "REQ-API-004")]
    public async Task A_command_that_is_refused_releases_the_key_so_the_caller_can_try_again()
    {
        var calls = 0;
        var rig = new CommandRig(respond: command => ++calls == 1 ? Result.Failure<SampleView>(new Conflict(ConflictReason.SkuAlreadyExists)) : Result.Success(new SampleView(command.Name)));

        var first = await rig.Pipeline.HandleAsync(new SampleCommand("Keyboard", Key), CancellationToken.None);
        var retry = await rig.Pipeline.HandleAsync(new SampleCommand("Keyboard", Key), CancellationToken.None);

        first.Error.Should().Be(new Conflict(ConflictReason.SkuAlreadyExists));
        retry.IsSuccess.Should().BeTrue();
        rig.Journal.Entries.Should().Contain("idempotency:release");
        rig.Journal.Entries.Count(entry => entry == "idempotency:complete").Should().Be(1);
    }

    [Fact]
    [Trait("Requirement", "REQ-API-004")]
    public async Task A_command_that_throws_releases_the_key_and_the_exception_reaches_the_caller()
    {
        var rig = new CommandRig(respond: _ => throw new TimeoutException("The database did not answer."));

        var act = () => rig.Pipeline.HandleAsync(new SampleCommand("Keyboard", Key), CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
        rig.Journal.Entries.Should().Contain("idempotency:release").And.NotContain("idempotency:complete");
    }

    [Fact]
    [Trait("Requirement", "REQ-API-004")]
    public async Task The_fingerprint_is_the_same_for_the_same_request_and_differs_for_another()
    {
        var rig = new CommandRig();

        await rig.Pipeline.HandleAsync(new SampleCommand("Keyboard", Key), CancellationToken.None);
        await rig.Pipeline.HandleAsync(new SampleCommand("Keyboard", Key), CancellationToken.None);
        await rig.Pipeline.HandleAsync(new SampleCommand("Mouse", Key), CancellationToken.None);

        var fingerprints = rig.Store.Requests.Select(request => request.Fingerprint).ToList();
        fingerprints[0].Should().Be(fingerprints[1]).And.MatchRegex("^[0-9a-f]{64}$");
        fingerprints[2].Should().NotBe(fingerprints[0]);
    }

    [Fact]
    [Trait("Requirement", "REQ-API-004")]
    public async Task A_command_without_a_key_never_touches_the_store()
    {
        var rig = new CommandRig();

        await rig.Pipeline.HandleAsync(new SampleCommand("Keyboard"), CancellationToken.None);

        rig.Store.Requests.Should().BeEmpty();
    }

    [Fact]
    [Trait("Requirement", "REQ-API-004")]
    public async Task A_store_that_answers_with_something_unknown_or_an_empty_replay_is_a_programming_error()
    {
        var journal = new Journal();
        var principal = TestPrincipal.With([], []);
        var unknown = new IdempotencyCommandDecorator<SampleCommand, SampleView>(new FakeCommandHandler(journal), new StubStore((IdempotencyOutcome)99), new FakeUnitOfWork(journal), principal);
        var empty = new IdempotencyCommandDecorator<SampleCommand, SampleView>(new FakeCommandHandler(journal), new StubStore(IdempotencyOutcome.Replay), new FakeUnitOfWork(journal), principal);

        var unknownAct = () => unknown.HandleAsync(new SampleCommand("Keyboard", Key), CancellationToken.None);
        var emptyAct = () => empty.HandleAsync(new SampleCommand("Keyboard", Key), CancellationToken.None);

        await unknownAct.Should().ThrowAsync<InvalidOperationException>();
        await emptyAct.Should().ThrowAsync<InvalidOperationException>();
    }

    private static IdempotencyCommandDecorator<SampleCommand, SampleView> Pipeline(Journal journal, FakeIdempotencyStore store, FakeCommandHandler handler, string callerId) =>
        new IdempotencyCommandDecorator<SampleCommand, SampleView>(handler, store, new FakeUnitOfWork(journal), new TestPrincipal { CallerId = callerId });

    private sealed class StubStore(IdempotencyOutcome outcome) : IIdempotencyStore
    {
        public Task<IdempotencyDecision> BeginAsync(IdempotencyRequest request, CancellationToken cancellationToken) => Task.FromResult(new IdempotencyDecision(outcome));

        public Task CompleteAsync(IdempotencyRequest request, string response, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ReleaseAsync(IdempotencyRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
