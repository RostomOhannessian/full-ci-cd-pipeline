using Governance.Auditor.Common;
using Governance.Auditor.Status;
using Governance.Auditor.Tests.Support;
using Governance.Auditor.Usage;
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
    [Trait("Requirement", "REQ-AI-005")]
    public async Task The_ai_usage_ledger_is_valid_and_its_report_is_exactly_what_it_renders()
    {
        var validate = await CliRunner.RunOnRealRepositoryAsync("usage", "validate");
        var render = await CliRunner.RunOnRealRepositoryAsync("usage", "render", "--check");

        Assert.True(validate.ExitCode == 0, validate.Output + validate.Error);
        Assert.True(render.ExitCode == 0, "Run 'governance usage render' and commit the page.\n" + render.Output + render.Error);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-005")]
    public void Every_completed_work_package_of_phase_1_and_the_phase_0_total_have_usage_in_the_ledger()
    {
        List<Finding> findings = [];
        var status = StatusFile.Load(RealRepository.Files, findings)!;
        var ledger = UsageFile.Load(RealRepository.Files, findings)!;

        Assert.Empty(findings);

        foreach (var workPackage in status.Phases.Single(phase => phase.Id.Value == "1").WorkPackages.Where(workPackage => workPackage.State == WorkState.Completed))
        {
            Assert.True(ledger.DeliveryOf(workPackage.Id).Any(), $"{workPackage.Id} is completed and has no usage in docs/project/ai-usage.yaml.");
        }

        Assert.Contains(ledger.Records, record => record.Phase.Value == "0" && record.Activity == UsageVocabulary.Delivery);
        Assert.Contains(ledger.Waivers, waiver => waiver.Phase is { Value: "0" });
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-005")]
    public async Task The_ai_usage_ledger_holds_opaque_ids_and_numbers_and_no_personal_data()
    {
        var text = await File.ReadAllTextAsync(Path.Combine(RealRepository.Root, "docs", "project", "ai-usage.yaml"), TestContext.Current.CancellationToken);

        Assert.DoesNotContain('@', text);
        Assert.DoesNotContain("RostomOhannessian", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(@"[A-Za-z]:\\", text);
        Assert.DoesNotMatch(@"(?i)/(users|home)/", text);
        Assert.DoesNotMatch(@"(?i)\b(?:prompt|branch|email|token|password)\b\s*:", text);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-005")]
    public async Task The_estimate_of_this_repository_prints_its_method_its_range_and_its_caveats()
    {
        var result = await CliRunner.RunOnRealRepositoryAsync("usage", "estimate");

        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        Assert.Contains("  method:", result.Output, StringComparison.Ordinal);
        Assert.Contains("  by size (observed AIU", result.Output, StringComparison.Ordinal);
        Assert.Contains("  caveats:", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("[usage-", result.Output, StringComparison.Ordinal);
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
