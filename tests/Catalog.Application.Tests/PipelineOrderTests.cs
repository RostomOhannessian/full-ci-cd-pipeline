using AwesomeAssertions;
using Catalog.Application.Idempotency;
using Catalog.Application.Results;
using Catalog.Application.Tests.Support;
using Xunit;

namespace Catalog.Application.Tests;

/// <summary>
/// REQ-APP-002 and REQ-APP-003: the pipeline types compose the decorators in the approved order, outermost first, and a decorator that refuses a request stops the ones inside it. Each test
/// runs a request through a chain that the production <c>CommandPipeline</c> or <c>QueryPipeline</c> built over fakes, and reads the order from a shared journal (ADR-0032).
/// </summary>
public sealed class PipelineOrderTests
{
    private static readonly IdempotencyKey Key = Make.Key("key-1");

    [Fact]
    [Trait("Requirement", "REQ-APP-002")]
    public async Task A_command_with_a_key_passes_through_the_decorators_in_the_approved_order()
    {
        var rig = new CommandRig();

        var result = await rig.Pipeline.HandleAsync(new SampleCommand("Keyboard", Key), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        rig.Journal.Entries.Should().Equal(
            "telemetry:begin SampleCommand Command",
            "validation",
            "authorization",
            "idempotency:begin",
            "uow:begin",
            "handler",
            "uow:hooks",
            "idempotency:complete",
            "uow:commit",
            "outbox:dispatch",
            "telemetry:succeeded",
            "telemetry:end");
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-002")]
    public async Task A_command_without_a_key_skips_idempotency_and_keeps_the_rest_of_the_order()
    {
        var rig = new CommandRig();

        await rig.Pipeline.HandleAsync(new SampleCommand("Keyboard"), CancellationToken.None);

        rig.Journal.Entries.Should().Equal(
            "telemetry:begin SampleCommand Command",
            "validation",
            "authorization",
            "uow:begin",
            "handler",
            "uow:hooks",
            "uow:commit",
            "outbox:dispatch",
            "telemetry:succeeded",
            "telemetry:end");
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-002")]
    public async Task An_invalid_command_is_refused_after_telemetry_and_before_authorization()
    {
        var rig = new CommandRig(caller: Callers.Anonymous);

        var result = await rig.Pipeline.HandleAsync(new SampleCommand(" ", Key), CancellationToken.None);

        result.Error.Should().BeOfType<ValidationFailed>();
        rig.Journal.Entries.Should().Equal("telemetry:begin SampleCommand Command", "validation", "telemetry:refused ValidationFailed", "telemetry:end");
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-002")]
    public async Task A_command_that_the_caller_may_not_run_is_refused_before_idempotency_and_the_transaction()
    {
        var rig = new CommandRig(caller: Callers.Reader);

        var result = await rig.Pipeline.HandleAsync(new SampleCommand("Keyboard", Key), CancellationToken.None);

        result.Error.Should().Be(new Forbidden(Application.Security.AccessLevel.Write, ForbiddenReason.MissingRole));
        rig.Journal.Entries.Should().Equal("telemetry:begin SampleCommand Command", "validation", "authorization", "telemetry:refused Forbidden", "telemetry:end");
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-002")]
    public async Task A_replayed_command_returns_the_first_result_without_a_transaction_or_the_handler()
    {
        var rig = new CommandRig();
        await rig.Pipeline.HandleAsync(new SampleCommand("Keyboard", Key), CancellationToken.None);
        var firstRun = rig.Journal.Entries.Count;

        var replay = await rig.Pipeline.HandleAsync(new SampleCommand("Keyboard", Key), CancellationToken.None);

        replay.Value.Should().Be(new SampleView("Keyboard"));
        rig.Handler.Calls.Should().Be(1);
        rig.Journal.Entries.Skip(firstRun).Should().Equal(
            "telemetry:begin SampleCommand Command",
            "validation",
            "authorization",
            "idempotency:begin",
            "telemetry:succeeded",
            "telemetry:end");
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-003")]
    public async Task A_cached_query_passes_through_telemetry_validation_authorization_and_the_cache_before_the_handler()
    {
        var rig = new QueryRig<SampleQuery>(caller: Callers.Reader, validators: journal => [new SampleQueryValidator(journal)]);

        var result = await rig.Pipeline.HandleAsync(new SampleQuery("1"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        rig.Journal.Entries.Should().Equal(
            "telemetry:begin SampleQuery Query",
            "validation",
            "authorization",
            "cache:get sample:1",
            "handler",
            "cache:store",
            "telemetry:succeeded",
            "telemetry:end");
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-003")]
    public async Task A_cache_hit_skips_the_handler_but_not_authorization()
    {
        var rig = new QueryRig<SampleQuery>(caller: Callers.Reader);
        await rig.Pipeline.HandleAsync(new SampleQuery("1"), CancellationToken.None);
        var firstRun = rig.Journal.Entries.Count;

        await rig.Pipeline.HandleAsync(new SampleQuery("1"), CancellationToken.None);

        rig.Handler.Calls.Should().Be(1);
        rig.Journal.Entries.Skip(firstRun).Should().Equal("telemetry:begin SampleQuery Query", "authorization", "cache:get sample:1", "telemetry:succeeded", "telemetry:end");
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-003")]
    public async Task A_query_that_is_not_marked_skips_the_cache()
    {
        var rig = new QueryRig<UncachedQuery>();

        await rig.Pipeline.HandleAsync(new UncachedQuery("1"), CancellationToken.None);

        rig.Journal.Entries.Should().Equal("telemetry:begin UncachedQuery Query", "authorization", "handler", "telemetry:succeeded", "telemetry:end");
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-003")]
    public async Task A_query_that_the_caller_may_not_run_is_refused_before_the_cache_is_read()
    {
        var rig = new QueryRig<UncachedQuery>(caller: Callers.Editor);

        var result = await rig.Pipeline.HandleAsync(new UncachedQuery("1"), CancellationToken.None);

        result.Error.Should().Be(new Forbidden(Application.Security.AccessLevel.Admin, ForbiddenReason.MissingRole));
        rig.Journal.Entries.Should().Equal("telemetry:begin UncachedQuery Query", "authorization", "telemetry:refused Forbidden", "telemetry:end");
    }
}
