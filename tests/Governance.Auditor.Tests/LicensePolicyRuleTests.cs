using Governance.Auditor.Common;
using Governance.Auditor.Security;
using Governance.Auditor.Tests.Support;
using Xunit;

namespace Governance.Auditor.Tests;

public sealed class LicensePolicyRuleTests
{
    private const string Forbidden = """
        { "schema-version": 1, "forbidden": [
          { "id": "FluentAssertions", "minVersion": "8.0.0", "reason": "Commercial terms from version 8." },
          { "id": "QuestPDF", "reason": "Not OSI approved." } ] }
        """;

    private const string ToolOnlyOverride = """{ "Id": "Tool.Only.Package", "Version": "1.0.0", "License": "LicenseRef-Microsoft-DotNet-Library-ToolOnly", "Kind": "tool-only" }""";

    // The package, version, and license that security-policy.yaml approves as a Windows native runtime.
    private const string NativePackage = "Microsoft.Data.SqlClient.SNI.runtime";

    private const string NativeLicense = "LicenseRef-Microsoft-SqlClient-SNI-WindowsRuntime";

    private const string FollowingAllowList = """["MIT", "Apache-2.0", "MPL-2.0", "LGPL-2.1-only", "LicenseRef-Microsoft-DotNet-Library-ToolOnly", "LicenseRef-Microsoft-SqlClient-SNI-WindowsRuntime"]""";

    private static readonly string Overrides = $"[{ToolOnlyOverride}, {NativeOverride(NativePackage, "7.1.0", NativeLicense, "Windows assets of this version.", "No shipped artifact contains it.")}]";

    [Fact]
    [Trait("Requirement", "REQ-SUP-003")]
    public async Task A_repository_that_follows_the_policy_has_no_findings()
    {
        using var repository = Repository(FollowingAllowList)
            .Add("src/App/packages.lock.json", LockOf("Transitive", ("Some.Library", "1.0.0"), (NativePackage, "7.1.0")))
            .Add("tests/App.Tests/packages.lock.json", Lock(("Tool.Only.Package", "1.0.0")))
            .Add("tests/App.Integration.Tests/packages.lock.json", LockOf("CentralTransitive", (NativePackage, "7.1.0")));

        Assert.Empty(await SecuritySamples.RunAsync(repository, "license-policy"));
    }

    [Theory]
    [Trait("Requirement", "REQ-SUP-003")]
    [InlineData("GPL-3.0-only")]
    [InlineData("AGPL-3.0-or-later")]
    [InlineData("SSPL-1.0")]
    [InlineData("BUSL-1.1")]
    [InlineData("OSMFEULA")]
    [InlineData("gpl-2.0-only")]
    public async Task The_allow_list_may_not_admit_a_license_that_the_adr_rejects(string license)
    {
        using var repository = Repository($"[\"MIT\", \"{license}\"]");

        var finding = Assert.Single(await SecuritySamples.RunAsync(repository, "license-policy"));

        Assert.Contains($"admits '{license}'", finding.Message, StringComparison.Ordinal);
        Assert.Equal("governance/policies/license-policy.json", finding.Path);
    }

