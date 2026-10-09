using Documentation.Auditor.Policy;
using Documentation.Auditor.Tests.Support;
using Governance.Auditor.Common;
using Xunit;

namespace Documentation.Auditor.Tests;

/// <summary>The policy file is data that the rules read, so a broken policy must fail loudly before any file is checked.</summary>
public sealed class PolicyTests
{
    [Fact]
    [Trait("Requirement", "REQ-DOC-004")]
    public void The_real_policy_loads_and_requires_the_sections_of_plan_section_11_2()
    {
        var policy = DocumentationPolicy.Load(RealRepository.Files);

        Assert.Equal(
            ["Overview", "Decision rationale", "Setup tutorial", "How this project uses it", "Validation and troubleshooting", "Security and operations", "Lab", "Further research"],
            policy.ToolPages.Sections["A"]);
        Assert.Equal(["Overview", "Decision rationale", "Setup tutorial", "Further research"], policy.ToolPages.Sections["B"]);
        Assert.Equal(180, policy.Freshness.WarnAfterDays);
        Assert.Equal("illustrative", policy.Pages.CommandBlocks.Marker);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public void The_real_policy_permits_only_licenses_from_the_adr_0004_allow_list()
    {
        var policy = DocumentationPolicy.Load(RealRepository.Files);
        var allowList = File.ReadAllText(Path.Combine(RealRepository.Root, "governance", "policies", "license-policy.json"));

        foreach (var license in policy.Inventory.PermissiveLicenses.Where(license => license != "CC0-1.0" && license != "Unlicense"))
        {
            Assert.Contains($"\"{license}\"", allowList, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(policy.Inventory.PermissiveLicenses, license => license.StartsWith("GPL", StringComparison.Ordinal) || license.StartsWith("AGPL", StringComparison.Ordinal) || license.StartsWith("BUSL", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public void An_unknown_key_in_the_policy_is_refused_so_a_typo_cannot_weaken_a_rule()
    {
        using var repository = Samples.Baseline().Replace("governance/policies/documentation-policy.yaml", "freshness:\n  warn-after-days: 180\n", "freshness:\n  warn-after-days: 180\n  warn-after-day: 365\n");

        var exception = Assert.Throws<GovernanceException>(() => DocumentationPolicy.Load(repository.Files));

        Assert.Contains("documentation-policy.yaml", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Requirement", "REQ-DOC-003")]
    [InlineData("schema-version: 1", "schema-version: 2", "schema-version 2 is not supported")]
    [InlineData("warn-after-days: 180", "warn-after-days: 0", "at least 1")]
    [InlineData("    B: [Overview, Decision rationale, Setup tutorial, Further research]\n", "    B: []\n", "tier B")]
    [InlineData("    marker: illustrative\n", "    marker: ''\n", "marker must not be empty")]
    [InlineData("      required: [title, description, status, date, decision-makers]\n", "      required: []\n", "needs paths and required keys")]
    public void A_policy_that_cannot_be_applied_stops_the_tool(string find, string replacement, string expected)
    {
        using var repository = Samples.Baseline().Replace("governance/policies/documentation-policy.yaml", find, replacement);

        var exception = Assert.Throws<GovernanceException>(() => DocumentationPolicy.Load(repository.Files));

        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-004")]
    public void An_owed_group_without_a_reason_stops_the_tool()
    {
        using var repository = Samples.Baseline().Replace("governance/policies/documentation-policy.yaml", "discovery:\n", "  owed:\n    - due: WP1.13\n      reason: ' '\n      tools: [acme-lint]\ndiscovery:\n");

        var exception = Assert.Throws<GovernanceException>(() => DocumentationPolicy.Load(repository.Files));

        Assert.Contains("owed-pages entry needs", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public void Every_unsupported_source_in_the_real_policy_names_the_work_package_that_adds_it()
    {
        var policy = DocumentationPolicy.Load(RealRepository.Files);

        Assert.NotEmpty(policy.Discovery.UnsupportedSources);
        Assert.All(policy.Discovery.UnsupportedSources, source => Assert.Matches(@"^WP\d+\.\d+$", source.Owner));
    }
}
