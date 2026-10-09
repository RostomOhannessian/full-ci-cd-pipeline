using Documentation.Auditor.Tests.Support;
using Xunit;

namespace Documentation.Auditor.Tests;

/// <summary>REQ-DOC-002: the inventory records every tool with its tier, lifecycle, version, license, sources, and freshness, and the file is checked.</summary>
public sealed class InventoryTests
{
    [Fact]
    [Trait("Requirement", "REQ-DOC-002")]
    public async Task A_conforming_inventory_passes_the_audit()
    {
        using var repository = Samples.Baseline();

        var result = await CliRunner.RunAsync(repository, "audit");

        Expect.Passes(result);
        Assert.Contains("documentation audit: passed", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Requirement", "REQ-DOC-002")]
    [InlineData("    name: Acme Lint\n", "")]
    [InlineData("    category: docs-tool\n", "")]
    [InlineData("    version: \"1.0.0\"\n", "")]
    [InlineData("    sources:\n      - https://example.test/acme-lint\n", "    sources: []\n")]
    [InlineData("    category: docs-tool\n", "    category: made-up\n")]
    [InlineData("    tier: B\n", "    tier: D\n")]
    [InlineData("    license-source: https://example.test/acme-lint/license\n", "    license-source: http://example.test/insecure\n")]
    [InlineData("    adrs: [\"0001\"]\n", "    adrs: [\"1\"]\n")]
    [InlineData("    detect: [\"image:registry.example.test/acme/lint\"]\n", "    detect: [\"registry.example.test/acme/lint\"]\n")]
    public async Task An_entry_that_breaks_the_schema_fails_with_a_schema_finding(string find, string replacement)
    {
        using var repository = Samples.Baseline().Replace("docs/reference/tools/inventory.yaml", find, replacement);

        var result = await CliRunner.RunAsync(repository, "inventory");

        Expect.FailsOnlyWith(result, "inventory-schema");
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-002")]
    public async Task An_unknown_field_fails_the_schema_so_a_typo_cannot_hide_a_value()
    {
        using var repository = Samples.Baseline().Replace("docs/reference/tools/inventory.yaml", "    purpose: Lints the documentation.\n", "    purpose: Lints the documentation.\n    licence-note: oops\n");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Expect.FailsOnlyWith(result, "inventory-schema");
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-002")]
    public async Task An_inventory_that_is_not_valid_yaml_fails_without_a_stack_trace()
    {
        using var repository = Samples.Baseline().Add("docs/reference/tools/inventory.yaml", "entries: [unclosed\n");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Expect.FailsOnlyWith(result, "inventory-schema");
        Assert.DoesNotContain("Exception", result.Output + result.Error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-002")]
    public async Task A_repeated_ID_fails()
    {
        using var repository = Samples.Baseline().Replace("docs/reference/tools/inventory.yaml", "  - id: old-tool\n", "  - id: acme-lint\n");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Assert.Contains("[inventory-duplicate-id]", result.Output, StringComparison.Ordinal);
        Assert.Equal(1, result.ExitCode);
    }

    [Theory]
    [Trait("Requirement", "REQ-DOC-002")]
    [InlineData("    lifecycle: adopt\n    usage: in-use\n", "    lifecycle: assess\n    usage: in-use\n")]
    [InlineData("    lifecycle: hold\n    usage: evaluated-only\n    introduced: n/a\n", "    lifecycle: hold\n    usage: planned\n    introduced: n/a\n")]
    [InlineData("    tier: n/a\n    lifecycle: hold\n", "    tier: B\n    lifecycle: hold\n")]
    [InlineData("    lifecycle: hold\n    usage: evaluated-only\n    introduced: n/a\n", "    lifecycle: adopt\n    usage: planned\n    introduced: n/a\n")]
    public async Task A_lifecycle_that_disagrees_with_the_usage_tier_or_phase_fails(string find, string replacement)
    {
        using var repository = Samples.Baseline().Replace("docs/reference/tools/inventory.yaml", find, replacement);

        var result = await CliRunner.RunAsync(repository, "inventory");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("[inventory-consistency]", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-002")]
    public async Task An_ADR_link_to_a_decision_that_does_not_exist_fails()
    {
        using var repository = Samples.Baseline().Replace("docs/reference/tools/inventory.yaml", "adrs: [\"0001\"]", "adrs: [\"0002\"]");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Expect.FailsOnlyWith(result, "inventory-adr-link");
        Assert.Contains("ADR-0002", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-002")]
    public async Task The_inventory_is_flagged_when_it_was_verified_more_than_180_days_ago()
    {
        using var repository = Samples.Baseline().Replace("docs/reference/tools/inventory.yaml", "last-verified: 2026-10-01", "last-verified: 2026-04-01");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Expect.Passes(result);
        Assert.Contains("warning [inventory-stale]", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Requirement", "REQ-DOC-002")]
    [InlineData("2026-04-11", false)]
    [InlineData("2026-04-10", true)]
    public async Task The_freshness_limit_is_exactly_180_days(string verified, bool flagged)
    {
        using var repository = Samples.Baseline().Replace("docs/reference/tools/inventory.yaml", "last-verified: 2026-10-01", $"last-verified: {verified}");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Assert.Equal(flagged, result.Output.Contains("[inventory-stale]", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-002")]
    public async Task A_verification_date_that_is_not_a_calendar_date_fails()
    {
        using var repository = Samples.Baseline().Replace("docs/reference/tools/inventory.yaml", "last-verified: 2026-10-01", "last-verified: 2026-13-45");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Expect.FailsOnlyWith(result, "inventory-stale");
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-002")]
    public async Task A_missing_policy_file_stops_the_tool_with_exit_code_2()
    {
        using var repository = Samples.Baseline().Remove("governance/policies/documentation-policy.yaml");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("documentation-policy.yaml", result.Error, StringComparison.Ordinal);
    }
}

/// <summary>REQ-DOC-003 and ADR-0004: a tool with a license that is not permissive needs a note that says why it is acceptable.</summary>
public sealed class LicenseNoteTests
{
    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public async Task A_non_permissive_license_without_a_note_fails()
    {
        using var repository = Samples.Baseline().Replace(
            "docs/reference/tools/inventory.yaml",
            "\n    notes: Held because it is not an open source license, and it is not linked or offered as a service.",
            string.Empty);

        var result = await CliRunner.RunAsync(repository, "inventory");

        Expect.FailsOnlyWith(result, "inventory-license-note");
        Assert.Contains("old-tool", result.Output, StringComparison.Ordinal);
        Assert.Contains("BUSL-1.1", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public async Task A_non_permissive_license_with_a_note_passes()
    {
        using var repository = Samples.Baseline();

        Expect.Passes(await CliRunner.RunAsync(repository, "inventory"));
    }

    [Theory]
    [Trait("Requirement", "REQ-DOC-003")]
    [InlineData("MIT", false)]
    [InlineData("Apache-2.0", false)]
    [InlineData("apache-2.0", false)]
    [InlineData("service", false)]
    [InlineData("n/a", false)]
    [InlineData("MIT and Apache-2.0", false)]
    [InlineData("MIT or Apache-2.0", false)]
    [InlineData("(MIT AND Apache-2.0)", false)]
    [InlineData("MIT and AGPL-3.0", true)]
    [InlineData("AGPL-3.0", true)]
    [InlineData("proprietary, free developer edition", true)]
    [InlineData("Community-Spec-1.0", true)]
    public async Task Every_term_of_a_license_expression_must_be_permissive_or_the_entry_needs_a_note(string license, bool needsNote)
    {
        using var repository = Samples.Baseline().Replace("docs/reference/tools/inventory.yaml", "    license: MIT\n", $"    license: {license}\n");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Assert.Equal(needsNote, result.Output.Contains("[inventory-license-note]", StringComparison.Ordinal));
    }
}
