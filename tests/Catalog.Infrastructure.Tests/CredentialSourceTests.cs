using AwesomeAssertions;
using Catalog.Infrastructure.Credentials;
using Catalog.Infrastructure.Tests.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Catalog.Infrastructure.Tests;

/// <summary>
/// REQ-DATA-005: the credential sources cache one credential, the rotating source switches when its file changes and keeps the credential in use when the file is wrong, and no value of a credential reaches a log, a
/// message, or a printed object (plan section 8.4, ADR-0037). The rotating source is driven by <c>Refresh</c> and by a fake clock, and one test uses the real file watcher with a bounded wait on an event.
/// </summary>
public sealed class CredentialSourceTests
{
    private const string Secret = "Pw-Secret-test";

    private static readonly DatabaseCredential First = new("login_a", Secret);
    private static readonly DatabaseCredential Second = new("login_b", "Pw-Other-test");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Requirement", "REQ-DATA-005")]
    public void A_credential_prints_its_user_name_and_never_its_password()
    {
        var text = First.ToString();

        text.Should().Contain("login_a").And.Contain("[redacted]").And.NotContain(Secret);
        $"{First}".Should().NotContain(Secret);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-005")]
    public void Two_credentials_are_equal_when_both_parts_are_equal_and_a_refused_part_never_appears_in_the_message()
    {
        new DatabaseCredential("login_a", Secret).Should().Be(First);
        Second.Should().NotBe(First);

        var tooLong = () => new DatabaseCredential("login_a", new string('x', DatabaseCredential.MaxLength + 1));
        var empty = () => new DatabaseCredential(string.Empty, Secret);

        tooLong.Should().Throw<ArgumentException>().Which.Message.Should().NotContain("xxxx");
        empty.Should().Throw<ArgumentException>().Which.Message.Should().NotContain(Secret);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-005")]
    public void The_static_source_holds_one_credential_and_never_rotates()
    {
        var source = new StaticCredentialSource(First);
        var raised = 0;
        source.Rotated += (_, _) => raised++;
        source.Rotated -= (_, _) => raised++;

        source.Current.Should().BeSameAs(First);
        raised.Should().Be(0);
        ((Action)(() => _ = new StaticCredentialSource(null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-005")]
    public void The_rotating_source_reads_the_file_at_startup_and_accepts_any_case_of_the_property_names()
    {
        using var file = new TempCredentialFile();
        file.Write("""{"UserName":"login_a","PASSWORD":"Pw-Secret-test"}""");

        using var source = NewSource(file, out _, out _);

        source.Current.Should().Be(First);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-005")]
    public void A_file_that_cannot_be_used_at_startup_stops_the_host_with_a_message_that_holds_no_content()
    {
        using var file = new TempCredentialFile();
        var logger = new CapturingLogger<RotatingFileCredentialSource>();

        var missing = () => new RotatingFileCredentialSource(file.Path, TimeSpan.FromSeconds(30), false, new FakeTimeProvider(), logger);
        missing.Should().Throw<InvalidOperationException>().WithMessage("*the file does not exist*");

        file.Write($$"""{"username":"login_a","password":"{{Secret}}" """);
        var broken = () => new RotatingFileCredentialSource(file.Path, TimeSpan.FromSeconds(30), false, new FakeTimeProvider(), logger);

        var failure = broken.Should().Throw<InvalidOperationException>().Which;
        failure.Message.Should().Contain("not valid JSON").And.NotContain(Secret);
        failure.ToString().Should().NotContain(Secret);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-005")]
    public void A_changed_file_switches_the_source_once_and_names_the_old_and_the_new_credential()
    {
        using var file = new TempCredentialFile();
        file.Write(First);
        using var source = NewSource(file, out _, out _);
        List<CredentialRotatedEventArgs> events = [];
        source.Rotated += (_, args) => events.Add(args);

        file.Write(Second);
        var first = source.Refresh();
        var second = source.Refresh();

        first.Should().BeTrue();
        second.Should().BeFalse("the file did not change again");
        source.Current.Should().Be(Second);
        events.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Previous = First, Current = Second });
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-005")]
    public void A_file_that_an_agent_replaces_by_a_rename_is_read_like_any_other_change()
    {
        using var file = new TempCredentialFile();
        file.Write(First);
        using var source = NewSource(file, out _, out _);

        file.Replace(Second);

        source.Refresh().Should().BeTrue();
        source.Current.Should().Be(Second);
    }

    [Theory]
    [Trait("Requirement", "REQ-DATA-005")]
    [InlineData("""{"username":"login_b","password":"Pw-Other-te""")]
    [InlineData("""{"username":"login_b"}""")]
    [InlineData("""{"username":"login_b","password":42}""")]
    [InlineData("""{"username":"login_b","password":""}""")]
    [InlineData("""["login_b","Pw-Other-test"]""")]
    [InlineData("")]
    [InlineData("not json Pw-Other-test")]
    public void A_file_that_is_half_written_or_wrong_never_replaces_the_credential_in_use(string content)
    {
        using var file = new TempCredentialFile();
        file.Write(First);
        using var source = NewSource(file, out var logger, out _);
        var raised = 0;
        source.Rotated += (_, _) => raised++;

        file.Write(content);
        var changed = source.Refresh();

        changed.Should().BeFalse();
        raised.Should().Be(0);
        source.Current.Should().Be(First);
        logger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Warning);
        AllText(logger).Should().NotContain("Pw-Other").And.NotContain(Secret);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-005")]
    public void A_file_that_vanishes_or_grows_too_large_keeps_the_credential_in_use()
    {
        using var file = new TempCredentialFile();
        file.Write(First);
        using var source = NewSource(file, out var logger, out _);

        file.Write(new string(' ', 5000) + TempCredentialFile.Json(Second));
        source.Refresh().Should().BeFalse();
        File.Delete(file.Path);
        source.Refresh().Should().BeFalse();

        source.Current.Should().Be(First);
        logger.Entries.Select(entry => entry.Message).Should().Contain(message => message.Contains("too large", StringComparison.Ordinal))
            .And.Contain(message => message.Contains("does not exist", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-005")]
    public void The_poll_timer_reads_the_file_again_when_the_watcher_misses_a_change()
    {
        using var file = new TempCredentialFile();
        file.Write(First);
        var clock = new FakeTimeProvider();
        using var source = new RotatingFileCredentialSource(file.Path, TimeSpan.FromSeconds(30), false, clock, new CapturingLogger<RotatingFileCredentialSource>());
        var raised = 0;
        source.Rotated += (_, _) => raised++;

        file.Write(Second);
        clock.Advance(TimeSpan.FromSeconds(29));
        raised.Should().Be(0);
        clock.Advance(TimeSpan.FromSeconds(1));

        raised.Should().Be(1);
        source.Current.Should().Be(Second);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-005")]
    public async Task The_file_watcher_notices_a_change_without_waiting_for_the_poll()
    {
        using var file = new TempCredentialFile();
        file.Write(First);
        using var source = new RotatingFileCredentialSource(file.Path, TimeSpan.FromHours(1), true, new FakeTimeProvider(), new CapturingLogger<RotatingFileCredentialSource>());
        var seen = new TaskCompletionSource<CredentialRotatedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.Rotated += (_, args) => seen.TrySetResult(args);

        file.Replace(Second);

        // The limit is a safety net: the event ends the wait as soon as the watcher reports the change.
        var args = await seen.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
        args.Current.Should().Be(Second);
        source.Current.Should().Be(Second);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-005")]
    public void A_listener_that_fails_does_not_stop_the_other_listeners_or_the_switch_and_its_message_is_not_logged()
    {
        using var file = new TempCredentialFile();
        file.Write(First);
        using var source = NewSource(file, out var logger, out _);
        var reached = false;
        source.Rotated += (_, _) => throw new InvalidOperationException($"listener fault {Secret}");
        source.Rotated += (_, _) => reached = true;

        file.Write(Second);
        source.Refresh();

        reached.Should().BeTrue();
        source.Current.Should().Be(Second);
        logger.Entries.Should().Contain(entry => entry.Level == LogLevel.Error);
        AllText(logger).Should().Contain("InvalidOperationException").And.NotContain(Secret);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-005")]
    public void A_disposed_source_reads_nothing_and_can_be_disposed_again()
    {
        using var file = new TempCredentialFile();
        file.Write(First);
        var source = NewSource(file, out _, out _);

        source.Dispose();
        source.Dispose();
        file.Write(Second);

        source.Refresh().Should().BeFalse();
        source.Current.Should().Be(First);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-005")]
    public void A_missing_path_logger_or_clock_is_refused()
    {
        var path = () => new RotatingFileCredentialSource(" ", TimeSpan.FromSeconds(1), false, new FakeTimeProvider(), new CapturingLogger<RotatingFileCredentialSource>());
        var clock = () => new RotatingFileCredentialSource("x", TimeSpan.FromSeconds(1), false, null!, new CapturingLogger<RotatingFileCredentialSource>());
        var logger = () => new RotatingFileCredentialSource("x", TimeSpan.FromSeconds(1), false, new FakeTimeProvider(), null!);
        var args = () => new CredentialRotatedEventArgs(null!, First);

        path.Should().Throw<ArgumentException>();
        clock.Should().Throw<ArgumentNullException>();
        logger.Should().Throw<ArgumentNullException>();
        args.Should().Throw<ArgumentNullException>();
    }

    private static RotatingFileCredentialSource NewSource(TempCredentialFile file, out CapturingLogger<RotatingFileCredentialSource> logger, out FakeTimeProvider clock)
    {
        logger = new CapturingLogger<RotatingFileCredentialSource>();
        clock = new FakeTimeProvider();

        return new RotatingFileCredentialSource(file.Path, TimeSpan.FromSeconds(30), false, clock, logger);
    }

    private static string AllText(CapturingLogger<RotatingFileCredentialSource> logger) =>
        string.Join('\n', logger.Entries.Select(entry => entry.Message + entry.Exception));
}
