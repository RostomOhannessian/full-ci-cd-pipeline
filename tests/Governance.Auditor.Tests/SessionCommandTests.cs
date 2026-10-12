using System.Text.Json;
using System.Text.RegularExpressions;
using Governance.Auditor.Session;
using Governance.Auditor.Status;
using Governance.Auditor.Tests.Support;
using Xunit;

namespace Governance.Auditor.Tests;

public sealed class SessionCommandTests
{
    private static readonly string[] ReadOnlyGitCommands = ["status", "rev-parse", "rev-list"];
    // The sample project has WP1.1 completed, WP1.2 in progress, and WP2.1 planned. This is the same project once WP1.2 is done, which is
    // the end of phase 1.
    private static readonly (string, string)[] SecondWorkPackageDone =
    [
        ("currentWorkPackage", "null"),
        ("currentBranch", "phase/1-sample"),
        ("phase1State", "completed"),
        ("wp12State", "completed"),
        ("wp12Evidence", "[\"docs/evidence/first.md\"]"),
    ];

    // The sample project with WP1.2 not started, so the next work package is in the same phase as the last one that was completed.
    private static readonly (string, string)[] SecondWorkPackageNotStarted =
    [
        ("currentWorkPackage", "null"),
        ("currentBranch", "phase/1-sample"),
        ("wp12State", "planned"),
        ("wp12Branch", "null"),
    ];