    [Fact]
    [Trait("Requirement", "REQ-SUP-003")]
    public async Task A_license_reference_is_only_allowed_when_the_policy_names_it()
    {
        using var repository = Repository("""["MIT", "LicenseRef-Anything-Goes"]""");

        var finding = Assert.Single(await SecuritySamples.RunAsync(repository, "license-policy"));

        Assert.Contains("LicenseRef-Anything-Goes", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-SUP-003")]
    public async Task A_tool_only_exception_in_a_production_project_is_rejected()
    {
        using var repository = Repository("""["MIT"]""").Add("src/App/packages.lock.json", Lock(("Tool.Only.Package", "1.0.0")));

        var finding = Assert.Single(await SecuritySamples.RunAsync(repository, "license-policy"));

        Assert.Contains("tool-only license exception but is used by a production project", finding.Message, StringComparison.Ordinal);
        Assert.Equal("src/App/packages.lock.json", finding.Path);
    }

    [Theory]
    [Trait("Requirement", "REQ-SUP-003")]
    [InlineData("src/App/packages.lock.json")]
    [InlineData("tests/App.Tests/packages.lock.json")]
    [InlineData("tools/Tool/packages.lock.json")]
    public async Task The_native_runtime_exception_covers_only_the_approved_version(string lockPath)
    {
        using var repository = Repository("""["MIT"]""").Add(lockPath, LockOf("Transitive", (NativePackage, "7.1.1")));

        var finding = Assert.Single(await SecuritySamples.RunAsync(repository, "license-policy"));

        Assert.Contains($"{NativePackage} 7.1.1 is not the version 7.1.0 that the Windows native runtime exception covers", finding.Message, StringComparison.Ordinal);
        Assert.Equal(lockPath, finding.Path);
    }

    [Fact]
    [Trait("Requirement", "REQ-SUP-003")]
    public async Task A_direct_reference_to_the_native_runtime_is_rejected()
    {
        using var repository = Repository("""["MIT"]""").Add("src/App/packages.lock.json", Lock((NativePackage, "7.1.0")));

        var finding = Assert.Single(await SecuritySamples.RunAsync(repository, "license-policy"));

        Assert.Contains($"{NativePackage} is referenced directly", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-SUP-003")]
    public async Task An_override_without_a_kind_is_rejected()
    {
        const string overrides = """[{ "Id": "Tool.Only.Package", "Version": "1.0.0", "License": "LicenseRef-Microsoft-DotNet-Library-ToolOnly" }]""";
        using var repository = Repository("""["MIT"]""", overrides);

        var finding = Assert.Single(await SecuritySamples.RunAsync(repository, "license-policy"));

        Assert.Contains("Tool.Only.Package has no kind", finding.Message, StringComparison.Ordinal);
        Assert.Equal("governance/policies/license-overrides.json", finding.Path);
    }

    [Fact]
    [Trait("Requirement", "REQ-SUP-003")]
    public async Task An_override_with_an_unknown_kind_is_rejected()
    {
        const string overrides = """[{ "Id": "Any.Package", "Version": "1.0.0", "License": "LicenseRef-Anything", "Kind": "anything-goes" }]""";
        using var repository = Repository("""["MIT"]""", overrides);

        var finding = Assert.Single(await SecuritySamples.RunAsync(repository, "license-policy"));

        Assert.Contains("the kind 'anything-goes'", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Requirement", "REQ-SUP-003")]
    [InlineData("Other.Native.Package", "1.0.0", NativeLicense)]
    [InlineData(NativePackage, "7.1.1", NativeLicense)]
    [InlineData(NativePackage, "7.1.0", "LicenseRef-Other-License")]
    public async Task A_native_runtime_override_for_anything_but_the_approved_package_is_rejected(string id, string version, string license)
    {
        var overrides = $"[{NativeOverride(id, version, license, "Windows assets.", "Not shipped.")}]";
        using var repository = Repository("""["MIT"]""", overrides);

        var finding = Assert.Single(await SecuritySamples.RunAsync(repository, "license-policy"));

        Assert.Contains("does not approve that package, version, and license", finding.Message, StringComparison.Ordinal);
        Assert.Equal("governance/policies/license-overrides.json", finding.Path);
    }

    [Fact]
    [Trait("Requirement", "REQ-SUP-003")]
    public async Task A_native_runtime_override_that_is_not_approved_does_not_open_a_production_project()
    {
        var overrides = $"[{NativeOverride("Other.Native.Package", "1.0.0", NativeLicense, "Windows assets.", "Not shipped.")}]";
        using var repository = Repository("""["MIT"]""", overrides).Add("src/App/packages.lock.json", LockOf("Transitive", ("Other.Native.Package", "1.0.0")));

        var findings = await SecuritySamples.RunAsync(repository, "license-policy");

        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, finding => finding.Message.Contains("does not approve", StringComparison.Ordinal));
        Assert.Contains(findings, finding => finding.Path == "src/App/packages.lock.json" && finding.Message.Contains("is used by a production project", StringComparison.Ordinal));
    }

    [Theory]
    [Trait("Requirement", "REQ-SUP-003")]
    [InlineData(null, "No shipped artifact contains it.", "no scope")]
    [InlineData("", "No shipped artifact contains it.", "no scope")]
    [InlineData("   ", "No shipped artifact contains it.", "no scope")]
    [InlineData("Windows assets of this version.", null, "no reason")]
    [InlineData("Windows assets of this version.", "", "no reason")]
    [InlineData("Windows assets of this version.", "   ", "no reason")]
    public async Task A_native_runtime_override_without_a_scope_or_a_reason_is_rejected(string? scope, string? reason, string expected)
    {
        var overrides = $"[{NativeOverride(NativePackage, "7.1.0", NativeLicense, scope, reason)}]";
        using var repository = Repository("""["MIT"]""", overrides);

        var finding = Assert.Single(await SecuritySamples.RunAsync(repository, "license-policy"));

        Assert.Contains(expected, finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Requirement", "REQ-SUP-003")]
    [InlineData("FluentAssertions", "8.0.0", true)]
    [InlineData("FluentAssertions", "8.3.1", true)]
    [InlineData("FluentAssertions", "8.0.0-beta.1", true)]
    [InlineData("fluentassertions", "9.0.0", true)]
    [InlineData("FluentAssertions", "7.2.0", false)]
    [InlineData("QuestPDF", "2024.12.1", true)]
    [InlineData("AwesomeAssertions", "9.6.0", false)]
    public async Task A_forbidden_package_is_found_by_id_and_version_in_any_lock_file(string id, string version, bool expectedForbidden)
    {
        using var repository = Repository("""["MIT"]""").Add("tools/Tool/packages.lock.json", Lock((id, version)));

        var findings = await SecuritySamples.RunAsync(repository, "license-policy");

        Assert.Equal(expectedForbidden, findings.Count == 1);
    }

    [Fact]
    [Trait("Requirement", "REQ-SUP-003")]
    public async Task A_forbidden_package_that_arrives_transitively_is_found()
    {
        const string lockFile = """
            { "version": 2, "dependencies": { "net10.0": {
              "Some.Library": { "type": "Direct", "requested": "[1.0.0, )", "resolved": "1.0.0", "dependencies": { "FluentAssertions": "[8.1.0, )" } },
              "FluentAssertions": { "type": "Transitive", "resolved": "8.1.0" },
              "Catalog.Domain": { "type": "Project" } } } }
            """;
        using var repository = Repository("""["MIT"]""").Add("tests/App.Tests/packages.lock.json", lockFile);

        var finding = Assert.Single(await SecuritySamples.RunAsync(repository, "license-policy"));

        Assert.Contains("FluentAssertions 8.1.0 is forbidden", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-SUP-003")]
    public async Task Missing_policy_files_are_errors_because_nothing_would_be_enforced()
    {
        using var repository = SecuritySamples.RepositoryWithPolicy();

        var findings = await SecuritySamples.RunAsync(repository, "license-policy");

        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, finding => finding.Path == "governance/policies/license-policy.json");
        Assert.Contains(findings, finding => finding.Path == "governance/policies/forbidden-packages.json");
    }

    [Fact]
    [Trait("Requirement", "REQ-SUP-003")]
    public async Task The_real_policy_files_pass_the_rule_against_the_real_lock_files()
    {
        var findings = await RunOnRealRepositoryAsync();

        Assert.Empty(findings);
    }

    private static async Task<IReadOnlyList<Finding>> RunOnRealRepositoryAsync()
    {
        var policy = SecurityPolicy.Load(RealRepository.Files);
        var context = new SecurityContext(RealRepository.Files, policy, new FakeProcessRunner(), range: null);
        return await SecurityAuditor.RunAsync(context, ["license-policy"], TestContext.Current.CancellationToken);
    }

    private static TestRepository Repository(string allowList, string? overrides = null) =>
        SecuritySamples.RepositoryWithPolicy()
            .Add("governance/policies/license-policy.json", allowList)
            .Add("governance/policies/license-overrides.json", overrides ?? Overrides)
            .Add("governance/policies/forbidden-packages.json", Forbidden);

    private static string Lock(params (string Id, string Version)[] packages) => LockOf("Direct", packages);

    private static string LockOf(string type, params (string Id, string Version)[] packages)
    {
        var entries = string.Join(",", packages.Select(package => $"\"{package.Id}\": {{ \"type\": \"{type}\", \"requested\": \"[{package.Version}, )\", \"resolved\": \"{package.Version}\" }}"));
        return $"{{ \"version\": 2, \"dependencies\": {{ \"net10.0\": {{ {entries} }} }} }}";
    }

    private static string NativeOverride(string id, string version, string license, string? scope, string? reason)
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
}
