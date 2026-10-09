using Governance.Auditor.Common;
using Governance.Auditor.Status;
using Governance.Auditor.Tests.Support;
using Xunit;

namespace Governance.Auditor.Tests;

public sealed class StatusValidatorTests
{
    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public void A_consistent_status_file_has_no_findings()
    {
        Assert.Empty(Validate(StatusSamples.Valid));
    }

    [Theory]
    [Trait("Requirement", "REQ-GOV-002")]
    [InlineData("updated", "2026-02-30", "not a real calendar date")]
    [InlineData("wp11Id", "WP1.2", "Work package WP1.2 appears 2 times")]
    [InlineData("wp21Id", "WP1.9", "its ID says it belongs to another phase")]
    [InlineData("wp11Issue", "4", "Issue 4 is linked to WP1.1 and WP1.2")]
    [InlineData("wp11DependsOn", "[\"WP9.9\"]", "depends on WP9.9, which is not in the status file")]
    [InlineData("wp12DependsOn", "[\"WP1.2\"]", "depends on itself")]
    public void An_inconsistent_identifier_or_reference_is_reported(string placeholder, string value, string expectedMessage)
    {
        var findings = Validate(StatusSamples.Build((placeholder, value)));

        Assert.Contains(findings, finding => finding.Message.Contains(expectedMessage, StringComparison.Ordinal));
    }

