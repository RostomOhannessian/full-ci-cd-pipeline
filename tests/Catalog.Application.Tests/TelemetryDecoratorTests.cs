using AwesomeAssertions;
using Catalog.Application.Results;
using Catalog.Application.Tests.Support;
using Catalog.CrossCutting.Decorators;
using Xunit;

namespace Catalog.Application.Tests;

/// <summary>REQ-APP-002 and REQ-APP-003: the outermost decorator reports exactly one outcome for each run of a command or a query, and a fault is reported and thrown again.</summary>
public sealed class TelemetryDecoratorTests
{
    [Fact]
    [Trait("Requirement", "REQ-APP-002")]
    public async Task A_command_that_succeeds_is_observed_as_a_success_and_the_observation_ends()
    {
        var journal = new Journal();
        var decorator = new TelemetryCommandDecorator<SampleCommand, SampleView>(new FakeCommandHandler(journal), new FakeTelemetry(journal));

        await decorator.HandleAsync(new SampleCommand("Keyboard"), CancellationToken.None);

        journal.Entries.Should().Equal("telemetry:begin SampleCommand Command", "handler", "telemetry:succeeded", "telemetry:end");
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-002")]
    public async Task A_command_that_is_refused_is_observed_as_a_refusal_and_the_result_is_unchanged()
    {
        var journal = new Journal();
        var refusal = Result.Failure<SampleView>(new NotFound("product"));
        var decorator = new TelemetryCommandDecorator<SampleCommand, SampleView>(new FakeCommandHandler(journal, _ => refusal), new FakeTelemetry(journal));

        var result = await decorator.HandleAsync(new SampleCommand("Keyboard"), CancellationToken.None);

        result.Should().BeSameAs(refusal);
        journal.Entries.Should().Equal("telemetry:begin SampleCommand Command", "handler", "telemetry:refused NotFound", "telemetry:end");
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-002")]
    public async Task A_command_that_throws_is_observed_as_a_fault_and_the_exception_is_thrown_again()
    {
        var journal = new Journal();
        var decorator = new TelemetryCommandDecorator<SampleCommand, SampleView>(
            new FakeCommandHandler(journal, _ => throw new TimeoutException("The database did not answer.")),
            new FakeTelemetry(journal));

        var act = () => decorator.HandleAsync(new SampleCommand("Keyboard"), CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
        journal.Entries.Should().Equal("telemetry:begin SampleCommand Command", "handler", "telemetry:faulted TimeoutException", "telemetry:end");
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-003")]
    public async Task A_query_is_observed_as_a_query_with_its_own_name()
    {
        var journal = new Journal();
        var decorator = new TelemetryQueryDecorator<SampleQuery, SampleView>(new FakeQueryHandler<SampleQuery>(journal), new FakeTelemetry(journal));

        await decorator.HandleAsync(new SampleQuery("1"), CancellationToken.None);

        journal.Entries.Should().Equal("telemetry:begin SampleQuery Query", "handler", "telemetry:succeeded", "telemetry:end");
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-003")]
    public async Task A_query_that_is_refused_or_throws_is_observed_and_nothing_is_hidden()
    {
        var journal = new Journal();
        var refused = new TelemetryQueryDecorator<SampleQuery, SampleView>(
            new FakeQueryHandler<SampleQuery>(journal, _ => Result.Failure<SampleView>(new NotFound("product"))),
            new FakeTelemetry(journal));
        var faulted = new TelemetryQueryDecorator<SampleQuery, SampleView>(
            new FakeQueryHandler<SampleQuery>(journal, _ => throw new InvalidOperationException("broken")),
            new FakeTelemetry(journal));

        var result = await refused.HandleAsync(new SampleQuery("1"), CancellationToken.None);
        var act = () => faulted.HandleAsync(new SampleQuery("1"), CancellationToken.None);

        result.Error.Should().Be(new NotFound("product"));
        await act.Should().ThrowAsync<InvalidOperationException>();
        journal.Entries.Should().Contain(["telemetry:refused NotFound", "telemetry:faulted InvalidOperationException"]);
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-002")]
    public void Every_decorator_needs_its_collaborators()
    {
        var journal = new Journal();
        var handler = new FakeCommandHandler(journal);
        var query = new FakeQueryHandler<SampleQuery>(journal);
        var principal = TestPrincipal.With([], []);

        Func<object>[] constructions =
        [
            () => new TelemetryCommandDecorator<SampleCommand, SampleView>(null!, new FakeTelemetry(journal)),
            () => new TelemetryCommandDecorator<SampleCommand, SampleView>(handler, null!),
            () => new TelemetryQueryDecorator<SampleQuery, SampleView>(null!, new FakeTelemetry(journal)),
            () => new TelemetryQueryDecorator<SampleQuery, SampleView>(query, null!),
            () => new ValidationCommandDecorator<SampleCommand, SampleView>(null!, []),
            () => new ValidationCommandDecorator<SampleCommand, SampleView>(handler, null!),
            () => new ValidationQueryDecorator<SampleQuery, SampleView>(null!, []),
            () => new ValidationQueryDecorator<SampleQuery, SampleView>(query, null!),
            () => new AuthorizationCommandDecorator<SampleCommand, SampleView>(null!, principal),
            () => new AuthorizationCommandDecorator<SampleCommand, SampleView>(handler, null!),
            () => new AuthorizationQueryDecorator<SampleQuery, SampleView>(null!, principal),
            () => new AuthorizationQueryDecorator<SampleQuery, SampleView>(query, null!),
            () => new IdempotencyCommandDecorator<SampleCommand, SampleView>(null!, new FakeIdempotencyStore(journal), new FakeUnitOfWork(journal), principal),
            () => new IdempotencyCommandDecorator<SampleCommand, SampleView>(handler, null!, new FakeUnitOfWork(journal), principal),
            () => new IdempotencyCommandDecorator<SampleCommand, SampleView>(handler, new FakeIdempotencyStore(journal), null!, principal),
            () => new IdempotencyCommandDecorator<SampleCommand, SampleView>(handler, new FakeIdempotencyStore(journal), new FakeUnitOfWork(journal), null!),
            () => new TransactionCommandDecorator<SampleCommand, SampleView>(null!, new FakeUnitOfWork(journal), new FakeOutbox(journal)),
            () => new TransactionCommandDecorator<SampleCommand, SampleView>(handler, null!, new FakeOutbox(journal)),
            () => new TransactionCommandDecorator<SampleCommand, SampleView>(handler, new FakeUnitOfWork(journal), null!),
            () => new QueryCacheDecorator<SampleQuery, SampleView>(null!, new FakeCache(journal)),
            () => new QueryCacheDecorator<SampleQuery, SampleView>(query, null!),
        ];

        foreach (var construction in constructions)
        {
            construction.Should().Throw<ArgumentNullException>();
        }
    }
}
