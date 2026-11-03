using AwesomeAssertions;
using Catalog.Infrastructure.Credentials;
using Catalog.Infrastructure.Idempotency;
using Catalog.Infrastructure.Outbox;
using Catalog.Infrastructure.Tests.Support;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Catalog.Infrastructure.Tests;

/// <summary>
/// REQ-DATA-005: the connection string of a credential is built by one function, so the pool that the interceptor opens and the pool that the janitor clears are the same pool. A base string that already holds a
/// credential is refused by a message that never repeats it, and the settings of the database and of the other persistence options have validated bounds (plan section 8.4, ADR-0037).
/// </summary>
public sealed class ConnectionStringAndOptionsTests
{
    private const string Base = "Server=db.example.test,1433;Database=catalog;Encrypt=true;TrustServerCertificate=false;Max Pool Size=30";

    private static readonly DatabaseCredential Credential = new("login_a", "Pw-Secret-test");

    [Fact]
    [Trait("Requirement", "REQ-DATA-005")]
    public void The_string_of_a_credential_is_the_same_text_every_time_so_the_pool_key_is_stable()
    {
        var first = CatalogConnectionString.Build(Base, Credential);
        var second = CatalogConnectionString.Build(Base, new DatabaseCredential("login_a", "Pw-Secret-test"));
        var reordered = CatalogConnectionString.Build("Max Pool Size=30;database=catalog;server=db.example.test,1433;trustservercertificate=False;ENCRYPT=True", Credential);

        second.Should().Be(first);
        reordered.Should().Be(first);
        first.Should().Contain("User ID=login_a").And.Contain("Pooling=True").And.Contain("Max Pool Size=30");
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-005")]
    public void Another_credential_gives_another_string_because_each_credential_has_its_own_pool()
    {
        CatalogConnectionString.Build(Base, new DatabaseCredential("login_b", "Pw-Other-test")).Should().NotBe(CatalogConnectionString.Build(Base, Credential));
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-005")]
    public void Pooling_is_on_even_when_the_base_string_turns_it_off()
    {
        CatalogConnectionString.Build(Base + ";Pooling=false", Credential).Should().Contain("Pooling=True");
    }

    [Theory]
    [Trait("Requirement", "REQ-DATA-005")]
    [InlineData("Server=x;Database=catalog;User ID=sa", "User ID")]
    [InlineData("Server=x;Database=catalog;Password=Pw-Secret-test", "Password")]
    [InlineData("Server=x;Database=catalog;uid=sa;pwd=Pw-Secret-test", "Password")]
    [InlineData("Server=x;Database=catalog;Integrated Security=true", "Integrated Security")]
    [InlineData("Server=x;Database=catalog;Authentication=Active Directory Password", "Authentication")]
    [InlineData("Server=x;Database=catalog;Pooling=false", "Pooling")]
    public void A_base_string_with_a_credential_or_without_pooling_is_refused_and_the_problem_never_repeats_a_value(string text, string keyword)
    {
        var problems = CatalogConnectionString.Validate(text);

        problems.Should().Contain(problem => problem.Contains(keyword, StringComparison.Ordinal));
        problems.Should().OnlyContain(problem => !problem.Contains("Pw-Secret", StringComparison.Ordinal) && !problem.Contains("Server=x", StringComparison.Ordinal));
    }

    [Theory]
    [Trait("Requirement", "REQ-DATA-005")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("this is not a connection string Pw-Secret-test")]
    public void A_missing_or_unreadable_base_string_is_refused_without_an_echo(string text)
    {
        var problems = CatalogConnectionString.Validate(text);

        problems.Should().ContainSingle();
        problems[0].Should().NotContain("Pw-Secret");
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-005")]
    public void A_valid_base_string_has_no_problem()
    {
        CatalogConnectionString.Validate(Base).Should().BeEmpty();
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-005")]
    public void The_database_settings_have_validated_bounds_and_name_a_setting_instead_of_a_value()
    {
        Valid().Validate().Should().BeEmpty();
        Valid(o => o.UserName = string.Empty).Validate().Should().ContainSingle().Which.Should().Contain("static mode");
        Valid(o => o.Password = string.Empty).Validate().Should().ContainSingle().Which.Should().Contain("static mode");
        Valid(o => o.PoolClearGrace = TimeSpan.FromSeconds(-1)).Validate().Should().ContainSingle().Which.Should().Contain("PoolClearGrace");
        Valid(o => o.PoolClearGrace = TimeSpan.FromHours(2)).Validate().Should().ContainSingle().Which.Should().Contain("PoolClearGrace");
        Valid(o => o.ConnectionString = "Server=x;Password=Pw-Secret-test").Validate().Should().OnlyContain(problem => !problem.Contains("Pw-Secret", StringComparison.Ordinal));

        var rotating = Valid(o =>
        {
            o.Mode = CredentialMode.RotatingFile;
            o.UserName = string.Empty;
            o.Password = string.Empty;
        });
        rotating.Validate().Should().ContainSingle().Which.Should().Contain("CredentialFile");
        rotating.CredentialFile = "/vault/secrets/sql.json";
        rotating.Validate().Should().BeEmpty();
        rotating.CredentialPollInterval = TimeSpan.Zero;
        rotating.Validate().Should().ContainSingle().Which.Should().Contain("CredentialPollInterval");
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-005")]
    public void The_settings_print_only_their_type_so_a_password_never_shows_in_a_log_or_a_debugger()
    {
        var options = Valid(o => o.Password = "Pw-Secret-test");

        options.ToString().Should().Be("DatabaseOptions").And.NotContain("Pw-Secret");
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-005")]
    public void The_validators_fail_with_the_problems_of_the_settings_and_succeed_for_valid_ones()
    {
        var database = new DatabaseOptionsValidator();
        var outbox = new OutboxOptionsValidator();
        var idempotency = new IdempotencyOptionsValidator();

        database.Validate(null, Valid()).Succeeded.Should().BeTrue();
        outbox.Validate(null, new OutboxOptions()).Succeeded.Should().BeTrue();
        idempotency.Validate(null, new IdempotencyOptions()).Succeeded.Should().BeTrue();

        var failed = database.Validate(null, Valid(o => o.ConnectionString = "Server=x;Password=Pw-Secret-test"));
        failed.Failed.Should().BeTrue();
        failed.FailureMessage.Should().Contain("Password").And.NotContain("Pw-Secret");
        outbox.Validate(null, new OutboxOptions { BatchSize = 0 }).Failed.Should().BeTrue();
        idempotency.Validate(null, new IdempotencyOptions { Retention = TimeSpan.Zero }).Failed.Should().BeTrue();
        database.Validate(null, null!).Failed.Should().BeTrue();
        outbox.Validate(null, null!).Failed.Should().BeTrue();
        idempotency.Validate(null, null!).Failed.Should().BeTrue();
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-005")]
    public void The_factory_builds_the_source_that_the_settings_name_and_refuses_an_unknown_mode()
    {
        using var file = new TempCredentialFile();
        file.Write(Credential);

        var staticSource = CredentialSources.Create(Valid(o => o.Password = "Pw-Secret-test"), TimeProvider.System, NullLoggerFactory.Instance);
        using var rotating = (RotatingFileCredentialSource)CredentialSources.Create(
            Valid(o =>
            {
                o.Mode = CredentialMode.RotatingFile;
                o.CredentialFile = file.Path;
                o.WatchCredentialFile = false;
            }),
            new FakeTimeProvider(),
            NullLoggerFactory.Instance);
        var unknown = () => CredentialSources.Create(Valid(o => o.Mode = (CredentialMode)42), TimeProvider.System, NullLoggerFactory.Instance);

        staticSource.Should().BeOfType<StaticCredentialSource>().Which.Current.UserName.Should().Be("login_a");
        rotating.Current.Should().Be(Credential);
        unknown.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-005")]
    public void The_interceptor_and_the_janitor_refuse_missing_arguments()
    {
        var source = new StaticCredentialSource(Credential);
        using var metrics = new CredentialMetrics();
        var logger = NullLogger<CredentialPoolJanitor>.Instance;

        ((Action)(() => _ = new CredentialConnectionInterceptor(null!, Base))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new CredentialConnectionInterceptor(source, null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new CredentialPoolJanitor(null!, Base, TimeSpan.Zero, TimeProvider.System, metrics, logger))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new CredentialPoolJanitor(source, null!, TimeSpan.Zero, TimeProvider.System, metrics, logger))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new CredentialPoolJanitor(source, Base, TimeSpan.FromSeconds(-1), TimeProvider.System, metrics, logger))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => _ = new CredentialPoolJanitor(source, Base, TimeSpan.Zero, null!, metrics, logger))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new CredentialPoolJanitor(source, Base, TimeSpan.Zero, TimeProvider.System, null!, logger))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new CredentialPoolJanitor(source, Base, TimeSpan.Zero, TimeProvider.System, metrics, null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => CatalogConnectionString.Build(null!, Credential))).Should().Throw<ArgumentNullException>();
        ((Action)(() => CatalogConnectionString.Build(Base, null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    [Trait("Requirement", "REQ-DATA-005")]
    public void The_interceptor_sets_the_string_of_the_current_credential_on_each_open_and_follows_a_rotation()
    {
        var source = new SwitchableSource(Credential);
        var interceptor = new CredentialConnectionInterceptor(source, Base);
        using var connection = new SqlConnection();

        interceptor.ConnectionOpening(connection, null!, default);
        var before = connection.ConnectionString;
        source.Switch(new DatabaseCredential("login_b", "Pw-Other-test"));
        interceptor.ConnectionOpening(connection, null!, default);

        before.Should().Contain("User ID=login_a");
        connection.ConnectionString.Should().Contain("User ID=login_b");
    }

    private static DatabaseOptions Valid(Action<DatabaseOptions>? change = null)
    {
        var options = new DatabaseOptions { ConnectionString = Base, UserName = "login_a", Password = "Pw-Secret-test" };
        change?.Invoke(options);

        return options;
    }

    private sealed class SwitchableSource(DatabaseCredential first) : ICredentialSource
    {
        public event EventHandler<CredentialRotatedEventArgs>? Rotated;

        public DatabaseCredential Current { get; private set; } = first;

        public void Switch(DatabaseCredential next)
        {
            var previous = Current;
            Current = next;
            Rotated?.Invoke(this, new CredentialRotatedEventArgs(previous, next));
        }
    }
}
