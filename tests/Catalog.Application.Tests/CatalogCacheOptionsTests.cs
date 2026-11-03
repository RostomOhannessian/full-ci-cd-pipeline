using AwesomeAssertions;
using Catalog.CrossCutting.Caching;
using Microsoft.Extensions.Options;
using Xunit;

namespace Catalog.Application.Tests;

/// <summary>REQ-CACHE-001 and REQ-CACHE-003: the settings of the cache are checked when the host starts, a problem names a setting and never repeats a value, and the circuit breakers cannot be turned off.</summary>
public sealed class CatalogCacheOptionsTests
{
    public static TheoryData<string, Action<CatalogCacheOptions>, string> Invalid => new()
    {
        { "an empty environment", options => options.Environment = string.Empty, "Environment" },
        { "an upper-case environment", options => options.Environment = "Prod", "Environment" },
        { "an environment that starts with a digit", options => options.Environment = "1dev", "Environment" },
        { "an environment with a colon, which would end the prefix of the key early", options => options.Environment = "dev:x", "Environment" },
        { "an environment with a line break", options => options.Environment = "dev\n", "Environment" },
        { "an environment that is too long", options => options.Environment = new string('a', CatalogCacheOptions.MaxEnvironmentLength + 1), "Environment" },
        { "no memory duration", options => options.MemoryDuration = TimeSpan.Zero, "MemoryDuration" },
        { "no shared duration", options => options.SharedDuration = TimeSpan.Zero, "SharedDuration" },
        { "no list duration", options => options.ListDuration = TimeSpan.Zero, "ListDuration" },
        { "no timeout for the shared cache", options => options.SharedTimeout = TimeSpan.Zero, "SharedTimeout" },
        { "a circuit breaker of the shared cache that is off", options => options.SharedCircuitBreakerDuration = TimeSpan.Zero, "SharedCircuitBreakerDuration" },
        { "a circuit breaker of the backplane that is off", options => options.BackplaneCircuitBreakerDuration = TimeSpan.Zero, "BackplaneCircuitBreakerDuration" },
        { "no room in memory", options => options.MemoryMaxEntries = 0, "MemoryMaxEntries" },
        { "a memory duration longer than the shared duration", options => options.MemoryDuration = options.SharedDuration + TimeSpan.FromSeconds(1), "MemoryDuration" },
        { "a list that lives longer than the memory duration", options => options.ListDuration = options.MemoryDuration + TimeSpan.FromSeconds(1), "ListDuration" },
    };

    [Fact]
    [Trait("Requirement", "REQ-CACHE-001")]
    public void The_defaults_are_valid_and_follow_plan_section_8_5()
    {
        var options = new CatalogCacheOptions();

        options.Validate().Should().BeEmpty();
        options.MemoryDuration.Should().Be(TimeSpan.FromSeconds(30));
        options.ListDuration.Should().Be(TimeSpan.FromSeconds(10));
        options.SharedCircuitBreakerDuration.Should().BePositive("FusionCache leaves a circuit breaker off at zero, and every call would then wait for the timeout (spike 1.b)");
        options.BackplaneCircuitBreakerDuration.Should().BePositive();
        CatalogCacheOptions.SectionName.Should().Be("Catalog:Cache");
    }

    [Theory]
    [MemberData(nameof(Invalid))]
    [Trait("Requirement", "REQ-CACHE-001")]
    public void A_setting_that_is_not_valid_is_named_in_a_problem(string scenario, Action<CatalogCacheOptions> change, string setting)
    {
        var options = new CatalogCacheOptions();
        change(options);

        var problems = options.Validate();

        problems.Should().Contain(problem => problem.Contains(setting, StringComparison.Ordinal), scenario);
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-001")]
    public void A_problem_never_repeats_the_value_of_a_setting()
    {
        var options = new CatalogCacheOptions { Environment = "Not-a-valid-ENVIRONMENT-value" };

        options.Validate().Should().NotContain(problem => problem.Contains("ENVIRONMENT-value", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-001")]
    public void The_validator_fails_the_start_of_the_host_with_every_problem_and_passes_valid_settings()
    {
        var validator = new CatalogCacheOptionsValidator();

        validator.Validate(null, new CatalogCacheOptions()).Succeeded.Should().BeTrue();

        var failed = validator.Validate(null, new CatalogCacheOptions { Environment = "X", MemoryMaxEntries = 0 });

        failed.Failed.Should().BeTrue();
        failed.Failures.Should().HaveCount(2);
        validator.Validate(null, null!).Failed.Should().BeTrue();
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-001")]
    public void The_keys_and_the_channel_of_an_environment_are_built_from_its_name()
    {
        var options = new CatalogCacheOptions { Environment = "prod" };

        options.KeyPrefix.Should().Be("catalog:prod:");
        options.ChannelPrefix.Should().Be("catalog:prod");
    }

    [Fact]
    [Trait("Requirement", "REQ-CACHE-001")]
    public void The_settings_print_nothing_but_their_name()
    {
        new CatalogCacheOptions().ToString().Should().Be(nameof(CatalogCacheOptions));
        Options.Create(new CatalogCacheOptions()).Value.Should().NotBeNull();
    }
}
