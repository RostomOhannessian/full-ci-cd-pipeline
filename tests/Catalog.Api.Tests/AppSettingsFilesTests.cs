using AwesomeAssertions;
using Catalog.Api.Configuration;
using Catalog.Api.Tests.Support;
using Catalog.CrossCutting.Caching;
using Catalog.Infrastructure.Caching;
using Catalog.Infrastructure.Credentials;
using Catalog.Infrastructure.Tests.Support;
using Xunit;

namespace Catalog.Api.Tests;

/// <summary>
/// The settings files of the API (plan section 8.8, REQ-ARC-002): <c>appsettings.json</c> holds the defaults that every environment shares, and <c>appsettings.Development.json</c> holds the addresses of the Compose
/// stack. Neither file holds a secret, so the two passwords arrive from the environment, and the production file alone never names an issuer, so a deployment that forgets its identity provider stops at start.
/// </summary>
public sealed class AppSettingsFilesTests
{
    private static string ApiDirectory => Path.Combine(ComposeImage.RepositoryRoot(), "src", "Catalog.Api");

    private static IConfigurationRoot Load(bool development, bool withPasswords)
    {
        var builder = new ConfigurationBuilder().AddJsonFile(Path.Combine(ApiDirectory, "appsettings.json"), optional: false);

        if (development)
        {
            builder.AddJsonFile(Path.Combine(ApiDirectory, "appsettings.Development.json"), optional: false);
        }

        if (withPasswords)
        {
            builder.AddInMemoryCollection(new Dictionary<string, string?> { ["Catalog:Database:Password"] = HostSettings.DatabasePassword, ["Catalog:Valkey:Password"] = HostSettings.ValkeyPassword });
        }

        return builder.Build();
    }

    private static T Bind<T>(IConfiguration configuration, string section)
        where T : new() => configuration.GetSection(section).Get<T>() ?? new T();

    [Fact]
    [Trait("Requirement", "REQ-ARC-002")]
    public void The_settings_files_hold_no_secret()
    {
        foreach (var file in new[] { "appsettings.json", "appsettings.Development.json" })
        {
            var text = File.ReadAllText(Path.Combine(ApiDirectory, file));

            text.Should().NotContainEquivalentOf("password").And.NotContainEquivalentOf("secret").And.NotContainEquivalentOf("token");
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-ARC-002")]
    public void The_development_settings_and_two_passwords_make_every_option_valid()
    {
        var configuration = Load(development: true, withPasswords: true);
        var development = new FakeEnvironment("Development");

        new AuthOptionsValidator(development).Validate(null, Bind<AuthOptions>(configuration, AuthOptions.SectionName)).Succeeded.Should().BeTrue();
        new HostingOptionsValidator(development).Validate(null, Bind<HostingOptions>(configuration, HostingOptions.SectionName)).Succeeded.Should().BeTrue();
        new LimitsOptionsValidator().Validate(null, Bind<LimitsOptions>(configuration, LimitsOptions.SectionName)).Succeeded.Should().BeTrue();
        new ForwardedHeadersSettingsValidator().Validate(null, Bind<ForwardedHeadersSettings>(configuration, ForwardedHeadersSettings.SectionName)).Succeeded.Should().BeTrue();
        Bind<DatabaseOptions>(configuration, DatabaseOptions.SectionName).Validate().Should().BeEmpty();
        Bind<ValkeyOptions>(configuration, ValkeyOptions.SectionName).Validate().Should().BeEmpty();
        Bind<CatalogCacheOptions>(configuration, CatalogCacheOptions.SectionName).Validate().Should().BeEmpty();
    }

    [Fact]
    [Trait("Requirement", "REQ-ARC-002")]
    public void Without_the_passwords_the_development_settings_stop_the_host()
    {
        var configuration = Load(development: true, withPasswords: false);

        Bind<DatabaseOptions>(configuration, DatabaseOptions.SectionName).Validate().Should().NotBeEmpty();
        Bind<ValkeyOptions>(configuration, ValkeyOptions.SectionName).Validate().Should().NotBeEmpty();
    }

    [Fact]
    [Trait("Requirement", "REQ-IDN-003")]
    public void The_production_file_alone_names_no_issuer_so_a_deployment_must_provide_one_and_it_must_use_https()
    {
        var configuration = Load(development: false, withPasswords: true);
        var auth = Bind<AuthOptions>(configuration, AuthOptions.SectionName);

        auth.RequireHttpsMetadata.Should().BeTrue();
        auth.Audience.Should().Be("catalog-api");
        new AuthOptionsValidator(new FakeEnvironment("Production")).Validate(null, auth).FailureMessage.Should().Contain("Issuer");
    }

    [Fact]
    [Trait("Requirement", "REQ-ARC-002")]
    public void The_development_issuer_is_the_fixed_issuer_of_the_compose_stack()
    {
        var auth = Bind<AuthOptions>(Load(development: true, withPasswords: false), AuthOptions.SectionName);

        auth.Issuer.Should().Be("http://localhost:8180/realms/catalog");
        auth.RequireHttpsMetadata.Should().BeFalse();
    }
}
