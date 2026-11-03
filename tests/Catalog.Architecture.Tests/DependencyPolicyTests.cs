using Xunit;

namespace Catalog.Architecture.Tests;

/// <summary>
/// The dependency rules that the build cannot express. The license allow-list runs in CI through nuget-license. These tests cover
/// the forbidden-package list from ADR-0004 and the two kinds of license exception: a tool-only exception never reaches production code,
/// and the one Windows native runtime exception covers its exact package and version and nothing else.
/// </summary>
public sealed class DependencyPolicyTests
{
    private const string ToolPackage = "Microsoft.Testing.Extensions.CodeCoverage";

    private const string NativePackage = "Microsoft.Data.SqlClient.SNI.runtime";

    private const string NativeLicense = "LicenseRef-Microsoft-SqlClient-SNI-WindowsRuntime";

    private const string ToolOnlyException = """{ "Id": "Microsoft.Testing.Extensions.CodeCoverage", "Version": "18.11.2", "License": "LicenseRef-Microsoft-DotNet-Library-ToolOnly", "Kind": "tool-only" }""";

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
    public void The_license_exceptions_of_the_repository_state_a_kind_and_follow_its_rules()
    {
        var exceptions = DependencyPolicy.LoadLicenseExceptions();

        // An empty list would make the checks below pass without checking anything.
        Assert.NotEmpty(exceptions);
        Assert.Empty(DependencyPolicy.ExceptionDefinitionViolations(exceptions));
    }

