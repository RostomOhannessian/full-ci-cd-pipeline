using Documentation.Auditor.Tests.Support;
using Xunit;

namespace Documentation.Auditor.Tests;

/// <summary>REQ-DOC-004: a tool page has the sections its tier requires, and a tool that is in use has a page, a catalog entry, or a dated debt.</summary>
public sealed class ToolPageTests
{
    private const string Inventory = "docs/reference/tools/inventory.yaml";
    private const string Policy = "governance/policies/documentation-policy.yaml";

    private const string TierAPage = """
---
title: "Acme Platform"
description: "A platform."
audience: [contributors]
tier: A
tools: [acme-platform]
introduced: "phase-3"
prerequisites: ["Docker"]
estimated-time: "30 minutes"
last-verified: 2026-10-01
verified-against: { acme-platform: "2.0.0" }
owner: "@owner"
---

# Acme Platform

## Overview

What it is.

## Decision rationale

Why.

## Setup tutorial

How.

## How this project uses it

Where.

## Validation and troubleshooting

Checks.

## Security and operations

Risks.

## Lab

None yet. WP3.10 adds it.

## Further research

- [Docs](https://example.test/acme-platform)
""";

    [Fact]
    [Trait("Requirement", "REQ-DOC-004")]
    public async Task A_tier_A_page_with_every_section_passes()
    {
        using var repository = Samples.Baseline().Add("docs/reference/tools/acme-platform.md", TierAPage);

        Expect.Passes(await CliRunner.RunAsync(repository, "inventory"));
    }

