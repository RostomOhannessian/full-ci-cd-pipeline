using AwesomeAssertions;
using Catalog.Infrastructure.Outbox;
using Catalog.Infrastructure.Tests.Support;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Catalog.Infrastructure.Tests;

/// <summary>
/// REQ-DATA-003: a commit wakes the background service through the dispatcher, a wake-up that nobody waits for is kept, many wake-ups count as one, and a wait that times out never takes a later wake-up (plan section 8.4,
/// ADR-0035). The tests move a fake clock to end a wait, so none of them sleeps.
/// </summary>
public sealed class OutboxSignalTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Requirement", "REQ-DATA-003")]
    public async Task A_wake_up_that_arrives_before_the_wait_ends_the_wait_at_once()
    {
        using var signal = new OutboxSignal();
        var clock = new FakeTimeProvider();
        signal.Notify();

        (await signal.WaitAsync(Timeout, clock, Token)).Should().BeTrue();
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-003")]
    public async Task A_wake_up_during_the_wait_ends_it()
    {
        using var signal = new OutboxSignal();
        var clock = new FakeTimeProvider();
        var wait = signal.WaitAsync(Timeout, clock, Token);
        wait.IsCompleted.Should().BeFalse();

        signal.Notify();

        (await wait).Should().BeTrue();
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-003")]
    public async Task Many_wake_ups_count_as_one()
    {
        using var signal = new OutboxSignal();
        var clock = new FakeTimeProvider();
        signal.Notify();
        signal.Notify();
        signal.Notify();
        (await signal.WaitAsync(Timeout, clock, Token)).Should().BeTrue();

        var second = signal.WaitAsync(Timeout, clock, Token);
        clock.Advance(Timeout);

        (await second).Should().BeFalse();
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-003")]
    public async Task A_wait_that_times_out_returns_false_and_does_not_take_the_next_wake_up()
    {
        using var signal = new OutboxSignal();
        var clock = new FakeTimeProvider();
        var wait = signal.WaitAsync(Timeout, clock, Token);
        clock.Advance(Timeout);
        (await wait).Should().BeFalse();

        signal.Notify();

        (await signal.WaitAsync(Timeout, clock, Token)).Should().BeTrue();
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-003")]
    public async Task A_cancelled_wait_throws_and_does_not_take_the_next_wake_up()
    {
        using var signal = new OutboxSignal();
        var clock = new FakeTimeProvider();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var wait = signal.WaitAsync(Timeout, clock, cancel.Token);
        await cancel.CancelAsync();

        var act = () => wait;
        await act.Should().ThrowAsync<OperationCanceledException>();
        signal.Notify();

        (await signal.WaitAsync(Timeout, clock, Token)).Should().BeTrue();
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-003")]
    public async Task The_dispatcher_wakes_the_service_after_a_commit()
    {
        using var signal = new OutboxSignal();
        await using var context = DispatcherSupport.NoDatabase();
        var dispatcher = DispatcherSupport.Create(context, signal);

        await dispatcher.DispatchCommittedAsync(Token);

        (await signal.WaitAsync(Timeout, new FakeTimeProvider(), Token)).Should().BeTrue();
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-003")]
    public async Task A_missing_signal_clock_or_dispatcher_signal_is_refused()
    {
        using var signal = new OutboxSignal();
        await using var context = DispatcherSupport.NoDatabase();
        var clock = () => signal.WaitAsync(Timeout, null!, Token);
        var noSignal = () => new OutboxDispatcher(context, [], null!, new OutboxMetrics(), new CapturingLogger<OutboxDispatcher>());
        var noContext = () => new OutboxDispatcher(null!, [], signal, new OutboxMetrics(), new CapturingLogger<OutboxDispatcher>());
        var noHandlers = () => new OutboxDispatcher(context, null!, signal, new OutboxMetrics(), new CapturingLogger<OutboxDispatcher>());
        var noMetrics = () => new OutboxDispatcher(context, [], signal, null!, new CapturingLogger<OutboxDispatcher>());
        var noLog = () => new OutboxDispatcher(context, [], signal, new OutboxMetrics(), null!);

        await clock.Should().ThrowAsync<ArgumentNullException>();
        noSignal.Should().Throw<ArgumentNullException>();
        noContext.Should().Throw<ArgumentNullException>();
        noHandlers.Should().Throw<ArgumentNullException>();
        noMetrics.Should().Throw<ArgumentNullException>();
        noLog.Should().Throw<ArgumentNullException>();
    }
}