    [Theory]
    [Trait("Requirement", "REQ-GOV-007")]
    [InlineData("wp11Branch", "feature/first", "WP1.1 names the branch 'feature/first', which is not wp/1.1-<slug>")]
    [InlineData("wp11Branch", "wp/1.2-first", "WP1.1 names the branch 'wp/1.2-first', which is not wp/1.1-<slug>")]
    [InlineData("wp11Branch", "wp/1.1-Upper", "WP1.1 names the branch 'wp/1.1-Upper', which is not wp/1.1-<slug>")]
    [InlineData("phase1Branch", "release/1", "Phase 1 names the branch 'release/1', which is not phase/1-<slug>")]
    [InlineData("phase1Branch", "phase/2-wrong-number", "Phase 1 names the branch 'phase/2-wrong-number', which is not phase/1-<slug>")]
    public void A_branch_name_that_breaks_the_branch_protocol_is_reported(string placeholder, string value, string expectedMessage)
    {
        var findings = Validate(StatusSamples.Build((placeholder, value)));

        Assert.Contains(findings, finding => finding.Message.Contains(expectedMessage, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-007")]
    public void A_docs_only_phase_may_do_its_work_on_the_phase_branch_itself()
    {
        Assert.Empty(Validate(StatusSamples.Build(("wp11Branch", "phase/1-sample"))));
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-004")]
    public void A_completed_work_package_without_evidence_is_reported()
    {
        var findings = Validate(StatusSamples.Build(("wp11Evidence", "[]")));

        Assert.Contains(findings, finding => finding.Message.Contains("WP1.1 is completed but lists no evidence", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-004")]
    public void A_work_package_cannot_start_before_its_dependencies_are_complete()
    {
        var findings = Validate(StatusSamples.Build(("wp11State", "in_progress")));

        Assert.Contains(findings, finding => finding.Message.Contains("WP1.2 is in progress, but its dependency WP1.1 is in progress", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-004")]
    public void A_blocked_work_package_must_say_what_blocks_it()
    {
        var findings = Validate(StatusSamples.Build(("wp12State", "blocked")));

        Assert.Contains(findings, finding => finding.Message.Contains("WP1.2 is blocked but lists no blocker", StringComparison.Ordinal));
        Assert.Empty(Validate(StatusSamples.Build(("wp12State", "blocked"), ("wp12Blockers", "[\"Waiting for a decision\"]"))));
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-004")]
    public void An_in_progress_work_package_needs_a_branch_and_a_completed_one_cannot_have_blockers()
    {
        var noBranch = Validate(StatusSamples.Build(("wp12Branch", "null")));
        var blockedAndDone = Validate(StatusSamples.Build(
            ("wp12State", "completed"),
            ("wp12Blockers", "[\"Still waiting\"]"),
            ("phase1State", "completed"),
            ("currentWorkPackage", "null"),
            ("currentBranch", "phase/1-sample")));

        Assert.Contains(noBranch, finding => finding.Message.Contains("WP1.2 is in progress but names no branch", StringComparison.Ordinal));
        Assert.Contains(blockedAndDone, finding => finding.Message.Contains("WP1.2 is completed but still lists blockers", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public void A_dependency_cycle_is_reported_once()
    {
        var findings = Validate(StatusSamples.Build(
                ("wp11State", "planned"),
                ("wp11DependsOn", "[\"WP1.2\"]"),
                ("wp12State", "planned"),
                ("currentWorkPackage", "null"),
                ("currentBranch", "phase/1-sample")))
            .Where(finding => finding.Message.Contains("cycle", StringComparison.Ordinal))
            .ToList();

        Assert.Single(findings);
        Assert.Contains("WP1.1 -> WP1.2 -> WP1.1", findings[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public void A_completed_phase_needs_every_work_package_completed()
    {
        var findings = Validate(StatusSamples.Build(("phase1State", "completed")));

        Assert.Contains(findings, finding => finding.Message.Contains("Phase 1 is completed, but only 1 of its 2 work packages are", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public void A_planned_phase_cannot_have_started_work_and_an_in_progress_phase_cannot_be_finished()
    {
        var started = Validate(StatusSamples.Build(("phase1State", "planned")));
        var finished = Validate(StatusSamples.Build(("phase2State", "in_progress"), ("wp21State", "completed")));

        Assert.Contains(started, finding => finding.Message.Contains("Phase 1 is planned, but some of its work packages have started", StringComparison.Ordinal));
        Assert.Contains(finished, finding => finding.Message.Contains("Phase 2 is in progress, but all of its work packages are completed", StringComparison.Ordinal));
    }

    [Theory]
    [Trait("Requirement", "REQ-GOV-003")]
    [InlineData("currentPhase", "7", "The current phase 7 is not in the status file")]
    [InlineData("currentWorkPackage", "\"WP9.9\"", "The current work package WP9.9 is not in the status file")]
    [InlineData("currentWorkPackage", "\"WP2.1\"", "does not belong to the current phase")]
    [InlineData("currentWorkPackage", "\"WP1.1\"", "the active work package must be in progress or blocked")]
    [InlineData("currentBranch", "wp/1.2-other", "differs from the branch of WP1.2")]
    public void The_current_work_must_agree_with_the_rest_of_the_file_so_a_resume_lands_on_the_right_branch(string placeholder, string value, string expectedMessage)
    {
        var findings = Validate(StatusSamples.Build((placeholder, value)));

        Assert.Contains(findings, finding => finding.Message.Contains(expectedMessage, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-003")]
    public void With_no_active_work_package_the_branch_is_the_phase_branch_and_nothing_is_in_progress()
    {
        var idle = StatusSamples.Build(("currentWorkPackage", "null"), ("currentBranch", "phase/1-sample"), ("wp12State", "planned"));
        var wrongBranch = StatusSamples.Build(("currentWorkPackage", "null"), ("currentBranch", "wp/1.2-second"), ("wp12State", "planned"));
        var forgotten = StatusSamples.Build(("currentWorkPackage", "null"), ("currentBranch", "phase/1-sample"));

        Assert.Empty(Validate(idle));
        Assert.Contains(Validate(wrongBranch), finding => finding.Message.Contains("the current branch should be the phase branch 'phase/1-sample'", StringComparison.Ordinal));
        Assert.Contains(Validate(forgotten), finding => finding.Message.Contains("A work package is in progress, but 'current.work-package' is null", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-004")]
    public void Evidence_must_exist_and_a_directory_entry_must_end_with_a_slash()
    {
        using var empty = new TestRepository();

        var missing = StatusValidator.Validate(StatusSamples.Parse(StatusSamples.Valid), empty.Files);

        Assert.Equal(2, missing.Count(finding => finding.Message.Contains("does not exist", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-004")]
    public void A_file_listed_as_a_directory_and_a_directory_listed_as_a_file_are_both_reported()
    {
        using var repository = RepositoryWithEvidence();

        var findings = StatusValidator.Validate(StatusSamples.Parse(StatusSamples.Build(("wp11Evidence", "[\"docs/evidence/first.md/\", \"docs/evidence\"]"))), repository.Files);

        Assert.Equal(2, findings.Count(finding => finding.Message.Contains("does not exist", StringComparison.Ordinal)));
    }

    [Theory]
    [Trait("Requirement", "REQ-GOV-004")]
    [InlineData("../outside.md")]
    [InlineData("/etc/passwd")]
    [InlineData("C:/Windows/win.ini")]
    [InlineData("http://example.test/evidence")]
    public void Evidence_must_stay_inside_the_repository_or_be_an_https_link(string evidence)
    {
        var findings = Validate(StatusSamples.Build(("wp11Evidence", $"[\"{evidence}\"]")));

        Assert.Contains(findings, finding => finding.Message.Contains("must be a repository-relative path or an https link", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-004")]
    public void An_https_link_counts_as_evidence_without_a_file_and_a_repeated_entry_is_reported()
    {
        var link = Validate(StatusSamples.Build(("wp11Evidence", "[\"https://github.com/owner/name/actions/runs/1\"]")));
        var repeated = Validate(StatusSamples.Build(("wp11Evidence", "[\"docs/evidence/first.md\", \"docs/evidence/first.md\"]")));

        Assert.Empty(link);
        Assert.Contains(repeated, finding => finding.Message.Contains("more than once", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public void The_schema_rejects_a_status_file_with_an_unknown_key()
    {
        using var repository = new TestRepository()
            .CopyFromRepository(StatusFile.SchemaPath)
            .Add(StatusFile.DocumentPath, StatusSamples.Valid.Replace("plan-version: \"1.0.0\"", "plan-version: \"1.0.0\"\n  surprise: true", StringComparison.Ordinal));
        List<Finding> findings = [];

        var status = StatusFile.Load(repository.Files, findings);

        Assert.Null(status);
        Assert.Contains(findings, finding => finding.Rule == StatusValidator.SchemaRule && finding.Message.Contains("the property 'surprise' is not allowed", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public void The_schema_rejects_a_state_that_is_not_one_of_the_four()
    {
        using var repository = new TestRepository()
            .CopyFromRepository(StatusFile.SchemaPath)
            .Add(StatusFile.DocumentPath, StatusSamples.Build(("wp12State", "almost_done")));
        List<Finding> findings = [];

        Assert.Null(StatusFile.Load(repository.Files, findings));
        Assert.Contains(findings, finding => finding.Message.Contains("must be one of", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public void A_status_file_that_is_not_valid_yaml_is_a_finding_and_not_a_crash()
    {
        using var repository = new TestRepository().CopyFromRepository(StatusFile.SchemaPath).Add(StatusFile.DocumentPath, "a: [1, 2\n");
        List<Finding> findings = [];

        Assert.Null(StatusFile.Load(repository.Files, findings));
        Assert.Contains(findings, finding => finding.Severity == Severity.Error && finding.Message.Contains("invalid YAML", StringComparison.Ordinal));
    }

    private static TestRepository RepositoryWithEvidence() => new TestRepository().Add("docs/evidence/first.md", "x");

    private static List<Finding> Validate(string yaml)
    {
        using var repository = RepositoryWithEvidence();
        return [.. StatusValidator.Validate(StatusSamples.Parse(yaml), repository.Files)];
    }
}
