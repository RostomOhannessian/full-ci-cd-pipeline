using Documentation.Auditor.Discovery;
using Documentation.Auditor.Policy;
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

public sealed class DevEnvironmentDiscoveryTests
{
    // The image tag and the locked feature version are 1.2.3, the version that the sample entry acme-lib records, so one entry can detect them all.
    private const string Lock = """
{
  "features": {
    "ghcr.io/devcontainers/features/docker-in-docker:2": {
      "version": "1.2.3",
      "resolved": "ghcr.io/devcontainers/features/docker-in-docker@sha256:25b9f05705ffba7dbe503230ac76081419306f8c8bc88e0ce78c4ecd99a0c78c",
      "integrity": "sha256:25b9f05705ffba7dbe503230ac76081419306f8c8bc88e0ce78c4ecd99a0c78c"
    }
  }
}
""";

    private const string Config = """
{
  // The base image and one feature, as a Dev Container is written.
  "name": "Sample",
  "image": "mcr.example.test/devcontainers/base:1.2.3@sha256:0000000000000000000000000000000000000000000000000000000000000000",
  "features": {
    "ghcr.io/devcontainers/features/docker-in-docker:2": { "moby": false },
  },
}
""";

    private static TestRepository DevContainerRepository() =>
        Samples.Baseline()
            .Add(".devcontainer/devcontainer.json", Config)
            .Add(".devcontainer/devcontainer-lock.json", Lock);

