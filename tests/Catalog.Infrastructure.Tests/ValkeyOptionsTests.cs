using AwesomeAssertions;
using Catalog.Infrastructure.Caching;
using Xunit;

namespace Catalog.Infrastructure.Tests;

/// <summary>
/// REQ-CACHE-001 and REQ-CACHE-003: the settings of the connection to Valkey are checked when the host starts and never repeat a secret, the connection never aborts when the server is down, so the host starts
/// without Valkey, and the commands time out before the cache does (plan sections 8.5 and 8.9, ADR-0038). These tests need no server.
/// </summary>
public sealed class ValkeyOptionsTests
{
    public static TheoryData<string, Action<ValkeyOptions>, string> Invalid => new()
    {
        { "no endpoint", options => options.Endpoint = string.Empty, "Endpoint" },
        { "an endpoint without a port", options => options.Endpoint = "valkey", "Endpoint" },
        { "an endpoint with a port that is not a number", options => options.Endpoint = "valkey:abc", "Endpoint" },
        { "an endpoint with the port zero", options => options.Endpoint = "valkey:0", "Endpoint" },
        { "an endpoint with a port above 65535", options => options.Endpoint = "valkey:65536", "Endpoint" },
        { "an endpoint with a comma, which would add a second server", options => options.Endpoint = "a:1,b:2", "Endpoint" },
        { "an endpoint with an equals sign, which would add an option", options => options.Endpoint = "valkey:6379,password=x", "Endpoint" },
        { "an endpoint with white space", options => options.Endpoint = "val key:6379", "Endpoint" },
        { "no user", options => options.UserName = string.Empty, "UserName" },
        { "no password", options => options.Password = string.Empty, "Password" },
        { "a password that is too long", options => options.Password = new string('a', ValkeyOptions.MaxCredentialLength + 1), "Password" },
        { "no connect timeout", options => options.ConnectTimeout = TimeSpan.Zero, "ConnectTimeout" },
        { "no command timeout", options => options.CommandTimeout = TimeSpan.Zero, "CommandTimeout" },
        { "a client name with white space", options => options.ClientName = "catalog api", "ClientName" },
    };

    private static ValkeyOptions Valid() => new() { Endpoint = "valkey:6379", UserName = "catalog-dev", Password = "Pw-Secret-test" };

    [Fact]
    [Trait("Requirement", "REQ-CACHE-001")]
    public void Valid_settings_have_no_problem()
    {
        Valid().Validate().Should().BeEmpty();
        ValkeyOptions.SectionName.Should().Be("Catalog:Valkey");
    }

    [Theory]
    [MemberData(nameof(Invalid))]
    [Trait("Requirement", "REQ-CACHE-001")]
    public void A_setting_that_is_not_valid_is_named_in_a_problem(string scenario, Action<ValkeyOptions> change, string setting)
    {
        var options = Valid();
        change(options);

        options.Validate().Should().Contain(problem => problem.Contains(setting, StringComparison.Ordinal), scenario);
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-001")]
    public void A_problem_and_the_text_of_the_settings_never_repeat_the_password()
    {
        var options = Valid();
        options.Endpoint = "bad endpoint";

        options.Validate().Should().NotContain(problem => problem.Contains("Pw-Secret-test", StringComparison.Ordinal));
        options.ToString().Should().Be(nameof(ValkeyOptions));
        options.ToString().Should().NotContain("Pw-Secret-test");
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-001")]
    public void The_validator_stops_the_start_of_the_host_and_names_every_problem()
    {
        var validator = new ValkeyOptionsValidator();

        validator.Validate(null, Valid()).Succeeded.Should().BeTrue();

        var failed = validator.Validate(null, new ValkeyOptions { Endpoint = "x", UserName = string.Empty, Password = string.Empty });

        failed.Failed.Should().BeTrue();
        failed.Failures.Should().HaveCount(3);
        validator.Validate(null, null!).Failed.Should().BeTrue();
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-003")]
    public void The_connection_does_not_abort_when_the_server_is_down_so_the_host_starts_without_valkey()
    {
        var configuration = ValkeyConnection.Configure(Valid());

        configuration.AbortOnConnectFail.Should().BeFalse("readiness excludes Valkey, and the circuit breakers of the cache take over (plan section 8.9)");
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-001")]
    public void The_connection_settings_carry_the_endpoint_the_user_the_timeouts_and_the_client_name()
    {
        var options = Valid();
        options.UseTls = true;
        options.ConnectTimeout = TimeSpan.FromMilliseconds(1500);
        options.CommandTimeout = TimeSpan.FromMilliseconds(2500);

        var configuration = ValkeyConnection.Configure(options);

        var endpoint = configuration.EndPoints.Should().ContainSingle().Which.Should().BeOfType<System.Net.DnsEndPoint>().Subject;
        endpoint.Host.Should().Be("valkey");
        endpoint.Port.Should().Be(6379);
        configuration.User.Should().Be("catalog-dev");
        configuration.Password.Should().Be("Pw-Secret-test");
        configuration.Ssl.Should().BeTrue();
        configuration.ConnectTimeout.Should().Be(1500);
        configuration.SyncTimeout.Should().Be(2500);
        configuration.AsyncTimeout.Should().Be(2500);
        configuration.ClientName.Should().Be("catalog-api");
        // ConfigurationOptions.ToString() with no argument includes the password (found with StackExchange.Redis 2.10.1, checked 2026-11-03), so the code never prints the settings and only the form without the password is safe.
        configuration.ToString(includePassword: false).Should().NotContain("Pw-Secret-test");
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-001")]
    public void A_connection_cannot_be_built_from_settings_that_are_not_valid_and_the_error_never_repeats_the_password()
    {
        var options = Valid();
        options.Endpoint = "no-port";

        var configure = () => ValkeyConnection.Configure(options);
        var shared = () => ValkeyConnection.CreateSharedCache(options);
        var backplane = () => ValkeyConnection.CreateBackplane(options);

        configure.Should().Throw<ArgumentException>().Which.Message.Should().Contain("Endpoint").And.NotContain("Pw-Secret-test");
        shared.Should().Throw<ArgumentException>();
        backplane.Should().Throw<ArgumentException>();
        ((Action)(() => ValkeyConnection.Configure(null!))).Should().Throw<ArgumentNullException>();
    }
}
