using Xunit;

namespace Catalog.Architecture.Tests;

/// <summary>
/// The dependency rules that the build cannot express. The license allow-list runs in CI through nuget-license. These tests cover
/// the forbidden-package list from ADR-0004 and the rule that a tool-only license exception never reaches production code.
/// </summary>
public sealed class DependencyPolicyTests
{
    [Fact]
    [Trait("Requirement", "REQ-SUP-003")]
    public void No_resolved_package_is_forbidden()
    {
        var forbidden = DependencyPolicy.LoadForbidden();

        var violations = DependencyPolicy.LockFiles()
            .SelectMany(lockFile => DependencyPolicy.Violations(lockFile, File.ReadAllText(Path.Combine(SolutionLayout.Root, lockFile)), forbidden))
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    [Trait("Requirement", "REQ-SUP-003")]
    public void The_lock_file_parser_sees_the_packages_of_a_real_lock_file()
    {
        // An empty result would make every forbidden-package check pass without checking anything.
        var lockFile = File.ReadAllText(Path.Combine(SolutionLayout.Root, "tests/Catalog.Architecture.Tests/packages.lock.json"));

        var packages = DependencyPolicy.ResolvedPackages(lockFile).Select(package => package.Id).ToList();

        Assert.Contains("xunit.v3", packages);
        Assert.Contains("TngTech.ArchUnitNET.xUnitV3", packages);
        Assert.DoesNotContain("Catalog.Domain", packages);
    }

    [Fact]
    [Trait("Requirement", "REQ-SUP-003")]
    public void A_license_exception_for_a_tool_never_reaches_a_production_project()
    {
        var exceptions = DependencyPolicy.LoadLicenseExceptions();

        var leaks = DependencyPolicy.LockFiles()
            .Where(lockFile => lockFile.StartsWith("src/", StringComparison.Ordinal))
            .SelectMany(lockFile => DependencyPolicy.ResolvedPackages(File.ReadAllText(Path.Combine(SolutionLayout.Root, lockFile)))
                .Where(package => exceptions.Contains(package.Id, StringComparer.OrdinalIgnoreCase))
                .Select(package => $"{lockFile}: {package.Id} has a tool-only license exception and must be used by test projects only."))
            .ToList();

        Assert.Empty(leaks);
    }

    [Fact]
    [Trait("Requirement", "REQ-SUP-003")]
    public void Every_project_has_a_lock_file_so_a_locked_restore_can_detect_drift()
    {
        var projects = SolutionLayout.ProjectFilesOnDisk().Select(path => Path.GetDirectoryName(path)!.Replace('\\', '/'));
        var locks = DependencyPolicy.LockFiles().Select(path => Path.GetDirectoryName(path)!.Replace('\\', '/'));

        Assert.Equal(projects.Order(StringComparer.Ordinal), locks);
    }

    [Theory]
    [Trait("Requirement", "REQ-SUP-003")]
    [InlineData("FluentAssertions", "8.0.0", true)]
    [InlineData("FluentAssertions", "8.3.1", true)]
    [InlineData("FluentAssertions", "8.0.0-beta.1", true)]
    [InlineData("FluentAssertions", "7.2.0", false)]
    [InlineData("fluentassertions", "9.0.0", true)]
    [InlineData("MediatR", "13.0.0", true)]
    [InlineData("MediatR", "12.5.0", false)]
    [InlineData("AutoMapper", "15.0.0", true)]
    [InlineData("AutoMapper", "14.0.0", false)]
    [InlineData("MassTransit", "9.0.0", true)]
    [InlineData("MassTransit", "8.5.0", false)]
    [InlineData("QuestPDF", "2024.12.1", true)]
    [InlineData("AwesomeAssertions", "9.6.0", false)]
    public void The_forbidden_list_rejects_exactly_the_forbidden_versions(string id, string version, bool expectedForbidden)
    {
        var lockFile = $$"""
            { "version": 2, "dependencies": { "net10.0": { "{{id}}": { "type": "Transitive", "resolved": "{{version}}" } } } }
            """;

        var violations = DependencyPolicy.Violations("sample/packages.lock.json", lockFile, DependencyPolicy.LoadForbidden());

        Assert.Equal(expectedForbidden, violations.Count > 0);
    }

    [Fact]
    [Trait("Requirement", "REQ-SUP-003")]
    public void A_forbidden_package_that_arrives_transitively_is_found_in_the_lock_file()
    {
        const string lockFile = """
            {
              "version": 2,
              "dependencies": {
                "net10.0": {
                  "Some.Library": { "type": "Direct", "requested": "[1.0.0, )", "resolved": "1.0.0", "dependencies": { "MediatR": "[13.0.0, )" } },
                  "MediatR": { "type": "Transitive", "resolved": "13.0.0" },
                  "Catalog.Domain": { "type": "Project" }
                }
              }
            }
            """;

        var violations = DependencyPolicy.Violations("sample/packages.lock.json", lockFile, DependencyPolicy.LoadForbidden());

        Assert.Single(violations);
        Assert.Contains("MediatR 13.0.0 is forbidden", violations[0], StringComparison.Ordinal);
    }
}
