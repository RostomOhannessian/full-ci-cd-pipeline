using AwesomeAssertions;
using Catalog.Application.Results;
using Catalog.Application.Tests.Support;
using Catalog.CrossCutting.Decorators;
using Xunit;

namespace Catalog.Application.Tests;

/// <summary>REQ-APP-002 and REQ-APP-003: the transaction decorator commits the work of a command and wakes the outbox after a success only, and the cache decorator serves marked queries from the cache.</summary>
public sealed class TransactionAndCacheDecoratorTests
{
    [Fact]
    [Trait("Requirement", "REQ-APP-002")]
    public async Task A_command_that_succeeds_commits_and_then_wakes_the_outbox()
    {
        var journal = new Journal();
        var decorator = new TransactionCommandDecorator<SampleCommand, SampleView>(new FakeCommandHandler(journal), new FakeUnitOfWork(journal), new FakeOutbox(journal));

        var result = await decorator.HandleAsync(new SampleCommand("Keyboard"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        journal.Entries.Should().Equal("uow:begin", "handler", "uow:hooks", "uow:commit", "outbox:dispatch");
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-002")]
    public async Task A_command_that_is_refused_rolls_back_and_never_wakes_the_outbox()
    {
        var journal = new Journal();
        var decorator = new TransactionCommandDecorator<SampleCommand, SampleView>(
            new FakeCommandHandler(journal, _ => Result.Failure<SampleView>(new NotFound("category"))),
            new FakeUnitOfWork(journal),
            new FakeOutbox(journal));

        var result = await decorator.HandleAsync(new SampleCommand("Keyboard"), CancellationToken.None);

        result.Error.Should().Be(new NotFound("category"));
        journal.Entries.Should().Equal("uow:begin", "handler", "uow:rollback");
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-002")]
    public async Task A_failing_wake_up_does_not_fail_a_command_that_committed()
    {
        var journal = new Journal();
        var decorator = new TransactionCommandDecorator<SampleCommand, SampleView>(
            new FakeCommandHandler(journal),
            new FakeUnitOfWork(journal),
            new FakeOutbox(journal, new InvalidOperationException("The dispatcher is not running.")));

        var result = await decorator.HandleAsync(new SampleCommand("Keyboard"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        journal.Entries.Should().EndWith("outbox:dispatch");
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-002")]
    public async Task A_cancelled_wake_up_is_not_swallowed()
    {
        var journal = new Journal();
        var decorator = new TransactionCommandDecorator<SampleCommand, SampleView>(
            new FakeCommandHandler(journal),
            new FakeUnitOfWork(journal),
            new FakeOutbox(journal, new OperationCanceledException()));

        var act = () => decorator.HandleAsync(new SampleCommand("Keyboard"), CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-003")]
    public async Task A_marked_query_is_loaded_once_and_then_served_from_the_cache()
    {
        var journal = new Journal();
        var handler = new FakeQueryHandler<SampleQuery>(journal);
        var decorator = new QueryCacheDecorator<SampleQuery, SampleView>(handler, new FakeCache(journal));

        await decorator.HandleAsync(new SampleQuery("1"), CancellationToken.None);
        var second = await decorator.HandleAsync(new SampleQuery("1"), CancellationToken.None);
        await decorator.HandleAsync(new SampleQuery("2"), CancellationToken.None);

        second.Value.Should().Be(new SampleView("loaded"));
        handler.Calls.Should().Be(2);
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-003")]
    public async Task A_refusal_is_not_cached_so_the_next_query_asks_again()
    {
        var journal = new Journal();
        var handler = new FakeQueryHandler<SampleQuery>(journal, _ => Result.Failure<SampleView>(new NotFound("product")));
        var decorator = new QueryCacheDecorator<SampleQuery, SampleView>(handler, new FakeCache(journal));

        await decorator.HandleAsync(new SampleQuery("1"), CancellationToken.None);
        await decorator.HandleAsync(new SampleQuery("1"), CancellationToken.None);

        handler.Calls.Should().Be(2);
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-003")]
    public async Task A_query_that_is_not_marked_never_reaches_the_cache()
    {
        var journal = new Journal();
        var decorator = new QueryCacheDecorator<UncachedQuery, SampleView>(new FakeQueryHandler<UncachedQuery>(journal), new FakeCache(journal));

        await decorator.HandleAsync(new UncachedQuery("1"), CancellationToken.None);

        journal.Entries.Should().Equal("handler");
    }
}
