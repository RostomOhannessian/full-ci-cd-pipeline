using AwesomeAssertions;
using Catalog.Api.Configuration;
using Catalog.Api.Tests.Support;
using Microsoft.Extensions.Options;
using Xunit;

namespace Catalog.Api.Tests;

/// <summary>
/// The settings of the host are checked when it starts, and a wrong one stops the host before it serves a request (plan section 8.8, REQ-ARC-002). A problem names a setting and never repeats a value, because a
/// value can be an address or a secret.
/// </summary>
public sealed class ConfigurationValidationTests
{
    private static ValidateOptionsResult Hosting(HostingOptions options, string environment = "Production") => new HostingOptionsValidator(new FakeEnvironment(environment)).Validate(null, options);

    private static ValidateOptionsResult Auth(AuthOptions options, string environment = "Production") => new AuthOptionsValidator(new FakeEnvironment(environment)).Validate(null, options);

    private static AuthOptions ValidAuth() => new() { Issuer = "https://id.catalog.localtest.me:8443/realms/catalog" };

    [Fact]
    [Trait("Requirement", "REQ-ARC-002")]
    public void The_default_listeners_are_valid_and_the_management_port_is_not_the_public_port()
    {
        var options = new HostingOptions();

        Hosting(options).Succeeded.Should().BeTrue();
        options.PublicPort.Should().Be(8080);
        options.ManagementPort.Should().Be(8081);
    }

    [Theory]
    [Trait("Requirement", "REQ-ARC-002")]
    [InlineData(8080, 8080)]
    [InlineData(0, 8081)]
    [InlineData(8080, 70000)]
    public void The_ports_must_differ_and_be_real_ports(int publicPort, int managementPort)
    {
        var result = Hosting(new HostingOptions { PublicPort = publicPort, ManagementPort = managementPort });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Port");
    }

    [Fact]
    [Trait("Requirement", "REQ-ARC-002")]
    public void The_listen_address_must_be_an_ip_address()
    {
        Hosting(new HostingOptions { ListenAddress = "not-an-address" }).FailureMessage.Should().Contain("ListenAddress");
        Hosting(new HostingOptions { ListenAddress = "0.0.0.0" }).Succeeded.Should().BeTrue();
    }

    [Theory]
    [Trait("Requirement", "REQ-ARC-002")]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    [InlineData("Development", true)]
    public void Only_the_development_environment_may_allow_every_host(string environment, bool allowed)
    {
        Hosting(new HostingOptions { AllowedHosts = "*" }, environment).Succeeded.Should().Be(allowed);
    }

    [Fact]
    [Trait("Requirement", "REQ-ARC-002")]
    public void The_allowed_hosts_are_one_semicolon_separated_setting_that_names_at_least_one_host()
    {
        Hosting(new HostingOptions { AllowedHosts = string.Empty }).Failed.Should().BeTrue();
        Hosting(new HostingOptions { AllowedHosts = " ; ;" }).Failed.Should().BeTrue();
        Hosting(new HostingOptions { AllowedHosts = "api.example.test;*.example.test;" }).Succeeded.Should().BeTrue();
        new HostingOptions { AllowedHosts = "api.example.test; *.example.test;" }.AllowedHostList.Should().Equal("api.example.test", "*.example.test");
    }

    [Fact]
    [Trait("Requirement", "REQ-IDN-003")]
    public void A_valid_https_issuer_passes_and_the_audience_defaults_to_the_catalog_api()
    {
        var options = ValidAuth();

        Auth(options).Succeeded.Should().BeTrue();
        options.Audience.Should().Be("catalog-api");
        options.RequireHttpsMetadata.Should().BeTrue();
        options.EffectiveMetadataAddress.Should().Be("https://id.catalog.localtest.me:8443/realms/catalog/.well-known/openid-configuration");
    }

    [Fact]
    [Trait("Requirement", "REQ-IDN-003")]
    public void An_explicit_metadata_address_replaces_the_one_that_the_issuer_implies()
    {
        var options = ValidAuth();
        options.MetadataAddress = "https://keycloak.catalog.svc/realms/catalog/.well-known/openid-configuration";

        Auth(options).Succeeded.Should().BeTrue();
        options.EffectiveMetadataAddress.Should().Be(options.MetadataAddress);
    }