    [Fact]
    [Trait("Requirement", "REQ-SUP-003")]
    public void A_license_exception_is_used_only_where_its_kind_allows()
    {
        var exceptions = DependencyPolicy.LoadLicenseExceptions();

        var violations = DependencyPolicy.LockFiles()
            .SelectMany(lockFile => DependencyPolicy.ExceptionUseViolations(lockFile, File.ReadAllText(Path.Combine(SolutionLayout.Root, lockFile)), exceptions))
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    [Trait("Requirement", "REQ-SUP-003")]
    public void A_tool_only_package_in_a_production_lock_file_is_rejected()
    {
        var exceptions = DependencyPolicy.ParseLicenseExceptions(Exceptions(ToolOnlyException));

        var violations = DependencyPolicy.ExceptionUseViolations("src/Catalog.Api/packages.lock.json", Lock("Transitive", (ToolPackage, "18.11.2")), exceptions);

        var violation = Assert.Single(violations);
        Assert.Contains($"{ToolPackage} has a tool-only license exception and must be used by test projects only", violation, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Requirement", "REQ-SUP-003")]
    [InlineData("tests/Catalog.Domain.Tests/packages.lock.json")]
    [InlineData("tools/Governance.Auditor/packages.lock.json")]
    public void A_tool_only_package_in_a_test_or_tool_lock_file_is_accepted(string lockFileName)
    {
        var exceptions = DependencyPolicy.ParseLicenseExceptions(Exceptions(ToolOnlyException));

        Assert.Empty(DependencyPolicy.ExceptionUseViolations(lockFileName, Lock("Transitive", (ToolPackage, "18.11.2")), exceptions));
    }

    [Theory]
    [Trait("Requirement", "REQ-SUP-003")]
    [InlineData("src/Catalog.Infrastructure/packages.lock.json", "Transitive")]
    [InlineData("src/Catalog.Infrastructure/packages.lock.json", "CentralTransitive")]
    [InlineData("tests/Catalog.Infrastructure.Tests/packages.lock.json", "Transitive")]
    [InlineData("tools/Governance.Auditor/packages.lock.json", "Transitive")]
    public void The_approved_native_runtime_may_be_a_dependency_of_any_project(string lockFileName, string type)
    {
        var exceptions = DependencyPolicy.ParseLicenseExceptions(Exceptions(ToolOnlyException, NativeException(NativePackage, "7.1.0", NativeLicense, "Windows assets.", "Not shipped.")));

        Assert.Empty(DependencyPolicy.ExceptionUseViolations(lockFileName, Lock(type, (NativePackage, "7.1.0")), exceptions));
    }

    [Theory]
    [Trait("Requirement", "REQ-SUP-003")]
    [InlineData("src/Catalog.Infrastructure/packages.lock.json", "7.1.1")]
    [InlineData("src/Catalog.Infrastructure/packages.lock.json", "7.0.0")]
    [InlineData("src/Catalog.Infrastructure/packages.lock.json", "8.0.0")]
    [InlineData("tests/Catalog.Infrastructure.Tests/packages.lock.json", "7.1.1")]
    public void The_approved_native_runtime_at_another_version_is_rejected(string lockFileName, string version)
    {
        var exceptions = DependencyPolicy.ParseLicenseExceptions(Exceptions(NativeException(NativePackage, "7.1.0", NativeLicense, "Windows assets.", "Not shipped.")));

        var violation = Assert.Single(DependencyPolicy.ExceptionUseViolations(lockFileName, Lock("Transitive", (NativePackage, version)), exceptions));

        Assert.Contains($"{NativePackage} {version} is not the version 7.1.0", violation, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-SUP-003")]
    public void The_approved_native_runtime_as_a_direct_reference_is_rejected()
    {
        var exceptions = DependencyPolicy.ParseLicenseExceptions(Exceptions(NativeException(NativePackage, "7.1.0", NativeLicense, "Windows assets.", "Not shipped.")));

        var violation = Assert.Single(DependencyPolicy.ExceptionUseViolations("src/Catalog.Infrastructure/packages.lock.json", Lock("Direct", (NativePackage, "7.1.0")), exceptions));

        Assert.Contains($"{NativePackage} is referenced directly", violation, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Requirement", "REQ-SUP-003")]
    [InlineData("Other.Native.Package", "1.0.0", NativeLicense)]
    [InlineData(NativePackage, "7.1.1", NativeLicense)]
    [InlineData(NativePackage, "7.0.0", NativeLicense)]
    [InlineData(NativePackage, "7.1.0", "LicenseRef-Other-License")]
    public void A_native_runtime_exception_for_any_other_package_or_version_is_rejected(string id, string version, string license)
    {
        var exceptions = DependencyPolicy.ParseLicenseExceptions(Exceptions(NativeException(id, version, license, "Windows assets.", "Not shipped.")));

        var violation = Assert.Single(DependencyPolicy.ExceptionDefinitionViolations(exceptions));

        Assert.Contains($"only {NativePackage} 7.1.0 under {NativeLicense} may have a windows-native-runtime exception", violation, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-SUP-003")]
    public void A_native_runtime_exception_for_another_package_does_not_open_a_production_project()
    {
        var exceptions = DependencyPolicy.ParseLicenseExceptions(Exceptions(NativeException("Other.Native.Package", "1.0.0", NativeLicense, "Windows assets.", "Not shipped.")));

        var violation = Assert.Single(DependencyPolicy.ExceptionUseViolations("src/Catalog.Api/packages.lock.json", Lock("Transitive", ("Other.Native.Package", "1.0.0")), exceptions));

        Assert.Contains("Other.Native.Package has a windows-native-runtime license exception and must be used by test projects only", violation, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Requirement", "REQ-SUP-003")]
    [InlineData(null, "Not shipped.", "must record its scope")]
    [InlineData("", "Not shipped.", "must record its scope")]
    [InlineData("   ", "Not shipped.", "must record its scope")]
    [InlineData("Windows assets.", null, "must record its reason")]
    [InlineData("Windows assets.", "", "must record its reason")]
    [InlineData("Windows assets.", "   ", "must record its reason")]
    public void A_native_runtime_exception_without_a_scope_or_a_reason_is_rejected(string? scope, string? reason, string expected)
    {
        var exceptions = DependencyPolicy.ParseLicenseExceptions(Exceptions(NativeException(NativePackage, "7.1.0", NativeLicense, scope, reason)));

        var violation = Assert.Single(DependencyPolicy.ExceptionDefinitionViolations(exceptions));

        Assert.Contains(expected, violation, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Requirement", "REQ-SUP-003")]
    [InlineData("""{ "Id": "Any.Package", "Version": "1.0.0", "License": "LicenseRef-Any" }""")]
    [InlineData("""{ "Id": "Any.Package", "Version": "1.0.0", "License": "LicenseRef-Any", "Kind": "anything-goes" }""")]
    [InlineData("""{ "Id": "Any.Package", "Version": "1.0.0", "License": "LicenseRef-Any", "Kind": "Tool-Only" }""")]
    public void A_license_exception_without_a_known_kind_is_rejected(string entry)
    {
        var exceptions = DependencyPolicy.ParseLicenseExceptions(Exceptions(entry));

        var violation = Assert.Single(DependencyPolicy.ExceptionDefinitionViolations(exceptions));

        Assert.Contains("has the kind 'tool-only' or 'windows-native-runtime'", violation, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-SUP-003")]
    public void The_exception_files_name_the_approved_native_runtime_exactly()
    {
        var native = DependencyPolicy.LoadLicenseExceptions().Where(exception => exception.Kind == DependencyPolicy.WindowsNativeRuntimeKind).ToList();

        // Every native exception is the approved one, and there is at most one of it, so the exception cannot widen without a change to this test.
        Assert.All(native, exception => Assert.True(DependencyPolicy.IsApprovedWindowsNativeRuntime(exception), $"{exception.Id} {exception.Version} is not the approved native runtime."));
        Assert.True(native.Count <= 1);
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

    private static string Exceptions(params string[] entries) => $"[{string.Join(",", entries)}]";

    private static string NativeException(string id, string version, string license, string? scope, string? reason)
    {
        var entry = $"{{ \"Id\": \"{id}\", \"Version\": \"{version}\", \"License\": \"{license}\", \"Kind\": \"windows-native-runtime\"";

        if (scope is not null)
        {
            entry += $", \"Scope\": \"{scope}\"";
        }

        if (reason is not null)
        {
            entry += $", \"Reason\": \"{reason}\"";
        }

        return entry + " }";
    }

    private static string Lock(string type, params (string Id, string Version)[] packages)
    {
        var entries = string.Join(",", packages.Select(package => $"\"{package.Id}\": {{ \"type\": \"{type}\", \"resolved\": \"{package.Version}\" }}"));
        return $"{{ \"version\": 2, \"dependencies\": {{ \"net10.0\": {{ {entries} }} }} }}";
    }
}
