using AwesomeAssertions;
using Catalog.Domain.Tests.Support;
using Catalog.Infrastructure.Credentials;
using Catalog.Infrastructure.Outbox;
using Catalog.Infrastructure.Tests.Support;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Catalog.Infrastructure.Tests;

/// <summary>
/// REQ-DATA-005: an interceptor gives each new connection the cached credential, rotation switches new connections without disabling pooling, the pool of the old credential is cleared after the grace period and the
/// old login ends with no session, work that started on the old login finishes, the old login can then be revoked, and no value of a credential reaches a log or a message (plan section 8.4, ADR-0037, spike 1.e). The
/// asserts read <c>sys.dm_exec_sessions</c> on the server, because the server is the proof that a pool is gone. A fake clock ends the grace period, and the files are temporary and held by the test.
/// </summary>
public sealed class CredentialRotationTests(CatalogDatabaseFixture database) : IClassFixture<CatalogDatabaseFixture>
{
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(60);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Requirement", "REQ-DATA-005")]
    public async Task The_interceptor_gives_each_new_connection_the_credential_of_the_source()
    {
        var login = await SqlLogins.CreateAsync(database, "static");
        var source = new StaticCredentialSource(login);

        for (var index = 0; index < 3; index++)
        {
            await using var context = SqlLogins.NewContext(database, source);
            (await SqlLogins.LoginOfAsync(context, Token)).Should().Be(login.UserName);
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-005")]
    public async Task A_rotation_switches_new_connections_to_the_new_login_while_pooling_stays_on()
    {
        var a = await SqlLogins.CreateAsync(database, "a");
        var b = await SqlLogins.CreateAsync(database, "b");
        using var rig = new Rig(database, a, Grace);

        var before = await RunAsync(rig.Source, 10);
        rig.File.Replace(b);
        rig.Source.Refresh().Should().BeTrue();
        var after = await RunAsync(rig.Source, 10);

        before.Logins.Should().OnlyContain(login => login == a.UserName);
        after.Logins.Should().OnlyContain(login => login == b.UserName);
        before.Sessions.Should().HaveCountLessThanOrEqualTo(2, "ten sequential operations reuse the pooled connection");
        after.Sessions.Should().HaveCountLessThanOrEqualTo(2);
        after.Sessions.Intersect(before.Sessions).Should().BeEmpty("the new login has a pool of its own");
        rig.Janitor.PendingClears.Should().Be(1);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-005")]
    public async Task After_the_grace_period_the_old_pool_is_cleared_and_the_old_login_has_no_session()
    {
        var a = await SqlLogins.CreateAsync(database, "a");
        var b = await SqlLogins.CreateAsync(database, "b");
        using var rig = new Rig(database, a, Grace);
        await RunAsync(rig.Source, 5);
        rig.File.Replace(b);
        rig.Source.Refresh();
        await RunAsync(rig.Source, 5);

        rig.Clock.Advance(Grace - TimeSpan.FromSeconds(1));
        rig.Janitor.PendingClears.Should().Be(1, "the grace period has not ended");
        rig.Clock.Advance(TimeSpan.FromSeconds(1));

        rig.Janitor.PendingClears.Should().Be(0);
        await SqlLogins.EventuallyAsync(async () => await SqlLogins.SessionsAsync(database, a) == 0, "the old login has no session after its pool was cleared", Token);
        rig.JanitorLog.Entries.Select(entry => entry.Message).Should().Contain(message => message.Contains("was cleared", StringComparison.Ordinal));
        (await RunAsync(rig.Source, 3)).Logins.Should().OnlyContain(login => login == b.UserName);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-005")]
    public async Task Work_that_started_on_the_old_login_finishes_after_the_pool_is_cleared()
    {
        var a = await SqlLogins.CreateAsync(database, "a");
        var b = await SqlLogins.CreateAsync(database, "b");
        using var rig = new Rig(database, a, Grace);
        await using (var context = SqlLogins.NewContext(database, rig.Source))
        {
            await using var transaction = await context.Database.BeginTransactionAsync(Token);
            (await SqlLogins.LoginOfAsync(context, Token)).Should().Be(a.UserName);

            rig.File.Replace(b);
            rig.Source.Refresh();
            rig.Clock.Advance(Grace);

            (await SqlLogins.LoginOfAsync(context, Token)).Should().Be(a.UserName, "the connection in use keeps its login and is not interrupted by the clear");
            await transaction.CommitAsync(Token);
        }

        await SqlLogins.EventuallyAsync(async () => await SqlLogins.SessionsAsync(database, a) == 0, "the connection that was in use closed when it returned to the cleared pool", Token);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-005")]
    public async Task The_old_login_can_be_revoked_after_the_clear_and_the_new_login_keeps_working()
    {
        var a = await SqlLogins.CreateAsync(database, "a");
        var b = await SqlLogins.CreateAsync(database, "b");
        using var rig = new Rig(database, a, Grace);
        await RunAsync(rig.Source, 5);
        rig.File.Replace(b);
        rig.Source.Refresh();
        rig.Clock.Advance(Grace);
        await SqlLogins.EventuallyAsync(async () => await SqlLogins.SessionsAsync(database, a) == 0, "the old login has no session", Token);

        await SqlLogins.DropAsync(database, a);

        var after = await RunAsync(rig.Source, 5);
        after.Logins.Should().OnlyContain(login => login == b.UserName);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-005")]
    public async Task A_credential_that_returns_to_use_is_not_cleared_while_it_is_the_current_one()
    {
        var a = await SqlLogins.CreateAsync(database, "a");
        var b = await SqlLogins.CreateAsync(database, "b");
        using var rig = new Rig(database, a, Grace);
        await RunAsync(rig.Source, 3);

        rig.File.Replace(b);
        rig.Source.Refresh();
        rig.File.Replace(a);
        rig.Source.Refresh();
        rig.Janitor.PendingClears.Should().Be(2);
        rig.Clock.Advance(Grace);

        rig.Janitor.PendingClears.Should().Be(0);
        rig.JanitorLog.Entries.Select(entry => entry.Message).Should().Contain(message => message.Contains("in use again", StringComparison.Ordinal));
        (await RunAsync(rig.Source, 3)).Logins.Should().OnlyContain(login => login == a.UserName);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-003")]
    [Trait("Requirement", "REQ-DATA-005")]
    public async Task The_outbox_keeps_delivering_while_the_credential_rotates_during_a_batch()
    {
        await database.ResetOutboxAsync();
        var a = await SqlLogins.CreateAsync(database, "a");
        var b = await SqlLogins.CreateAsync(database, "b");
        using var credentials = new Rig(database, a, Grace);
        using var outbox = new ProcessorRig(database, Make.Clock());
        var ids = await outbox.SeedMessagesAsync(4);
        var rotated = false;
        var handler = new RecordingHandler
        {
            Behavior = (_, _) =>
            {
                if (!rotated)
                {
                    rotated = true;
                    credentials.File.Replace(b);
                    credentials.Source.Refresh();
                    credentials.Clock.Advance(Grace);
                }

                return Task.CompletedTask;
            },
        };
        using var metrics = new OutboxMetrics();
        var processor = new OutboxProcessor(
            new TestContextFactory(() => SqlLogins.NewContext(database, credentials.Source)),
            [handler],
            Microsoft.Extensions.Options.Options.Create(outbox.Options),
            outbox.Clock,
            metrics,
            new CapturingLogger<OutboxProcessor>(),
            new Random(7),
            () => TestIds.V7(Counters.Next()));

        var result = await processor.ProcessBatchAsync(Token);

        result.Should().Be(new OutboxBatchResult(4, 4, 0, 0));
        handler.Deliveries.Select(delivery => delivery.MessageId).Should().Equal(ids);
        foreach (var id in ids)
        {
            (await outbox.RowAsync(id)).Status.Should().Be("Done");
        }

        (await SqlProbe.ScalarAsync<int>(database.ConnectionString, "SELECT COUNT(*) FROM catalog.OutboxReceipts")).Should().Be(4);
        (await SqlLogins.SessionsAsync(database, b)).Should().BeGreaterThan(0, "the messages after the rotation were finished by the new login");
        await SqlLogins.EventuallyAsync(async () => await SqlLogins.SessionsAsync(database, a) == 0, "the old login has no session after the pool was cleared during the batch", Token);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-005")]
    public async Task A_zero_grace_clears_the_old_pool_at_once_and_a_stopped_janitor_drops_the_clears_that_wait()
    {
        var a = await SqlLogins.CreateAsync(database, "a");
        var b = await SqlLogins.CreateAsync(database, "b");
        using var now = new Rig(database, a, TimeSpan.Zero);
        await RunAsync(now.Source, 3);
        now.File.Replace(b);
        now.Source.Refresh();
        now.Janitor.PendingClears.Should().Be(0);
        await SqlLogins.EventuallyAsync(async () => await SqlLogins.SessionsAsync(database, a) == 0, "a zero grace clears at once", Token);

        var c = await SqlLogins.CreateAsync(database, "c");
        var d = await SqlLogins.CreateAsync(database, "d");
        using var stopped = new Rig(database, c, Grace);
        stopped.File.Replace(d);
        stopped.Source.Refresh();
        stopped.Janitor.PendingClears.Should().Be(1);
        await stopped.Janitor.StopAsync(Token);

        stopped.Janitor.PendingClears.Should().Be(0);
        stopped.File.Replace(c);
        stopped.Source.Refresh().Should().BeTrue("the source still rotates, and nobody follows it");
        stopped.Janitor.PendingClears.Should().Be(0);
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-005")]
    public async Task No_value_of_a_credential_reaches_a_log_a_message_or_the_data_of_an_exception()
    {
        var a = await SqlLogins.CreateAsync(database, "a");
        var b = await SqlLogins.CreateAsync(database, "b");
        var unknown = new DatabaseCredential($"wp18_unknown_{Counters.Next()}", TestPasswords.Next());
        using var rig = new Rig(database, a, Grace);
        List<Exception> faults = [];

        await RunAsync(rig.Source, 2);
        rig.File.Write($$"""{"username":"{{b.UserName}}","password":"{{b.Password}}" """);
        rig.Source.Refresh();
        rig.File.Replace(unknown);
        rig.Source.Refresh();
        try
        {
            await using var context = SqlLogins.NewContext(database, rig.Source);
            await SqlLogins.LoginOfAsync(context, Token);
        }
        catch (SqlException exception)
        {
            faults.Add(exception);
        }

        rig.File.Replace(b);
        rig.Source.Refresh();
        rig.Clock.Advance(Grace);

        faults.Should().ContainSingle("the login that does not exist is refused by the server");
        var secrets = new[] { a.Password, b.Password, unknown.Password };
        var text = string.Join('\n', rig.SourceLog.Entries.Concat(rig.JanitorLog.Entries).Select(entry => entry.Message + entry.Exception));
        text += string.Join('\n', faults.Select(fault => fault + fault.Message + string.Join(',', fault.Data.Values.Cast<object?>())));
        text += a + "\n" + rig.Source.Current + "\n" + new DatabaseCredential(a.UserName, a.Password);
        foreach (var secret in secrets)
        {
            text.Should().NotContain(secret);
        }
    }

    private async Task<(List<string> Logins, HashSet<int> Sessions)> RunAsync(ICredentialSource source, int count)
    {
        List<string> logins = [];
        HashSet<int> sessions = [];
        for (var index = 0; index < count; index++)
        {
            await using var context = SqlLogins.NewContext(database, source);
            logins.Add(await SqlLogins.LoginOfAsync(context, Token));
            sessions.Add(await SqlLogins.SessionOfAsync(context, Token));
        }

        return (logins, sessions);
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-001")]
    [Trait("Requirement", "REQ-SEC-006")]
    public async Task A_rotation_and_the_clear_of_the_old_pool_are_spans_that_hold_no_credential()
    {
        var a = await SqlLogins.CreateAsync(database, "a");
        var b = await SqlLogins.CreateAsync(database, "b");
        using var rig = new Rig(database, a, Grace);
        using var probe = new TelemetryProbe(null, rig.Metrics.Source);
        await RunAsync(rig.Source, 3);

        rig.File.Replace(b);
        rig.Source.Refresh();

        probe.Spans.Select(span => span.OperationName).Should().Equal("credentials.rotate");

        rig.Clock.Advance(Grace);

        var clear = probe.Spans.Single(span => span.OperationName == "credentials.clear_pool");
        clear.GetTagItem("catalog.outcome").Should().Be("cleared");
        var tags = string.Join(' ', probe.Spans.SelectMany(span => span.TagObjects).Select(tag => $"{tag.Key}={tag.Value}"));
        tags.Should().NotContain(a.UserName).And.NotContain(b.UserName).And.NotContain(a.Password).And.NotContain(b.Password);
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-001")]
    public async Task A_clear_that_is_skipped_because_the_credential_returned_to_use_says_so_in_its_span()
    {
        var a = await SqlLogins.CreateAsync(database, "a");
        var b = await SqlLogins.CreateAsync(database, "b");
        using var rig = new Rig(database, a, Grace);
        using var probe = new TelemetryProbe(null, rig.Metrics.Source);
        await RunAsync(rig.Source, 3);

        rig.File.Replace(b);
        rig.Source.Refresh();
        rig.File.Replace(a);
        rig.Source.Refresh();
        rig.Clock.Advance(Grace);

        probe.Spans.Count(span => span.OperationName == "credentials.rotate").Should().Be(2);
        probe.Spans.Where(span => span.OperationName == "credentials.clear_pool").Select(span => (string?)span.GetTagItem("catalog.outcome")).Should().BeEquivalentTo("cleared", "skipped");
    }

    private sealed class Rig : IDisposable
    {
        public Rig(CatalogDatabaseFixture database, DatabaseCredential first, TimeSpan grace)
        {
            File.Write(first);
            Clock = new FakeTimeProvider();
            Source = new RotatingFileCredentialSource(File.Path, TimeSpan.FromHours(1), false, Clock, SourceLog);
            Metrics = new CredentialMetrics();
            Janitor = new CredentialPoolJanitor(Source, SqlLogins.BaseConnectionString(database), grace, Clock, Metrics, JanitorLog);
            Janitor.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        public TempCredentialFile File { get; } = new();

        public FakeTimeProvider Clock { get; }

        public CapturingLogger<RotatingFileCredentialSource> SourceLog { get; } = new();

        public CapturingLogger<CredentialPoolJanitor> JanitorLog { get; } = new();

        public RotatingFileCredentialSource Source { get; }

        public CredentialMetrics Metrics { get; }

        public CredentialPoolJanitor Janitor { get; }

        public void Dispose()
        {
            Janitor.Dispose();
            Metrics.Dispose();
            Source.Dispose();
            File.Dispose();
        }
    }
}