    [Theory]
    [Trait("Requirement", "REQ-IDN-003")]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("ftp://id.example.test/realms/catalog")]
    public void The_issuer_must_be_an_absolute_http_or_https_url(string issuer)
    {
        Auth(new AuthOptions { Issuer = issuer }).FailureMessage.Should().Contain("Issuer");
    }

    [Fact]
    [Trait("Requirement", "REQ-IDN-003")]
    public void Metadata_must_come_over_https_unless_the_development_environment_turns_it_off()
    {
        var plain = new AuthOptions { Issuer = "http://localhost:8180/realms/catalog" };
        var off = new AuthOptions { Issuer = "http://localhost:8180/realms/catalog", RequireHttpsMetadata = false };

        Auth(plain).FailureMessage.Should().Contain("https");
        Auth(plain, "Development").FailureMessage.Should().Contain("https");
        Auth(off).FailureMessage.Should().Contain("Development");
        Auth(off, "Staging").FailureMessage.Should().Contain("Development");
        Auth(off, "Development").Succeeded.Should().BeTrue();
    }

    [Theory]
    [Trait("Requirement", "REQ-IDN-003")]
    [InlineData(0, true)]
    [InlineData(30, true)]
    [InlineData(31, false)]
    [InlineData(300, false)]
    [InlineData(-1, false)]
    public void The_clock_skew_is_at_most_thirty_seconds(int seconds, bool valid)
    {
        var options = ValidAuth();
        options.ClockSkew = TimeSpan.FromSeconds(seconds);

        Auth(options).Succeeded.Should().Be(valid);
    }

    [Theory]
    [Trait("Requirement", "REQ-IDN-003")]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(86400, true)]
    [InlineData(90000, false)]
    public void The_metadata_refresh_interval_is_between_one_second_and_one_day(int seconds, bool valid)
    {
        var options = ValidAuth();
        options.MetadataRefreshInterval = TimeSpan.FromSeconds(seconds);

        Auth(options).Succeeded.Should().Be(valid);
    }

    [Fact]
    [Trait("Requirement", "REQ-IDN-003")]
    public void A_problem_names_the_setting_and_never_repeats_its_value()
    {
        var result = Auth(new AuthOptions { Issuer = "ftp://secret-host.internal/realm" });

        result.FailureMessage.Should().Contain("Issuer").And.NotContain("secret-host");
    }

    [Fact]
    [Trait("Requirement", "REQ-API-006")]
    public void The_default_limits_are_valid_and_every_bound_is_checked()
    {
        var validator = new LimitsOptionsValidator();

        validator.Validate(null, new LimitsOptions()).Succeeded.Should().BeTrue();
        validator.Validate(null, new LimitsOptions { MaxRequestBodyBytes = 10 }).FailureMessage.Should().Contain("MaxRequestBodyBytes");
        validator.Validate(null, new LimitsOptions { MaxRequestBodyBytes = 2 * 1024 * 1024 }).FailureMessage.Should().Contain("MaxRequestBodyBytes");
        validator.Validate(null, new LimitsOptions { MaxJsonDepth = 1 }).FailureMessage.Should().Contain("MaxJsonDepth");
        validator.Validate(null, new LimitsOptions { MaxJsonDepth = 65 }).FailureMessage.Should().Contain("MaxJsonDepth");
        validator.Validate(null, new LimitsOptions { ReadPermits = 0 }).FailureMessage.Should().Contain("Permits");
        validator.Validate(null, new LimitsOptions { WritePermits = 2_000_000 }).FailureMessage.Should().Contain("Permits");
        validator.Validate(null, new LimitsOptions { Window = TimeSpan.Zero }).FailureMessage.Should().Contain("Window");
    }

    [Fact]
    [Trait("Requirement", "REQ-API-007")]
    public void A_write_has_a_lower_default_limit_than_a_read()
    {
        var options = new LimitsOptions();

        options.WritePermits.Should().BeLessThan(options.ReadPermits);
    }

    [Fact]
    [Trait("Requirement", "REQ-ARC-002")]
    public void Without_a_setting_no_proxy_is_trusted()
    {
        var settings = new ForwardedHeadersSettings();

        settings.KnownNetworks.Should().BeEmpty();
        settings.KnownProxies.Should().BeEmpty();
        new ForwardedHeadersSettingsValidator().Validate(null, settings).Succeeded.Should().BeTrue();
    }

    [Theory]
    [Trait("Requirement", "REQ-ARC-002")]
    [InlineData("10.0.0.0/8")]
    [InlineData("192.168.0.0/16")]
    [InlineData("fd00::/8")]
    public void A_network_with_a_prefix_is_accepted(string network)
    {
        new ForwardedHeadersSettingsValidator().Validate(null, new ForwardedHeadersSettings { KnownNetworks = network }).Succeeded.Should().BeTrue();
    }

    [Fact]
    [Trait("Requirement", "REQ-ARC-002")]
    public void Several_networks_and_proxies_are_one_semicolon_separated_setting()
    {
        var settings = new ForwardedHeadersSettings { KnownNetworks = "10.0.0.0/8; 172.16.0.0/12;", KnownProxies = "192.0.2.1;192.0.2.2" };

        settings.KnownNetworkList.Should().Equal("10.0.0.0/8", "172.16.0.0/12");
        settings.KnownProxyList.Should().Equal("192.0.2.1", "192.0.2.2");
        new ForwardedHeadersSettingsValidator().Validate(null, settings).Succeeded.Should().BeTrue();
    }

    [Theory]
    [Trait("Requirement", "REQ-ARC-002")]
    [InlineData("0.0.0.0/0", "prefix length of 0")]
    [InlineData("::/0", "prefix length of 0")]
    [InlineData("10.0.0.0", "KnownNetworks")]
    [InlineData("nonsense/8", "KnownNetworks")]
    public void A_network_that_trusts_every_address_or_is_not_a_network_is_refused(string network, string expected)
    {
        new ForwardedHeadersSettingsValidator().Validate(null, new ForwardedHeadersSettings { KnownNetworks = network }).FailureMessage.Should().Contain(expected);
    }

    [Theory]
    [Trait("Requirement", "REQ-ARC-002")]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("gateway")]
    public void A_proxy_must_be_a_real_address(string proxy)
    {
        new ForwardedHeadersSettingsValidator().Validate(null, new ForwardedHeadersSettings { KnownProxies = proxy }).FailureMessage.Should().Contain("KnownProxies");
    }

    [Theory]
    [Trait("Requirement", "REQ-ARC-002")]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    public void The_forward_limit_is_between_one_and_five(int limit, bool valid)
    {
        new ForwardedHeadersSettingsValidator().Validate(null, new ForwardedHeadersSettings { ForwardLimit = limit }).Succeeded.Should().Be(valid);
    }
}