    // Nothing is started and the first two work packages do not depend on each other, so both are ready at once.
    private static readonly (string, string)[] TwoReadyWorkPackages =
    [
        ("currentWorkPackage", "null"),
        ("currentBranch", "phase/1-sample"),
        ("wp11State", "planned"),
        ("wp11Branch", "phase/1-sample"),
        ("wp11Evidence", "[]"),
        ("wp12State", "planned"),
        ("wp12Branch", "null"),
        ("wp12DependsOn", "[]"),
    ];

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public async Task A_fresh_session_with_a_complete_handoff_is_told_to_carry_on_and_gets_no_prompt()
    {
        using var repository = SessionSamples.Repository();

        var result = await CliRunner.RunAsync(repository, SessionSamples.Git(), "session", "--moment", "start", "--context", "fresh");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("session: continue", result.Output, StringComparison.Ordinal);
        Assert.Contains("rule:      fresh-session-start", result.Output, StringComparison.Ordinal);
        Assert.Contains("handoff:   complete", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("kickoff prompt", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public async Task A_dirty_working_tree_at_the_start_stops_the_session_and_names_the_paths()
    {
        using var repository = SessionSamples.Repository();
        var git = SessionSamples.Git(changed: ["docs/a.md", "src/b.cs", "src/c.cs", "src/d.cs"]);

        var result = await CliRunner.RunAsync(repository, git, "session", "--moment", "start");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("session: stop", result.Output, StringComparison.Ordinal);
        Assert.Contains("rule:      leftovers", result.Output, StringComparison.Ordinal);
        Assert.Contains("4 paths have uncommitted changes (for example docs/a.md, src/b.cs, src/c.cs)", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("kickoff prompt", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public async Task One_changed_path_is_reported_in_the_singular()
    {
        using var repository = SessionSamples.Repository();

        var result = await CliRunner.RunAsync(repository, SessionSamples.Git(changed: ["docs/a.md"]), "session", "--moment", "start");

        Assert.Contains("1 path has uncommitted changes (for example docs/a.md)", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public async Task Unpushed_commits_at_the_end_ask_for_the_handoff_to_be_finished()
    {
        using var repository = SessionSamples.Repository();

        var result = await CliRunner.RunAsync(repository, SessionSamples.Git(ahead: 2), "session", "--moment", "end");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("session: finish-handoff", result.Output, StringComparison.Ordinal);
        Assert.Contains("rule:      handoff-first", result.Output, StringComparison.Ordinal);
        Assert.Contains("2 commits are not pushed to origin/wp/1.2-second", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public async Task A_branch_with_no_upstream_and_commits_on_no_remote_is_an_unpushed_branch()
    {
        using var repository = SessionSamples.Repository();

        var result = await CliRunner.RunAsync(repository, SessionSamples.Git(upstream: null, unpushed: 1), "session", "--moment", "end");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("The branch 'wp/1.2-second' has no upstream, and 1 commit is on no remote", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public async Task A_branch_with_no_upstream_but_nothing_that_no_remote_has_leaves_nothing_to_push()
    {
        using var repository = SessionSamples.Repository();

        var result = await CliRunner.RunAsync(repository, SessionSamples.Git(upstream: null, unpushed: 0), "session", "--moment", "end");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("handoff:   complete", result.Output, StringComparison.Ordinal);
        Assert.Contains("upstream none", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public async Task An_invalid_status_file_is_a_handoff_gap_and_the_next_work_package_is_not_guessed()
    {
        using var repository = SessionSamples.Repository(StatusSamples.Build(("wp11Evidence", "[]")), withPage: false);

        var result = await CliRunner.RunAsync(repository, SessionSamples.Git(), "session", "--moment", "end");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("docs/project/status.yaml is not valid (1 errors)", result.Output, StringComparison.Ordinal);
        Assert.Contains("next:      none", result.Output, StringComparison.Ordinal);
        Assert.Contains("status.yaml is not valid, so the boundary cannot be worked out", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("kickoff prompt", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public async Task A_missing_status_file_is_a_handoff_gap_and_not_a_tool_error()
    {
        using var repository = new TestRepository().CopyFromRepository(SessionPolicy.Path).CopyFromRepository(SessionPolicy.SchemaPath).CopyFromRepository(StatusFile.SchemaPath);

        var result = await CliRunner.RunAsync(repository, SessionSamples.Git(), "session", "--moment", "start");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("docs/project/status.yaml is not valid (1 errors)", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public async Task A_status_page_that_status_yaml_does_not_render_is_a_handoff_gap()
    {
        using var repository = SessionSamples.Repository(page: "# Edited by hand\n");

        var result = await CliRunner.RunAsync(repository, SessionSamples.Git(), "session", "--moment", "end");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("docs/project/STATUS.md is not what status.yaml renders. Run 'governance status render'", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public async Task A_missing_status_page_is_a_handoff_gap()
    {
        using var repository = SessionSamples.Repository(withPage: false);

        var result = await CliRunner.RunAsync(repository, SessionSamples.Git(), "session", "--moment", "end");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("docs/project/STATUS.md does not exist", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public async Task A_status_page_with_windows_line_endings_is_not_stale()
    {
        var yaml = StatusSamples.Valid;
        using var repository = SessionSamples.Repository(yaml, page: StatusRenderer.Render(StatusSamples.Parse(yaml)).ReplaceLineEndings("\r\n"));

        var result = await CliRunner.RunAsync(repository, SessionSamples.Git(), "session", "--moment", "end");

        Assert.Contains("handoff:   complete", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public async Task Being_behind_the_upstream_is_a_note_and_not_a_handoff_gap()
    {
        using var repository = SessionSamples.Repository();

        var result = await CliRunner.RunAsync(repository, SessionSamples.Git(behind: 3), "session", "--moment", "start", "--context", "fresh");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("handoff:   complete", result.Output, StringComparison.Ordinal);
        Assert.Contains("The branch is 3 commits behind origin/wp/1.2-second as of the last fetch", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public async Task The_end_of_a_work_package_in_the_same_phase_gets_a_fresh_session_and_the_kickoff_prompt_of_the_next_one()
    {
        using var repository = SessionSamples.Repository(StatusSamples.Build(SecondWorkPackageNotStarted));
        var git = SessionSamples.Git(branch: "wp/1.1-first", upstream: "origin/wp/1.1-first");

        var result = await CliRunner.RunAsync(repository, git, "session", "--moment", "end");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("session: fresh", result.Output, StringComparison.Ordinal);
        Assert.Contains("rule:      work-package", result.Output, StringComparison.Ordinal);
        Assert.Contains("boundary:  package", result.Output, StringComparison.Ordinal);
        Assert.Contains("next:      WP1.2: Second (Issue 4, phase 1), branch wp/1.2-second", result.Output, StringComparison.Ordinal);
        Assert.Contains("--- kickoff prompt ---", result.Output, StringComparison.Ordinal);
        Assert.Contains("Task: WP1.2, Second (Issue 4, phase 1).", result.Output, StringComparison.Ordinal);
        Assert.Contains("created from `phase/1-sample`", result.Output, StringComparison.Ordinal);
        Assert.Contains("`gh issue view 4`", result.Output, StringComparison.Ordinal);
        Assert.Contains("the work-package row in docs/plans/phase-1.md", result.Output, StringComparison.Ordinal);
        Assert.Contains("--- end of kickoff prompt ---", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public async Task A_next_work_package_in_another_phase_is_a_phase_boundary_and_a_fresh_session()
    {
        using var repository = SessionSamples.Repository(StatusSamples.Build(SecondWorkPackageDone));

        var result = await CliRunner.RunAsync(repository, SessionSamples.Git(branch: "phase/1-sample", upstream: "origin/phase/1-sample"), "session", "--moment", "end");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("session: fresh", result.Output, StringComparison.Ordinal);
        Assert.Contains("rule:      phase-boundary", result.Output, StringComparison.Ordinal);
        Assert.Contains("boundary:  phase", result.Output, StringComparison.Ordinal);
        Assert.Contains("WP2.1 is in phase 2, and the last completed work package, WP1.2, is in phase 1.", result.Output, StringComparison.Ordinal);
        Assert.Contains("next:      WP2.1: Third (no Issue yet, phase 2), branch wp/2.1-third", result.Output, StringComparison.Ordinal);
        Assert.Contains("WP2.1 has no Issue in status.yaml", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public async Task A_blocked_phase_with_every_work_package_completed_is_a_phase_boundary_through_the_command()
    {
        using var repository = SessionSamples.Repository(StatusSamples.Build([.. SecondWorkPackageDone, ("phase1State", "blocked")]));

        var result = await CliRunner.RunAsync(repository, SessionSamples.Git(branch: "phase/1-sample", upstream: "origin/phase/1-sample"), "session", "--moment", "end");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("rule:      phase-boundary", result.Output, StringComparison.Ordinal);
        Assert.Contains("Every work package of phase 1 is completed, so the phase exit gate comes next.", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public async Task A_compacted_context_at_the_end_gets_a_fresh_session_even_inside_a_phase()
    {
        using var repository = SessionSamples.Repository(StatusSamples.Build(SecondWorkPackageNotStarted));

        var result = await CliRunner.RunAsync(repository, SessionSamples.Git(branch: "phase/1-sample", upstream: "origin/phase/1-sample"), "session", "--moment", "end", "--context", "compacted");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("rule:      compacted", result.Output, StringComparison.Ordinal);
        Assert.Contains("--- kickoff prompt ---", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public async Task Follow_up_on_the_open_pull_request_continues_in_this_session_and_prints_no_prompt()
    {
        using var repository = SessionSamples.Repository(StatusSamples.Build(("openPullRequests", "[{ number: 19, title: \"feat(x): add it\", branch: \"wp/1.2-second\" }]")));

        var result = await CliRunner.RunAsync(repository, SessionSamples.Git(), "session", "--moment", "end", "--task", "pull-request");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("session: continue", result.Output, StringComparison.Ordinal);
        Assert.Contains("rule:      pull-request-follow-up", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("kickoff prompt", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public async Task Follow_up_after_a_compaction_gets_a_fresh_session_with_the_pull_request_prompt()
    {
        using var repository = SessionSamples.Repository(StatusSamples.Build(("openPullRequests", "[{ number: 19, title: \"feat(x): add it\", branch: \"wp/1.2-second\" }]")));

        var result = await CliRunner.RunAsync(repository, SessionSamples.Git(), "session", "--moment", "end", "--task", "pull-request", "--context", "compacted");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("session: fresh", result.Output, StringComparison.Ordinal);
        Assert.Contains("to follow up on the open pull request #19: feat(x): add it (branch `wp/1.2-second`)", result.Output, StringComparison.Ordinal);
        Assert.Contains("`gh pr checks 19`", result.Output, StringComparison.Ordinal);
        Assert.Contains("the failed checks of this pull request, and nothing else", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public async Task Follow_up_with_no_open_pull_request_in_the_status_file_says_so_and_prints_no_prompt()
    {
        using var repository = SessionSamples.Repository();

        var result = await CliRunner.RunAsync(repository, SessionSamples.Git(), "session", "--moment", "end", "--task", "pull-request", "--context", "compacted");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("status.yaml lists no open pull request for this branch", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("--- kickoff prompt ---", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public async Task The_only_open_pull_request_is_used_when_the_branch_names_none()
    {
        using var repository = SessionSamples.Repository(StatusSamples.Build(("openPullRequests", "[{ number: 21, title: \"fix(x): repair it\", branch: \"wp/1.9-other\" }]")));

        var result = await CliRunner.RunAsync(repository, SessionSamples.Git(branch: "phase/1-sample"), "session", "--moment", "end", "--task", "pull-request", "--context", "compacted");

        Assert.Contains("pull request #21: fix(x): repair it (branch `wp/1.9-other`)", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public async Task A_bug_fix_gets_a_fresh_session_and_the_test_first_prompt_with_the_phase_branch()
    {
        using var repository = SessionSamples.Repository();

        var result = await CliRunner.RunAsync(repository, SessionSamples.Git(), "session", "--moment", "start", "--task", "bug-fix");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("session: fresh", result.Output, StringComparison.Ordinal);
        Assert.Contains("rule:      bug-fix", result.Output, StringComparison.Ordinal);
        Assert.Contains("including the smallest bug fix", result.Output, StringComparison.Ordinal);
        Assert.Contains("The fix branch is `fix/<slug>`", result.Output, StringComparison.Ordinal);
        Assert.Contains("merge `master` back into the active phase branch, `phase/1-sample`", result.Output, StringComparison.Ordinal);
        Assert.Contains("Write the failing regression test first", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public async Task A_bug_fix_in_a_session_that_began_fresh_is_done_there()
    {
        using var repository = SessionSamples.Repository();

        var result = await CliRunner.RunAsync(repository, SessionSamples.Git(), "session", "--moment", "start", "--task", "bug-fix", "--context", "fresh");

        Assert.Contains("session: continue", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public async Task Every_kickoff_prompt_is_filled_in_completely()
    {
        using var repository = SessionSamples.Repository(StatusSamples.Build([.. SecondWorkPackageNotStarted, ("openPullRequests", "[{ number: 19, title: \"feat(x): add it\", branch: \"phase/1-sample\" }]")]));
        var git = SessionSamples.Git(branch: "phase/1-sample", upstream: "origin/phase/1-sample");

        foreach (var task in new[] { "work-package", "pull-request", "bug-fix" })
        {
            var result = await CliRunner.RunAsync(repository, git, "session", "--moment", "end", "--task", task, "--context", "compacted", "--json");
            using var document = JsonDocument.Parse(result.Output);
            var prompt = document.RootElement.GetProperty("kickoffPrompt").GetString();

            Assert.NotNull(prompt);
            Assert.DoesNotMatch(new Regex(@"\{[a-z-]+\}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)), prompt);
            Assert.DoesNotContain('\r', prompt);
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public async Task A_title_that_looks_like_a_placeholder_is_copied_and_never_expanded_again()
    {
        using var repository = SessionSamples.Repository(StatusSamples.Build([.. SecondWorkPackageNotStarted, ("wp12Title", "Fix {branch} and {issue} handling")]));

        var result = await CliRunner.RunAsync(repository, SessionSamples.Git(branch: "phase/1-sample", upstream: "origin/phase/1-sample"), "session", "--moment", "end");

        Assert.Contains("Task: WP1.2, Fix {branch} and {issue} handling (Issue 4, phase 1).", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public async Task The_chosen_next_work_package_wins_over_an_earlier_ready_one()
    {
        using var repository = SessionSamples.Repository(StatusSamples.Build([.. TwoReadyWorkPackages, ("nextWorkPackageLine", "next-work-package: \"WP1.2\"")]));

        var result = await CliRunner.RunAsync(repository, SessionSamples.Git(branch: "phase/1-sample", upstream: "origin/phase/1-sample"), "session", "--moment", "end", "--json");

        using var document = JsonDocument.Parse(result.Output);
        Assert.Equal("WP1.2", document.RootElement.GetProperty("nextWorkPackage").GetProperty("id").GetString());
        Assert.Equal(0, document.RootElement.GetProperty("notes").GetArrayLength());
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public async Task Without_a_choice_the_first_ready_work_package_in_phase_order_is_next()
    {
        using var repository = SessionSamples.Repository(StatusSamples.Build(TwoReadyWorkPackages));

        var result = await CliRunner.RunAsync(repository, SessionSamples.Git(branch: "phase/1-sample", upstream: "origin/phase/1-sample"), "session", "--moment", "end", "--json");

        using var document = JsonDocument.Parse(result.Output);
        Assert.Equal("WP1.1", document.RootElement.GetProperty("nextWorkPackage").GetProperty("id").GetString());
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public async Task A_chosen_work_package_that_is_not_ready_is_ignored_with_a_note_that_names_what_it_waits_for()
    {
        using var repository = SessionSamples.Repository(StatusSamples.Build([.. SecondWorkPackageNotStarted, ("nextWorkPackageLine", "next-work-package: \"WP2.1\"")]));

        var result = await CliRunner.RunAsync(repository, SessionSamples.Git(branch: "phase/1-sample", upstream: "origin/phase/1-sample"), "session", "--moment", "end");

        Assert.Contains("resume.next-work-package names WP2.1, which waits for WP1.2, so the first ready work package is used.", result.Output, StringComparison.Ordinal);
        Assert.Contains("next:      WP1.2:", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public async Task With_no_ready_work_package_the_note_says_so_and_no_prompt_is_printed()
    {
        using var repository = SessionSamples.Repository(StatusSamples.Build(
        [
            .. SecondWorkPackageDone,
            ("phase2State", "in_progress"),
            ("wp21State", "blocked"),
            ("wp21Blockers", "[\"Waiting for a decision\"]"),
        ]));

        var result = await CliRunner.RunAsync(repository, SessionSamples.Git(branch: "phase/1-sample", upstream: "origin/phase/1-sample"), "session", "--moment", "end");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("next:      none", result.Output, StringComparison.Ordinal);
        Assert.Contains("No work package is ready: each one is completed, blocked, or waits for a dependency.", result.Output, StringComparison.Ordinal);
        Assert.Contains("There is no kickoff prompt, because no work package is ready.", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("--- kickoff prompt ---", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Requirement", "REQ-AI-004")]
    [InlineData("--moment", "middle")]
    [InlineData("--task", "refactor")]
    [InlineData("--context", "stale")]
    public async Task An_unknown_moment_task_or_context_is_refused_before_git_starts(string option, string value)
    {
        using var repository = SessionSamples.Repository();
        var git = new FakeProcessRunner();
        string[] arguments = option == "--moment" ? ["session", "--moment", value] : ["session", "--moment", "end", option, value];

        var result = await CliRunner.RunAsync(repository, git, arguments);

        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain("session:", result.Output, StringComparison.Ordinal);
        Assert.Empty(git.Requests);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public async Task The_moment_is_required()
    {
        using var repository = SessionSamples.Repository();

        var result = await CliRunner.RunAsync(repository, new FakeProcessRunner(), "session");

        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain("session:", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public async Task A_git_failure_is_a_tool_error_with_exit_code_2()
    {
        using var repository = SessionSamples.Repository();
        var git = new FakeProcessRunner().On(_ => true, string.Empty, exitCode: 128, standardError: "fatal: not a git repository");

        var result = await CliRunner.RunAsync(repository, git, "session", "--moment", "start");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Git could not report the state of the working tree. fatal: not a git repository", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public async Task An_answer_from_git_that_the_tool_cannot_read_is_a_tool_error_and_not_a_guess()
    {
        using var repository = SessionSamples.Repository();
        var git = new FakeProcessRunner()
            .On(request => request.Arguments[0] == "status", string.Empty)
            .On(request => request.Arguments.Contains("HEAD") && request.Arguments[0] == "rev-parse", "wp/1.2-second\n")
            .On(request => request.Arguments.Contains("@{upstream}") && request.Arguments[0] == "rev-parse", "origin/wp/1.2-second\n")
            .On(request => request.Arguments[0] == "rev-list", "many\tmany\n");

        var result = await CliRunner.RunAsync(repository, git, "session", "--moment", "end");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("the tool cannot read", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public async Task A_missing_policy_file_is_a_tool_error_with_exit_code_2()
    {
        using var repository = new TestRepository().CopyFromRepository(SessionPolicy.SchemaPath);

        var result = await CliRunner.RunAsync(repository, SessionSamples.Git(), "session", "--moment", "start");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("session-policy.yaml", result.Error, StringComparison.Ordinal);
        Assert.Contains("does not exist", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public async Task A_policy_file_with_an_unknown_value_is_a_tool_error_with_exit_code_2()
    {
        using var repository = SessionSamples.Repository();
        repository.Add(SessionPolicy.Path, repository.Read(SessionPolicy.Path).Replace("advice: stop", "advice: wait", StringComparison.Ordinal));

        var result = await CliRunner.RunAsync(repository, SessionSamples.Git(), "session", "--moment", "start");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("/rules/0/advice", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public async Task The_check_only_reads_git_and_never_reaches_the_network_or_changes_anything()
    {
        using var repository = SessionSamples.Repository();
        var git = SessionSamples.Git(ahead: 1, behind: 1, changed: ["docs/a.md"]);

        await CliRunner.RunAsync(repository, git, "session", "--moment", "end");

        Assert.NotEmpty(git.Requests);
        Assert.All(git.Requests, request =>
        {
            Assert.Equal("git", request.FileName);
            Assert.Contains(request.Arguments[0], ReadOnlyGitCommands);
        });
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public async Task The_check_changes_no_file_in_the_repository()
    {
        using var repository = SessionSamples.Repository();
        var before = Snapshot(repository.Root);

        await CliRunner.RunAsync(repository, SessionSamples.Git(), "session", "--moment", "end");

        Assert.Equal(before, Snapshot(repository.Root));
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public async Task The_json_output_is_one_line_with_the_advice_the_rule_the_gaps_and_the_next_work_package()
    {
        using var repository = SessionSamples.Repository(StatusSamples.Build(SecondWorkPackageNotStarted));

        var result = await CliRunner.RunAsync(repository, SessionSamples.Git(branch: "phase/1-sample", upstream: "origin/phase/1-sample", behind: 2, changed: ["a.md", "b.md"]), "session", "--moment", "end", "--json");

        Assert.Equal(1, result.ExitCode);
        Assert.DoesNotContain('\n', result.Output.TrimEnd('\n'));
        using var document = JsonDocument.Parse(result.Output);
        var root = document.RootElement;

        Assert.Equal("finish-handoff", root.GetProperty("advice").GetString());
        Assert.Equal("handoff-first", root.GetProperty("rule").GetString());
        Assert.Equal("end", root.GetProperty("moment").GetString());
        Assert.Equal("work-package", root.GetProperty("task").GetString());
        Assert.Equal("used", root.GetProperty("context").GetString());
        Assert.Equal("package", root.GetProperty("boundary").GetString());
        Assert.Equal("incomplete", root.GetProperty("handoff").GetString());
        Assert.Equal("uncommitted-changes", root.GetProperty("gaps")[0].GetProperty("kind").GetString());
        Assert.Equal(2, root.GetProperty("reasons").GetArrayLength());
        Assert.Equal(1, root.GetProperty("notes").GetArrayLength());
        Assert.Equal("WP1.2", root.GetProperty("nextWorkPackage").GetProperty("id").GetString());
        Assert.Equal(2, root.GetProperty("git").GetProperty("changedPaths").GetInt32());
        Assert.Equal(2, root.GetProperty("git").GetProperty("behind").GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("kickoffPrompt").ValueKind);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public async Task The_markdown_summary_holds_the_answer_and_puts_the_prompt_in_a_fence_that_it_cannot_close()
    {
        using var repository = SessionSamples.Repository(StatusSamples.Build([.. SecondWorkPackageNotStarted, ("wp12Title", "Handle ```text fences")]));
        var summary = Path.Combine(repository.Root, "summary.md");

        await CliRunner.RunAsync(repository, SessionSamples.Git(branch: "phase/1-sample", upstream: "origin/phase/1-sample"), "session", "--moment", "end", "--summary", summary);

        var markdown = await File.ReadAllTextAsync(summary, TestContext.Current.CancellationToken);
        Assert.Contains("### Session boundary: fresh", markdown, StringComparison.Ordinal);
        Assert.Contains("| Question | Answer |", markdown, StringComparison.Ordinal);
        Assert.Contains("| Rule | `work-package` |", markdown, StringComparison.Ordinal);
        Assert.Contains("\nKickoff prompt:\n\n````text\n", markdown, StringComparison.Ordinal);
        Assert.Contains("Handle ```text fences", markdown, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public async Task The_markdown_summary_lists_the_handoff_gaps()
    {
        using var repository = SessionSamples.Repository();
        var summary = Path.Combine(repository.Root, "summary.md");

        await CliRunner.RunAsync(repository, SessionSamples.Git(ahead: 1), "session", "--moment", "end", "--summary", summary);

        var markdown = await File.ReadAllTextAsync(summary, TestContext.Current.CancellationToken);
        Assert.Contains("### Session boundary: finish-handoff", markdown, StringComparison.Ordinal);
        Assert.Contains("Handoff gaps:", markdown, StringComparison.Ordinal);
        Assert.Contains("1 commit is not pushed to origin/wp/1.2-second", markdown, StringComparison.Ordinal);
    }

    private static string Snapshot(string root) => string.Join(
        '\n',
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(file => $"{Path.GetRelativePath(root, file)}:{File.GetLastWriteTimeUtc(file).Ticks}:{new FileInfo(file).Length}"));
}

public sealed class SessionAdvisorTests
{
    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public void The_boundary_is_a_phase_when_the_next_work_package_number_ends_in_zero()
    {
        var status = StatusSamples.Parse(StatusSamples.Build(
            ("currentWorkPackage", "null"),
            ("currentBranch", "phase/1-sample"),
            ("phase1State", "completed"),
            ("wp12State", "completed"),
            ("wp12Evidence", "[\"docs/evidence/first.md\"]"),
            ("wp21Id", "WP2.0")));

        var next = SessionAdvisor.ChooseNext(status, []);
        var (kind, reason) = SessionAdvisor.Classify(status, next);

        Assert.Equal("WP2.0", next?.Id);
        Assert.Equal("phase", kind);
        Assert.Contains("WP2.0 opens a phase", reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public void The_boundary_is_a_phase_when_every_work_package_of_the_current_phase_is_completed_but_the_phase_is_not()
    {
        // A status file with an in-progress phase and every work package completed does not validate, so only a blocked phase reaches this
        // case through the command. The rule is tested here on a file that has not been validated.
        var status = StatusSamples.Parse(StatusSamples.Build(
            ("currentWorkPackage", "null"),
            ("currentBranch", "phase/1-sample"),
            ("wp12State", "completed"),
            ("wp12Evidence", "[\"docs/evidence/first.md\"]")));

        var (kind, reason) = SessionAdvisor.Classify(status, next: null);

        Assert.Equal("phase", kind);
        Assert.Contains("Every work package of phase 1 is completed, so the phase exit gate comes next.", reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public void The_boundary_is_a_package_inside_a_phase_and_a_phase_when_nothing_is_completed_yet()
    {
        var inside = StatusSamples.Parse(StatusSamples.Build(("currentWorkPackage", "null"), ("currentBranch", "phase/1-sample"), ("wp12State", "planned"), ("wp12Branch", "null")));
        var nothingDone = StatusSamples.Parse(StatusSamples.Build(TwoReady));

        var (insideKind, insideReason) = SessionAdvisor.Classify(inside, SessionAdvisor.ChooseNext(inside, []));
        var (startKind, startReason) = SessionAdvisor.Classify(nothingDone, SessionAdvisor.ChooseNext(nothingDone, []));

        Assert.Equal("package", insideKind);
        Assert.Contains("WP1.2 is in the same phase as the last completed work package, WP1.1.", insideReason, StringComparison.Ordinal);
        Assert.Equal("phase", startKind);
        Assert.Contains("No work package is completed yet", startReason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public void A_work_package_that_is_blocked_or_waits_for_a_dependency_is_never_ready()
    {
        var status = StatusSamples.Parse(StatusSamples.Build(
            ("currentWorkPackage", "null"),
            ("currentBranch", "phase/1-sample"),
            ("wp11State", "blocked"),
            ("wp11Evidence", "[]"),
            ("wp11Branch", "phase/1-sample"),
            ("wp12State", "planned"),
            ("wp12Branch", "null")));

        var next = SessionAdvisor.ChooseNext(status, []);

        Assert.Null(next);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public void A_work_package_in_progress_is_ready_and_comes_first_in_phase_order()
    {
        var status = StatusSamples.Parse(StatusSamples.Valid);

        var next = SessionAdvisor.ChooseNext(status, []);

        Assert.Equal("WP1.2", next?.Id);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public void When_every_work_package_is_completed_nothing_is_next()
    {
        var status = StatusSamples.Parse(StatusSamples.Build(("wp11State", "completed"), ("wp12State", "completed"), ("wp21State", "completed")));
        List<string> notes = [];

        var next = SessionAdvisor.ChooseNext(status, notes);

        Assert.Null(next);
        Assert.Single(notes);
        Assert.Contains("No work package is ready", notes[0], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public void A_chosen_work_package_that_is_not_in_the_file_or_is_completed_is_ignored_with_a_note()
    {
        List<string> notes = [];
        var missing = StatusSamples.Parse(StatusSamples.Build(("nextWorkPackageLine", "next-work-package: \"WP9.9\"")));
        var completed = StatusSamples.Parse(StatusSamples.Build(("nextWorkPackageLine", "next-work-package: \"WP1.1\"")));

        Assert.Equal("WP1.2", SessionAdvisor.ChooseNext(missing, notes)?.Id);
        Assert.Equal("WP1.2", SessionAdvisor.ChooseNext(completed, notes)?.Id);
        Assert.Contains("resume.next-work-package names WP9.9, which is not in the status file", notes[0], StringComparison.Ordinal);
        Assert.Contains("resume.next-work-package names WP1.1, which is completed", notes[1], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public void The_exit_code_is_1_for_advice_that_blocks_the_session_and_0_for_the_rest()
    {
        Assert.True(SessionVocabulary.IsBlocking("stop"));
        Assert.True(SessionVocabulary.IsBlocking("finish-handoff"));
        Assert.False(SessionVocabulary.IsBlocking("continue"));
        Assert.False(SessionVocabulary.IsBlocking("fresh"));
    }

    // Nothing is started and the first two work packages do not depend on each other, so both are ready at once.
    private static readonly (string, string)[] TwoReady =
    [
        ("currentWorkPackage", "null"),
        ("currentBranch", "phase/1-sample"),
        ("wp11State", "planned"),
        ("wp11Branch", "phase/1-sample"),
        ("wp11Evidence", "[]"),
        ("wp12State", "planned"),
        ("wp12Branch", "null"),
        ("wp12DependsOn", "[]"),
    ];
}