    [Theory]
    [Trait("Requirement", "REQ-DOC-004")]
    [InlineData("## Overview")]
    [InlineData("## Decision rationale")]
    [InlineData("## Setup tutorial")]
    [InlineData("## How this project uses it")]
    [InlineData("## Validation and troubleshooting")]
    [InlineData("## Security and operations")]
    [InlineData("## Lab")]
    [InlineData("## Further research")]
    public async Task A_tier_A_page_without_a_required_section_fails_and_names_the_section(string heading)
    {
        using var repository = Samples.Baseline().Add("docs/reference/tools/acme-platform.md", TierAPage.Replace(heading + "\n", "### Renamed\n", StringComparison.Ordinal));

        var result = await CliRunner.RunAsync(repository, "inventory");

        Expect.FailsOnlyWith(result, "tool-page-section");
        Assert.Contains($"'{heading}'", result.Output, StringComparison.Ordinal);
        Assert.Contains("docs/reference/tools/acme-platform.md", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Requirement", "REQ-DOC-004")]
    [InlineData("## Overview")]
    [InlineData("## Decision rationale")]
    [InlineData("## Setup tutorial")]
    [InlineData("## Further research")]
    public async Task A_tier_B_page_needs_the_four_required_sections(string heading)
    {
        using var repository = Samples.Baseline();
        repository.Replace("docs/reference/tools/acme-lint.md", heading + "\n", "### Renamed\n");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Expect.FailsOnlyWith(result, "tool-page-section");
        Assert.Contains($"'{heading}'", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-004")]
    public async Task A_tier_B_page_does_not_need_the_extra_tier_A_sections()
    {
        using var repository = Samples.Baseline();

        Assert.DoesNotContain("## Lab", repository.Read("docs/reference/tools/acme-lint.md"), StringComparison.Ordinal);
        Expect.Passes(await CliRunner.RunAsync(repository, "inventory"));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-004")]
    public async Task A_section_with_no_content_fails_even_when_the_heading_is_there()
    {
        using var repository = Samples.Baseline();
        repository.Replace("docs/reference/tools/acme-lint.md", "## Decision rationale\n\nWhy it was chosen.\n", "## Decision rationale\n\n<!-- Write this. -->\n");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Expect.FailsOnlyWith(result, "tool-page-section");
        Assert.Contains("is empty", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-004")]
    public async Task A_heading_inside_a_code_fence_does_not_count_as_a_section()
    {
        using var repository = Samples.Baseline();
        repository.Replace("docs/reference/tools/acme-lint.md", "## Decision rationale\n\nWhy it was chosen.\n", "```text\n## Decision rationale\n```\n");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Expect.FailsOnlyWith(result, "tool-page-section");
        Assert.Contains("'## Decision rationale'", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-004")]
    public async Task A_tool_in_use_with_no_page_fails_and_says_where_the_page_belongs()
    {
        using var repository = Samples.Baseline().Remove("docs/reference/tools/acme-lint.md");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Expect.FailsOnlyWith(result, "tool-page-missing");
        Assert.Contains("docs/reference/tools/acme-lint.md", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-004")]
    public async Task A_tool_that_is_only_planned_needs_no_page()
    {
        using var repository = Samples.Baseline();

        Assert.False(File.Exists(Path.Combine(repository.Root, "docs/reference/tools/acme-platform.md")));
        Expect.Passes(await CliRunner.RunAsync(repository, "inventory"));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-004")]
    public async Task A_page_whose_tier_disagrees_with_the_inventory_fails()
    {
        using var repository = Samples.Baseline();
        repository.Replace("docs/reference/tools/acme-lint.md", "tier: B", "tier: A");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("[tool-page-tier]", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-004")]
    public async Task A_page_that_is_named_after_no_inventory_entry_fails()
    {
        using var repository = Samples.Baseline().Add("docs/reference/tools/mystery.md", Samples.ToolPage.Replace("acme-lint", "mystery", StringComparison.Ordinal));

        var result = await CliRunner.RunAsync(repository, "inventory");

        Assert.Contains("[tool-page-unknown]", result.Output, StringComparison.Ordinal);
        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-004")]
    public async Task A_page_that_leaves_its_own_ID_out_of_the_tools_list_fails()
    {
        using var repository = Samples.Baseline();
        repository.Replace("docs/reference/tools/acme-lint.md", "tools: [acme-lint]", "tools: [acme-lib]");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Expect.FailsOnlyWith(result, "tool-page-front-matter");
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-004")]
    public async Task A_page_that_names_no_verified_version_fails()
    {
        using var repository = Samples.Baseline();
        repository.Replace("docs/reference/tools/acme-lint.md", "verified-against: { acme-lint: \"1.0.0\" }", "verified-against: { other: \"1.0.0\" }");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Expect.FailsOnlyWith(result, "tool-page-front-matter");
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-004")]
    public async Task A_page_checked_against_an_older_version_is_flagged_for_a_recheck_but_does_not_fail()
    {
        using var repository = Samples.Baseline();
        repository.Replace("docs/reference/tools/acme-lint.md", "verified-against: { acme-lint: \"1.0.0\" }", "verified-against: { acme-lint: \"0.9.0\" }");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Expect.Passes(result);
        Assert.Contains("warning [tool-page-version]", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-004")]
    public async Task A_Tier_C_library_needs_an_entry_in_the_catalog_and_the_heading_match_ignores_case()
    {
        using var repository = Samples.Baseline();
        repository.Replace("docs/reference/tools/dependency-catalog.md", "## Acme.Lib", "## acme.lib");

        Expect.Passes(await CliRunner.RunAsync(repository, "inventory"));

        repository.Replace("docs/reference/tools/dependency-catalog.md", "## acme.lib", "## Something else");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Expect.FailsOnlyWith(result, "tool-page-missing");
        Assert.Contains("'## Acme.Lib' section", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-004")]
    public async Task A_Tier_C_library_fails_when_there_is_no_catalog_at_all()
    {
        using var repository = Samples.Baseline().Remove("docs/reference/tools/dependency-catalog.md");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Expect.FailsOnlyWith(result, "tool-page-missing");
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-004")]
    public async Task A_page_for_a_Tier_C_library_declares_the_tier_A_or_B_it_follows()
    {
        using var repository = Samples.Baseline().Add("docs/reference/tools/acme-lib.md", Samples.ToolPage.Replace("acme-lint", "acme-lib", StringComparison.Ordinal).Replace("1.0.0", "1.2.3", StringComparison.Ordinal));

        Expect.Passes(await CliRunner.RunAsync(repository, "inventory"));

        repository.Replace("docs/reference/tools/acme-lib.md", "tier: B", "tier: C");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Assert.Contains("[tool-page-tier]", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-004")]
    public async Task A_page_that_is_owed_by_a_later_work_package_is_a_note_and_not_a_failure()
    {
        using var repository = Samples.Baseline().Remove("docs/reference/tools/acme-lint.md");
        repository.Replace(Policy, "discovery:\n", OwedGroup("WP1.13", "acme-lint") + "discovery:\n");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Expect.Passes(result);
        var note = Assert.Single(Expect.Notes(result));
        Assert.Contains("[tool-page-owed]", note, StringComparison.Ordinal);
        Assert.Contains("owed by WP1.13", note, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-004")]
    public async Task An_owed_page_becomes_an_error_when_the_owing_work_package_is_completed()
    {
        using var repository = Samples.Baseline().Remove("docs/reference/tools/acme-lint.md");
        repository.Replace(Policy, "discovery:\n", OwedGroup("WP1.13", "acme-lint") + "discovery:\n");
        repository.Replace("docs/project/status.yaml", "id: WP1.13\n        state: planned", "id: WP1.13\n        state: completed");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Expect.FailsOnlyWith(result, "tool-page-missing");
        Assert.Contains("owed by WP1.13, which is completed", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-004")]
    public async Task A_page_that_exists_must_leave_the_owed_list()
    {
        using var repository = Samples.Baseline();
        repository.Replace(Policy, "discovery:\n", OwedGroup("WP1.13", "acme-lint") + "discovery:\n");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Expect.FailsOnlyWith(result, "tool-page-owed-stale");
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-004")]
    public async Task An_owed_entry_for_a_tool_that_is_not_in_the_inventory_or_needs_no_page_fails()
    {
        using var repository = Samples.Baseline();
        repository.Replace(Policy, "discovery:\n", OwedGroup("WP1.13", "no-such-tool", "acme-platform") + "discovery:\n");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Expect.FailsOnlyWith(result, "tool-page-owed-stale");
        Assert.Equal(2, Expect.Errors(result).Count);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-004")]
    public async Task An_owed_entry_that_names_a_work_package_the_status_file_does_not_know_fails()
    {
        using var repository = Samples.Baseline().Remove("docs/reference/tools/acme-lint.md");
        repository.Replace(Policy, "discovery:\n", OwedGroup("WP9.9", "acme-lint") + "discovery:\n");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Expect.FailsOnlyWith(result, "tool-page-owed-stale");
        Assert.Contains("WP9.9", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-004")]
    public async Task An_owed_list_needs_the_status_file_so_a_missing_one_is_reported()
    {
        using var repository = Samples.Baseline().Remove("docs/reference/tools/acme-lint.md").Remove("docs/project/status.yaml");
        repository.Replace(Policy, "discovery:\n", OwedGroup("WP1.13", "acme-lint") + "discovery:\n");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Assert.Contains("[status-unreadable]", result.Output, StringComparison.Ordinal);
        Assert.Equal(1, result.ExitCode);
    }

    private static string OwedGroup(string due, params string[] tools) =>
        $"  owed:\n    - due: {due}\n      reason: Written later.\n      tools: [{string.Join(", ", tools)}]\n";
}
