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

    private const string Overrides = """[{ "Id": "Tool.Only.Package", "Version": "1.0.0", "License": "LicenseRef-Microsoft-DotNet-Library-ToolOnly" }]""";

    [Fact]
    [Trait("Requirement", "REQ-SUP-003")]
    public async Task A_repository_that_follows_the_policy_has_no_findings()
    {
        using var repository = Repository("""["MIT", "Apache-2.0", "MPL-2.0", "LGPL-2.1-only", "LicenseRef-Microsoft-DotNet-Library-ToolOnly"]""")
            .Add("src/App/packages.lock.json", Lock(("Some.Library", "1.0.0")))
            .Add("tests/App.Tests/packages.lock.json", Lock(("Tool.Only.Package", "1.0.0")));

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

    private static TestRepository Repository(string allowList) =>
        SecuritySamples.RepositoryWithPolicy()
            .Add("governance/policies/license-policy.json", allowList)
            .Add("governance/policies/license-overrides.json", Overrides)
            .Add("governance/policies/forbidden-packages.json", Forbidden);

    private static string Lock(params (string Id, string Version)[] packages)
    {
        var entries = string.Join(",", packages.Select(package => $"\"{package.Id}\": {{ \"type\": \"Direct\", \"requested\": \"[{package.Version}, )\", \"resolved\": \"{package.Version}\" }}"));
        return $"{{ \"version\": 2, \"dependencies\": {{ \"net10.0\": {{ {entries} }} }} }}";
    }
}