    private static TestRepository Documented(TestRepository repository) =>
        repository.Replace(
            "docs/reference/tools/inventory.yaml",
            "    detect: [\"nuget:Acme.Lib\"]",
            "    detect: [\"nuget:Acme.Lib\", \"image:mcr.example.test/devcontainers/*\", \"feature:ghcr.io/devcontainers/features/*\"]");

    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public async Task The_image_and_the_feature_of_a_dev_container_are_found_and_a_comment_and_a_trailing_comma_are_allowed()
    {
        using var repository = DevContainerRepository();

        var result = await CliRunner.RunAsync(repository, "inventory");

        var errors = Expect.Errors(result);
        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, line => line.Contains("[tool-undocumented]", StringComparison.Ordinal) && line.Contains("'mcr.example.test/devcontainers/base'", StringComparison.Ordinal) && line.Contains("image:", StringComparison.Ordinal));
        Assert.Contains(errors, line => line.Contains("'ghcr.io/devcontainers/features/docker-in-docker'", StringComparison.Ordinal) && line.Contains("feature:ghcr.io/devcontainers/features/docker-in-docker", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public async Task A_documented_dev_container_passes()
    {
        using var repository = Documented(DevContainerRepository());

        Expect.Passes(await CliRunner.RunAsync(repository, "inventory"));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public async Task A_feature_is_compared_with_the_version_in_the_lock_file_and_a_pin_that_drifts_fails()
    {
        using var repository = Documented(DevContainerRepository());
        repository.Replace(".devcontainer/devcontainer-lock.json", "\"version\": \"1.2.3\"", "\"version\": \"2.18.0\"");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Expect.FailsOnlyWith(result, "tool-version-drift");
        Assert.Contains("pins version 2.18.0", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-DX-001")]
    public async Task A_dev_container_with_features_and_no_lock_file_fails()
    {
        using var repository = Documented(DevContainerRepository()).Remove(".devcontainer/devcontainer-lock.json");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Expect.FailsOnlyWith(result, "devcontainer-lock-missing");
        Assert.Contains(".devcontainer/devcontainer-lock.json", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-DX-001")]
    public async Task A_feature_that_the_lock_file_lacks_fails()
    {
        using var repository = Documented(DevContainerRepository());
        repository.Replace(".devcontainer/devcontainer.json", "\"ghcr.io/devcontainers/features/docker-in-docker:2\": { \"moby\": false },", "\"ghcr.io/devcontainers/features/docker-in-docker:2\": { \"moby\": false },\n    \"ghcr.io/devcontainers/features/github-cli:1\": {},");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Expect.FailsOnlyWith(result, "devcontainer-lock-missing");
        Assert.Contains("github-cli:1", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-DX-001")]
    public async Task A_lock_entry_for_a_feature_that_is_gone_fails_as_stale()
    {
        using var repository = Documented(DevContainerRepository());
        repository.Replace(".devcontainer/devcontainer-lock.json", "\"features\": {", "\"features\": {\n    \"ghcr.io/devcontainers/features/node:1\": { \"version\": \"1.0.0\", \"resolved\": \"x@sha256:abc\", \"integrity\": \"sha256:abc\" },");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Expect.FailsOnlyWith(result, "devcontainer-lock-stale");
        Assert.Contains("features/node:1", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-DX-001")]
    public async Task A_lock_entry_without_a_digest_fails_as_unpinned()
    {
        using var repository = Documented(DevContainerRepository());
        repository.Replace(".devcontainer/devcontainer-lock.json", "ghcr.io/devcontainers/features/docker-in-docker@sha256:25b9f05705ffba7dbe503230ac76081419306f8c8bc88e0ce78c4ecd99a0c78c\"", "ghcr.io/devcontainers/features/docker-in-docker:2.17.0\"");

        Expect.FailsOnlyWith(await CliRunner.RunAsync(repository, "inventory"), "devcontainer-lock-unpinned");
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public async Task A_feature_in_a_folder_of_the_repository_needs_no_lock_and_no_entry()
    {
        using var repository = Samples.Baseline().Add(".devcontainer/devcontainer.json", """{ "name": "Sample", "features": { "./local-feature": {} } }""");

        Expect.Passes(await CliRunner.RunAsync(repository, "inventory"));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public async Task A_dev_container_configuration_that_is_not_valid_json_is_reported_and_not_skipped()
    {
        using var repository = Samples.Baseline().Add(".devcontainer/devcontainer.json", """{ "image": """);

        Expect.FailsOnlyWith(await CliRunner.RunAsync(repository, "inventory"), "discovery-unreadable");
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public async Task The_dockerfile_of_a_dev_container_is_read_like_any_other()
    {
        using var repository = Samples.Baseline().Add(".devcontainer/Dockerfile", "FROM mcr.example.test/devcontainers/base:1.2.3@sha256:abc\nENV A=1\n");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Expect.FailsOnlyWith(result, "tool-undocumented");
        Assert.Contains("'mcr.example.test/devcontainers/base'", result.Output, StringComparison.Ordinal);
    }
    private const string Versions = """
schema-version: 1
tools:
  - id: pester
    kind: powershell-module
    name: Pester
    version: 5.9.0
    url: https://www.powershellgallery.com/api/v2/package/Pester/5.9.0
    sha256: 5a0fd80b361600bf4bbd4c307c1fd01b17f11668bab19e657add41b00ad22ab9
""";

    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public async Task A_module_in_the_tool_manifest_that_no_entry_detects_fails_with_the_rule_to_add()
    {
        using var repository = Samples.Baseline().Add("tools/versions.yaml", Versions);

        var result = await CliRunner.RunAsync(repository, "inventory");

        Expect.FailsOnlyWith(result, "tool-undocumented");
        Assert.Contains("'Pester'", result.Output, StringComparison.Ordinal);
        Assert.Contains("psmodule:Pester", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public async Task A_documented_module_passes_and_its_pin_is_compared_with_the_recorded_version()
    {
        using var repository = Samples.Baseline().Add("tools/versions.yaml", Versions);
        repository.Replace("docs/reference/tools/inventory.yaml", "    detect: [\"nuget:Acme.Lib\"]", "    detect: [\"nuget:Acme.Lib\", \"psmodule:Pester\"]");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Expect.FailsOnlyWith(result, "tool-version-drift");

        repository.Replace("tools/versions.yaml", "version: 5.9.0\n    url: https://www.powershellgallery.com/api/v2/package/Pester/5.9.0", "version: 1.2.3\n    url: https://www.powershellgallery.com/api/v2/package/Pester/1.2.3");

        Expect.Passes(await CliRunner.RunAsync(repository, "inventory"));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public async Task A_tool_of_a_kind_the_auditor_cannot_read_is_reported_so_it_cannot_hide_in_the_manifest()
    {
        using var repository = Samples.Baseline().Add("tools/versions.yaml", Versions.Replace("powershell-module", "archive"));

        var result = await CliRunner.RunAsync(repository, "inventory");

        Expect.FailsOnlyWith(result, "discovery-unreadable");
        Assert.Contains("kind 'archive'", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public async Task A_manifest_with_an_anchor_is_refused_and_reported()
    {
        using var repository = Samples.Baseline().Add("tools/versions.yaml", Versions.Replace("id: pester", "id: &x pester"));

        Expect.FailsOnlyWith(await CliRunner.RunAsync(repository, "inventory"), "discovery-unreadable");
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public async Task The_installed_tools_folder_is_not_scanned()
    {
        using var repository = Samples.Baseline().Replace("governance/policies/documentation-policy.yaml", "discovery:\n", "discovery:\n  exclude-paths: ['.dev/**']\n");
        repository.Add(".dev/modules/Pester/5.9.0/Thing.csproj", """<Project><ItemGroup><PackageReference Include="Mystery.Package" Version="1.0.0" /></ItemGroup></Project>""");

        Expect.Passes(await CliRunner.RunAsync(repository, "inventory"));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public void The_real_policy_no_longer_lists_the_dev_container_or_the_manifest_as_unreadable()
    {
        var policy = DocumentationPolicy.Load(RealRepository.Files);

        Assert.DoesNotContain(policy.Discovery.UnsupportedSources, source => source.Pattern.StartsWith(".devcontainer", StringComparison.Ordinal));
        Assert.DoesNotContain(policy.Discovery.UnsupportedSources, source => source.Pattern.Contains("versions.yaml", StringComparison.Ordinal));
        Assert.Contains(".dev/**", policy.Discovery.ExcludePaths);
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
