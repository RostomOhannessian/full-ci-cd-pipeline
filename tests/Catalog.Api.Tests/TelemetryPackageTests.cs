using System.Xml.Linq;
using AwesomeAssertions;
using Catalog.Api.Tests.Support;
using Xunit;

namespace Catalog.Api.Tests;

/// <summary>
/// REQ-OBS-001: the instrumentation of EF Core and of StackExchange.Redis has only had beta releases, so both are pinned to an exact beta version that matches the release of the SDK, and every other OpenTelemetry package is
/// stable and on the same release (plan section 8.11, ADR-0043, ADR-0020). A Dependabot update that moves a beta, or that moves one package of the family and not the others, fails here until a person reads the release notes.
/// </summary>
public sealed class TelemetryPackageTests
{
    private static readonly string[] Betas = ["OpenTelemetry.Instrumentation.EntityFrameworkCore", "OpenTelemetry.Instrumentation.StackExchangeRedis"];

    private static Dictionary<string, string> Versions()
    {
        var props = XDocument.Load(Path.Combine(Repository.Root, "Directory.Packages.props"));

        return props.Descendants("PackageVersion")
            .Where(element => ((string?)element.Attribute("Include"))?.StartsWith("OpenTelemetry", StringComparison.Ordinal) == true)
            .ToDictionary(element => (string)element.Attribute("Include")!, element => (string)element.Attribute("Version")!, StringComparer.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-001")]
    public void The_family_has_the_packages_that_the_composition_root_names()
    {
        Versions().Keys.Should().BeEquivalentTo(
        [
            "OpenTelemetry.Extensions.Hosting",
            "OpenTelemetry.Exporter.OpenTelemetryProtocol",
            "OpenTelemetry.Exporter.InMemory",
            "OpenTelemetry.Instrumentation.AspNetCore",
            "OpenTelemetry.Instrumentation.Http",
            "OpenTelemetry.Instrumentation.SqlClient",
            "OpenTelemetry.Instrumentation.Runtime",
            "OpenTelemetry.Instrumentation.EntityFrameworkCore",
            "OpenTelemetry.Instrumentation.StackExchangeRedis",
        ]);
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-001")]
    public void The_two_beta_instrumentations_are_pinned_to_an_exact_beta_and_every_other_package_is_a_stable_release()
    {
        foreach (var (package, version) in Versions())
        {
            version.Should().NotContainAny("[", "(", ",", "*", "$").And.MatchRegex(@"^\d+\.\d+\.\d+(-beta\.\d+)?$", $"{package} is pinned to one exact version");

            if (Betas.Contains(package))
            {
                version.Should().Contain("-beta.", $"{package} has never had a stable release, and the pin says so");
            }
            else
            {
                version.Should().NotContain("-", $"{package} is a stable release");
            }
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-OBS-001")]
    public void Every_package_of_the_family_is_on_the_release_of_the_sdk()
    {
        var versions = Versions();
        var release = versions["OpenTelemetry.Extensions.Hosting"];

        foreach (var (package, version) in versions)
        {
            version.Split('-')[0].Should().Be(release, $"{package} must match the release of the SDK");
        }
    }
}
