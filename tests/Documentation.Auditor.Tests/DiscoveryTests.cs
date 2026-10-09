using Documentation.Auditor.Discovery;
using Documentation.Auditor.Tests.Support;
using Xunit;

namespace Documentation.Auditor.Tests;

/// <summary>REQ-DOC-003: the auditor fails when a tool the repository uses has no inventory entry, or the entry disagrees with the files.</summary>
public sealed class DiscoveryTests
{
    private static readonly string[] ExpectedImages =
    [
        "${CACHE_IMAGE:-valkey}",
        "mcr.example.test/db/server",
        "mcr.example.test/runtime",
        "mcr.example.test/sdk",
        "mcr.example.test/sql/server",
        "mcr.example.test/tests/runner",
        "mcr.example.test/tools/lint",
    ];

    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public async Task A_package_that_no_entry_detects_fails_with_the_rule_to_add()
    {
        using var repository = Samples.Baseline().Replace("Directory.Packages.props", "</ItemGroup>", "  <PackageVersion Include=\"Mystery.Package\" Version=\"9.9.9\" />\n  </ItemGroup>");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Expect.FailsOnlyWith(result, "tool-undocumented");
        Assert.Contains("'Mystery.Package'", result.Output, StringComparison.Ordinal);
        Assert.Contains("nuget:Mystery.Package", result.Output, StringComparison.Ordinal);
        Assert.Contains("Directory.Packages.props:", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public async Task A_documented_package_in_a_project_file_passes()
    {
        using var repository = Samples.Baseline().Add("src/App/App.csproj", """
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Acme.Lib" />
  </ItemGroup>
</Project>
""");

        Expect.Passes(await CliRunner.RunAsync(repository, "inventory"));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public async Task A_package_in_a_project_file_that_no_entry_detects_fails()
    {
        using var repository = Samples.Baseline().Add("src/App/App.csproj", """
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Other.Thing" Version="1.0.0" />
  </ItemGroup>
</Project>
""");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Expect.FailsOnlyWith(result, "tool-undocumented");
        Assert.Contains("src/App/App.csproj:", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public async Task A_local_tool_that_no_entry_detects_fails_and_a_detected_one_passes()
    {
        using var repository = Samples.Baseline().Add(".config/dotnet-tools.json", """{ "version": 1, "tools": { "acme-cli": { "version": "1.0.0", "commands": ["acme"] } } }""");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Expect.FailsOnlyWith(result, "tool-undocumented");
        Assert.Contains("dotnet-tool:acme-cli", result.Output, StringComparison.Ordinal);

        repository.Replace("docs/reference/tools/inventory.yaml", "detect: [\"image:registry.example.test/acme/lint\"]", "detect: [\"image:registry.example.test/acme/lint\", \"dotnet-tool:acme-cli\"]");

        Expect.Passes(await CliRunner.RunAsync(repository, "inventory"));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public async Task An_action_that_no_entry_detects_fails_and_a_wildcard_rule_covers_the_owner()
    {
        using var repository = Samples.Baseline().Add(".github/workflows/ci.yml", """
name: ci
on: pull_request
permissions: {}
jobs:
  build:
    runs-on: ubuntu-24.04
    steps:
      - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1
      - uses: ./.github/actions/local
      - uses: thirdparty/deploy@3d3c42e5aac5ba805825da76410c181273ba90b1
""");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Assert.Equal(1, result.ExitCode);
        var errors = Expect.Errors(result);
        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, line => line.Contains("'actions/checkout'", StringComparison.Ordinal) && line.Contains(".github/workflows/ci.yml:8", StringComparison.Ordinal));
        Assert.Contains(errors, line => line.Contains("'thirdparty/deploy'", StringComparison.Ordinal));
        Assert.DoesNotContain(errors, line => line.Contains("local", StringComparison.Ordinal));

        repository.Replace("docs/reference/tools/inventory.yaml", "detect: [\"image:registry.example.test/acme/lint\"]", "detect: [\"image:registry.example.test/acme/lint\", \"action:actions/*\", \"action:thirdparty/deploy\"]");

        Expect.Passes(await CliRunner.RunAsync(repository, "inventory"));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public async Task An_image_in_a_compose_file_a_dockerfile_a_workflow_container_and_a_docker_action_is_discovered()
    {
        using var repository = Samples.Baseline()
            .Add("compose.yaml", "services:\n  db:\n    image: \"mcr.example.test/db/server:2022-latest@sha256:abc\" # the database\n  cache:\n    image: ${CACHE_IMAGE:-valkey}\n")
            .Add("Dockerfile", "FROM --platform=$BUILDPLATFORM mcr.example.test/sdk:10.0 AS build\nFROM build AS publish\nFROM scratch\nFROM mcr.example.test/runtime:10.0\n")
            .Add(".github/workflows/it.yml", "name: it\non: push\npermissions: {}\njobs:\n  test:\n    runs-on: ubuntu-24.04\n    container: mcr.example.test/tests/runner:1.0\n    services:\n      sql:\n        image: mcr.example.test/sql/server:2022\n    steps:\n      - uses: docker://mcr.example.test/tools/lint@sha256:abc\n");

        var result = await CliRunner.RunAsync(repository, "inventory");

        var images = Expect.Errors(result).Select(line => line.Split('\'')[1]).ToList();
        Assert.Equal(ExpectedImages, images.Order(StringComparer.Ordinal));
        Assert.DoesNotContain("'scratch'", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("'build'", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public async Task A_compose_file_that_shares_a_service_with_an_anchor_is_still_read()
    {
        using var repository = Samples.Baseline().Add("compose.yaml", "services:\n  one: &base\n    image: registry.example.test/acme/lint:v1.0.0\n  two:\n    <<: *base\n");

        Expect.Passes(await CliRunner.RunAsync(repository, "inventory"));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public async Task A_tool_in_use_whose_entry_says_planned_fails_so_the_inventory_cannot_understate_use()
    {
        using var repository = Samples.Baseline().Replace(
            "docs/reference/tools/inventory.yaml",
            "    lifecycle: adopt\n    usage: in-use\n    introduced: phase-1\n    purpose: A library.",
            "    lifecycle: adopt\n    usage: planned\n    introduced: phase-1\n    purpose: A library.");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Expect.FailsOnlyWith(result, "tool-usage-state");
        Assert.Contains("acme-lib", result.Output, StringComparison.Ordinal);
        Assert.Contains("'planned'", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public async Task A_held_tool_that_the_repository_uses_fails_with_the_hold_message()
    {
        using var repository = Samples.Baseline()
            .Replace("docs/reference/tools/inventory.yaml", "    adrs: []\n    notes: Held", "    adrs: []\n    detect: [\"nuget:Old.Thing\"]\n    notes: Held")
            .Replace("Directory.Packages.props", "</ItemGroup>", "  <PackageVersion Include=\"Old.Thing\" Version=\"3.1.0\" />\n  </ItemGroup>");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Expect.FailsOnlyWith(result, "tool-usage-state");
        Assert.Contains("on hold", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Requirement", "REQ-DOC-003")]
    [InlineData("1.2.4", true)]
    [InlineData("1.2.3", false)]
    [InlineData("v1.2.3", false)]
    public async Task A_pin_that_differs_from_the_recorded_version_fails_and_a_v_prefix_does_not(string pinned, bool drifts)
    {
        using var repository = Samples.Baseline().Replace("Directory.Packages.props", "Version=\"1.2.3\"", $"Version=\"{pinned}\"");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Assert.Equal(drifts, result.Output.Contains("[tool-version-drift]", StringComparison.Ordinal));
        Assert.Equal(drifts ? 1 : 0, result.ExitCode);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public async Task An_image_tag_is_compared_with_the_recorded_version_after_stripping_a_prefix()
    {
        using var repository = Samples.Baseline().Replace("tools/lint/compose.yaml", ":v1.0.0@", ":v1.1.0@");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Expect.FailsOnlyWith(result, "tool-version-drift");
        Assert.Contains("tools/lint/compose.yaml:3", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public async Task An_entry_that_is_in_use_with_detect_rules_that_match_nothing_fails()
    {
        using var repository = Samples.Baseline().Remove("tools/lint/compose.yaml");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Expect.FailsOnlyWith(result, "tool-not-found");
        Assert.Contains("acme-lint", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public async Task A_file_rule_finds_a_tool_by_the_file_it_leaves_and_checks_the_usage_state()
    {
        using var repository = Samples.Baseline().Replace(
            "docs/reference/tools/inventory.yaml",
            "    sources:\n      - https://example.test/acme-platform\n    adrs: []\n",
            "    sources:\n      - https://example.test/acme-platform\n    adrs: []\n    detect: [\"file:platform/**/*.yaml\"]\n");

        // The entry says planned and no file matches yet, so nothing is wrong.
        Expect.Passes(await CliRunner.RunAsync(repository, "inventory"));

        repository.Add("platform/base/app.yaml", "kind: Deployment\n");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Expect.FailsOnlyWith(result, "tool-usage-state");
        Assert.Contains("platform/base/app.yaml", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public async Task Two_entries_that_detect_the_same_tool_fail_as_ambiguous()
    {
        using var repository = Samples.Baseline().Replace("docs/reference/tools/inventory.yaml", "    adrs: []\n    detect: [\"nuget:Acme.Lib\"]\n", "    adrs: []\n    detect: [\"nuget:Acme.*\"]\n")
            .Replace("docs/reference/tools/inventory.yaml", "    adrs: []\n    notes: Held", "    adrs: []\n    detect: [\"nuget:Acme.Lib\"]\n    notes: Held");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Assert.Contains("[tool-ambiguous]", result.Output, StringComparison.Ordinal);
        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public async Task A_file_the_auditor_cannot_read_yet_fails_so_its_tools_cannot_go_undiscovered()
    {
        using var repository = Samples.Baseline().Add("infra/main.tf", "terraform {}\n");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Expect.FailsOnlyWith(result, "discovery-unsupported-source");
        Assert.Contains("infra/main.tf", result.Output, StringComparison.Ordinal);
        Assert.Contains("WP3.1", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public async Task A_project_file_with_a_dtd_is_refused_and_reported_instead_of_skipped()
    {
        using var repository = Samples.Baseline().Add("src/App/App.csproj", """
<!DOCTYPE Project [<!ENTITY x "y">]>
<Project Sdk="Microsoft.NET.Sdk" />
""");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Expect.FailsOnlyWith(result, "discovery-unreadable");
        Assert.Contains("src/App/App.csproj", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public async Task A_workflow_that_is_not_valid_yaml_is_reported_instead_of_skipped()
    {
        using var repository = Samples.Baseline().Add(".github/workflows/broken.yml", "jobs: [unclosed\n");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Expect.FailsOnlyWith(result, "discovery-unreadable");
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public async Task Build_output_that_the_policy_excludes_is_not_scanned()
    {
        using var repository = Samples.Baseline().Replace("governance/policies/documentation-policy.yaml", "discovery:\n", "discovery:\n  exclude-paths: ['generated/**']\n");
        repository.Add("generated/Other.csproj", """<Project><ItemGroup><PackageReference Include="Mystery.Package" Version="1.0.0" /></ItemGroup></Project>""");

        Expect.Passes(await CliRunner.RunAsync(repository, "inventory"));
    }
}

public sealed class ScannerTests
{
    [Theory]
    [Trait("Requirement", "REQ-DOC-003")]
    [InlineData("ghcr.io/gitleaks/gitleaks:v8.30.1@sha256:abc", "ghcr.io/gitleaks/gitleaks", "v8.30.1")]
    [InlineData("ubuntu:24.04", "ubuntu", "24.04")]
    [InlineData("ubuntu", "ubuntu", null)]
    [InlineData("localhost:5000/team/tool", "localhost:5000/team/tool", null)]
    [InlineData("localhost:5000/team/tool:2.0", "localhost:5000/team/tool", "2.0")]
    [InlineData("name@sha256:abc", "name", null)]
    public void An_image_reference_is_split_into_a_name_and_a_tag(string reference, string name, string? tag)
    {
        var tool = Scanners.ReadImage(reference, "file", null);

        Assert.Equal(DiscoveryKinds.Image, tool.Kind);
        Assert.Equal(name, tool.Identifier);
        Assert.Equal(tag, tool.Version);
    }

    [Theory]
    [Trait("Requirement", "REQ-DOC-003")]
    [InlineData("v2.81.0", "2.81.0")]
    [InlineData("2.81.0", "2.81.0")]
    [InlineData("lychee-v0.24.2", "0.24.2")]
    [InlineData("10.0.401", "10.0.401")]
    [InlineData("n/a", null)]
    [InlineData("unverified", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void A_version_is_compared_by_its_numbers_only(string? version, string? expected)
    {
        Assert.Equal(expected, Scanners.NormalizeVersion(version));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public void An_action_gets_its_version_from_the_comment_beside_the_pin()
    {
        var tools = Scanners.ScanWorkflow(".github/workflows/ci.yml", "jobs:\n  a:\n    steps:\n      - uses: actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68 # v6.0.0\n      - uses: owner/repo/sub@v2\n").ToList();

        Assert.Equal(["actions/setup-dotnet", "owner/repo/sub"], tools.Select(tool => tool.Identifier));
        Assert.Equal(["v6.0.0", null], tools.Select(tool => tool.Version));
    }
}
