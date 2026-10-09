using Governance.Auditor.Tests.Support;
using Xunit;

namespace Governance.Auditor.Tests;

/// <summary>
/// Runs the governance commands over this repository, as the <c>governance</c> workflow does. A change that breaks the status files,
/// leaves a generated page out of date, drops the proof of a requirement, or adds a risky workflow fails here before it fails in CI.
/// </summary>
public sealed class RepositorySelfCheckTests
{
    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public async Task The_status_file_is_valid_and_its_evidence_exists()
    {
        var result = await CliRunner.RunOnRealRepositoryAsync("status", "validate");

        Assert.True(result.ExitCode == 0, result.Output + result.Error);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public async Task The_status_page_is_exactly_what_the_status_file_renders()
    {
        var result = await CliRunner.RunOnRealRepositoryAsync("status", "render", "--check");

        Assert.True(result.ExitCode == 0, "Run 'governance status render' and commit the page.\n" + result.Output + result.Error);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-003")]
    public async Task The_status_page_names_the_branch_the_next_action_and_how_to_resume_on_another_machine()
    {
        var page = await File.ReadAllTextAsync(Path.Combine(RealRepository.Root, "docs", "project", "STATUS.md"), TestContext.Current.CancellationToken);

        Assert.Contains("## Resume here", page, StringComparison.Ordinal);
        Assert.Contains("| Branch | `", page, StringComparison.Ordinal);
        Assert.Contains("| Next action |", page, StringComparison.Ordinal);
        Assert.Contains("### Resume on another machine", page, StringComparison.Ordinal);
        Assert.Contains("git fetch origin", page, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-QUA-003")]
    public async Task Every_in_scope_requirement_has_proof_and_the_traceability_report_is_current()
    {
        var result = await CliRunner.RunOnRealRepositoryAsync("trace", "--check");

        Assert.True(result.ExitCode == 0, "Run 'governance trace', read the findings, and commit the report.\n" + result.Output + result.Error);
    }

    [Fact]
    [Trait("Requirement", "REQ-SEC-001")]
    public async Task The_repository_holds_nothing_that_looks_like_a_stored_secret()
    {
        var result = await CliRunner.RunOnRealRepositoryAsync("security", "--rule", "secret-patterns");

        Assert.True(result.ExitCode == 0, result.Output + result.Error);
    }

    [Fact]
    [Trait("Requirement", "REQ-SEC-007")]
    public async Task Every_workflow_passes_the_permission_pinning_secret_and_limit_rules()
    {
        var result = await CliRunner.RunOnRealRepositoryAsync(
            "security",
            "--rule", "workflow-parse",
            "--rule", "workflow-permissions",
            "--rule", "action-pinning",
            "--rule", "workflow-secrets",
            "--rule", "checkout-credentials",
            "--rule", "pull-request-target",
            "--rule", "workflow-limits");

        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        Assert.DoesNotContain("warning [", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-SUP-003")]
    public async Task The_real_lock_files_pass_the_license_policy_rule()
    {
        var result = await CliRunner.RunOnRealRepositoryAsync("security", "--rule", "license-policy");

        Assert.True(result.ExitCode == 0, result.Output + result.Error);
    }

    [Fact]
    [Trait("Requirement", "REQ-CI-002")]
    public async Task The_blast_radius_map_loads_and_classifies_the_paths_of_this_work_package()
    {
        var result = await CliRunner.RunOnRealRepositoryAsync(
            "blast-radius",
            "--path", ".github/workflows/governance.yml",
            "--path", "tools/Governance.Auditor/Program.cs",
            "--path", "tests/Governance.Auditor.Tests/YamlDocumentTests.cs",
            "--path", "governance/policies/security-policy.yaml",
            "--path", "docs/project/STATUS.md",
            "--path", "docs/testing/evidence.yaml",
            "--path", "docs/adr/0021-governance-auditor.md",
            "--path", "CHANGELOG.md");

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("blast-radius-unclassified", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-CI-004")]
    public async Task The_all_files_option_treats_every_file_as_changed_so_selection_fails_closed()
    {
        using var repository = new TestRepository().CopyFromRepository("governance/policies/blast-radius-map.yaml");
        var git = new FakeProcessRunner().On(request => request.Arguments[0] == "ls-files", "src/Catalog.Domain/A.cs\0docs/page.md\0unknown.xyz\0");

        var result = await CliRunner.RunAsync(repository, git, "blast-radius", "--all-files", "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("\"changedPaths\":3", result.Output, StringComparison.Ordinal);
        Assert.Contains("\"unclassifiedCount\":1", result.Output, StringComparison.Ordinal);
        Assert.Equal(["ls-files", "-z"], Assert.Single(git.Requests).Arguments);
    }
}
