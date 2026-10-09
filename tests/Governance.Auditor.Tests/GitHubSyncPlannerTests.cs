using Governance.Auditor.Common;
using Governance.Auditor.GitHubSync;
using Governance.Auditor.Status;
using Governance.Auditor.Tests.Support;
using Xunit;

namespace Governance.Auditor.Tests;

public sealed class GitHubSyncPlannerTests
{
    private static readonly DesiredLabel[] Labels =
    [
        new("work-package", "5319e7", "A planned work package"),
        new("phase:1", "c5def5", "Phase 1"),
        new("phase:2", "c5def5", "Phase 2"),
        new("size:S", "c2e0c6", "Small"),
        new("size:M", "8fd19e", "Medium"),
        new("size:L", "3fa34d", "Large"),
        new("blocked", "000000", "Blocked"),
    ];

    private static readonly DesiredMilestone[] Milestones =
    [
        new("Phase 1: Sample", "The first phase.", "open"),
        new("Phase 2: Later", "The second phase.", "open"),
    ];

    private static readonly StatusDocument Status = StatusSamples.Parse(StatusSamples.Valid);

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public async Task Missing_labels_are_created_and_labels_with_a_different_color_or_description_are_updated()
    {
        var api = new FakeGitHubApi();
        api.Labels.Add(new GitHubLabel("WORK-PACKAGE", "5319e7", "A planned work package"));
        api.Labels.Add(new GitHubLabel("phase:1", "ffffff", "Phase 1"));
        api.Labels.Add(new GitHubLabel("size:S", "c2e0c6", "Old text"));
        api.Labels.Add(new GitHubLabel("unrelated", "123456", null));

        var plan = await GitHubSyncPlanner.PlanAsync(api, Status, Labels, [], Scope(labels: true), TestContext.Current.CancellationToken);

        var descriptions = plan.Actions.Select(action => action.Description).ToList();
        Assert.Contains("update the color of the label 'phase:1'", descriptions);
        Assert.Contains("update the description of the label 'size:S'", descriptions);
        Assert.Contains("create the label 'phase:2'", descriptions);
        Assert.DoesNotContain(descriptions, description => description.Contains("work-package", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(descriptions, description => description.Contains("unrelated", StringComparison.Ordinal));
        Assert.Empty(plan.Findings);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public async Task A_milestone_is_created_when_missing_and_its_state_follows_the_phase_state()
    {
        var api = new FakeGitHubApi();
        api.Milestones.Add(new GitHubMilestone(2, "Phase 1: Sample", "Old description", "closed"));
        var status = StatusSamples.Parse(StatusSamples.Build(("phase1State", "completed"), ("wp12State", "completed"), ("currentWorkPackage", "null"), ("currentBranch", "phase/1-sample")));
        var desired = new[] { new DesiredMilestone("Phase 1: Sample", "The first phase.", "closed"), new DesiredMilestone("Phase 2: Later", "The second phase.", "open") };

        var plan = await GitHubSyncPlanner.PlanAsync(api, status, [], desired, Scope(milestones: true), TestContext.Current.CancellationToken);

        var descriptions = plan.Actions.Select(action => action.Description).ToList();
        Assert.Contains("change the description on the milestone 'Phase 1: Sample'", descriptions);
        Assert.Contains("create the milestone 'Phase 2: Later' (open)", descriptions);
        Assert.DoesNotContain(descriptions, description => description.Contains("closed to", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public void The_milestone_state_comes_from_the_phase_that_names_it_and_the_file_otherwise()
    {
        using var repository = new TestRepository()
            .Add(GitHubSyncPlanner.MilestonesPath, "[{ \"title\": \"Phase 1: Sample\", \"state\": \"open\" }, { \"title\": \"Phase 2: Later\", \"description\": \"Later.\", \"state\": \"open\" }, { \"title\": \"Unowned\", \"state\": \"closed\" }]");
        var status = StatusSamples.Parse(StatusSamples.Build(("phase1State", "completed"), ("wp12State", "completed"), ("currentWorkPackage", "null"), ("currentBranch", "phase/1-sample")));

        var milestones = GitHubSyncPlanner.LoadMilestones(repository.Files, status);

        Assert.Equal("closed", milestones.Single(milestone => milestone.Title == "Phase 1: Sample").State);
        Assert.Equal("open", milestones.Single(milestone => milestone.Title == "Phase 2: Later").State);
        Assert.Equal("closed", milestones.Single(milestone => milestone.Title == "Unowned").State);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public async Task An_issue_is_found_by_its_marker_and_a_completed_work_package_closes_it()
    {
        var api = ApiWithIssues(Issue(3, "WP1.1: First", "WP1.1", state: "open"), Issue(4, "WP1.2: Second", "WP1.2"));

        var plan = await PlanIssues(api);

        var action = Assert.Single(plan.Actions);
        Assert.Equal("issue #3 (WP1.1): close it, because WP1.1 is completed", action.Description);
        Assert.Empty(plan.Findings);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public async Task A_closed_issue_is_reopened_when_its_work_package_is_not_completed()
    {
        var api = ApiWithIssues(Issue(3, "WP1.1: First", "WP1.1", state: "closed"), Issue(4, "WP1.2: Second", "WP1.2", state: "closed"));

        var plan = await PlanIssues(api);

        Assert.Equal("issue #4 (WP1.2): reopen it, because WP1.2 is in progress", Assert.Single(plan.Actions).Description);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public async Task The_managed_labels_follow_status_yaml_and_other_labels_are_kept()
    {
        var api = ApiWithIssues(
            Issue(3, "WP1.1: First", "WP1.1", state: "closed", labels: ["work-package", "phase:1", "size:S"]),
            Issue(4, "WP1.2: Second", "WP1.2", labels: ["work-package", "phase:2", "size:S", "triage", "blocked"]));

        var plan = await PlanIssues(api);

        var action = Assert.Single(plan.Actions);
        Assert.Contains("issue #4 (WP1.2): set the labels to 'phase:1', 'size:M', 'triage', 'work-package'", action.Description, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public async Task A_blocked_work_package_gets_the_blocked_label()
    {
        var status = StatusSamples.Parse(StatusSamples.Build(("wp12State", "blocked"), ("wp12Blockers", "[\"Waiting\"]")));
        var api = ApiWithIssues(Issue(3, "WP1.1: First", "WP1.1", state: "closed"), Issue(4, "WP1.2: Second", "WP1.2"));

        var plan = await GitHubSyncPlanner.PlanAsync(api, status, Labels, Milestones, Scope(issues: true), TestContext.Current.CancellationToken);

        Assert.Contains("'blocked'", Assert.Single(plan.Actions).Description, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public async Task The_title_and_the_milestone_follow_status_yaml()
    {
        var api = ApiWithIssues(Issue(3, "Old title", "WP1.1", state: "closed", milestone: "Phase 2: Later"), Issue(4, "WP1.2: Second", "WP1.2"));

        var plan = await PlanIssues(api);

        var action = Assert.Single(plan.Actions);
        Assert.Contains("set the title", action.Description, StringComparison.Ordinal);
        Assert.Contains("set the milestone to 'Phase 1: Sample'", action.Description, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public async Task An_issue_that_status_yaml_names_but_that_has_no_marker_is_adopted_by_adding_the_marker_and_nothing_else_in_the_body()
    {
        var api = ApiWithIssues(Issue(3, "WP1.1: First", "WP1.1", state: "closed", body: "My hand-written text."), Issue(4, "WP1.2: Second", "WP1.2"));
        api.Issues[0] = api.Issues[0] with { Body = "My hand-written text." };

        var plan = await PlanIssues(api);
        foreach (var action in plan.Actions)
        {
            await action.Apply(api, TestContext.Current.CancellationToken);
        }

        Assert.Equal($"{GitHubSyncPlanner.Marker("WP1.1")}\nMy hand-written text.", api.Issues[0].Body);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public async Task A_work_package_in_scope_without_an_issue_gets_one_created_from_the_plan_and_a_planned_phase_is_skipped_by_default()
    {
        var api = ApiWithIssues(Issue(3, "WP1.1: First", "WP1.1", state: "closed"));
        var status = StatusSamples.Parse(StatusSamples.Build(("wp12State", "planned"), ("currentWorkPackage", "null"), ("currentBranch", "phase/1-sample")));
        // WP1.2 has issue 4 in the sample, so remove it from the fake to simulate a lost issue number.
        status = status with { Phases = [.. status.Phases.Select(phase => phase with { WorkPackages = [.. phase.WorkPackages.Select(workPackage => workPackage.Id == "WP1.2" ? workPackage with { Issue = null } : workPackage)] })] };

        var plan = await GitHubSyncPlanner.PlanAsync(api, status, Labels, Milestones, Scope(issues: true), TestContext.Current.CancellationToken);

        Assert.Equal("create an issue for WP1.2", Assert.Single(plan.Actions).Description);

        await plan.Actions[0].Apply(api, TestContext.Current.CancellationToken);
        var created = api.Issues.Single(issue => issue.Title == "WP1.2: Second");
        Assert.StartsWith(GitHubSyncPlanner.Marker("WP1.2") + "\n", created.Body, StringComparison.Ordinal);
        Assert.Contains("https://github.com/owner/name/blob/master/docs/plans/phase-1.md", created.Body, StringComparison.Ordinal);
        Assert.Equal(["phase:1", "size:M", "work-package"], created.Labels);
        Assert.Equal("Phase 1: Sample", created.MilestoneTitle);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public async Task The_phase_option_includes_the_work_packages_of_a_phase_that_has_no_issues_yet()
    {
        var api = ApiWithIssues(Issue(3, "WP1.1: First", "WP1.1", state: "closed"), Issue(4, "WP1.2: Second", "WP1.2"));

        var plan = await GitHubSyncPlanner.PlanAsync(api, Status, Labels, Milestones, new SyncScope(false, false, true, new HashSet<string> { "2" }), TestContext.Current.CancellationToken);

        Assert.Equal("create an issue for WP2.1", Assert.Single(plan.Actions).Description);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public async Task An_issue_number_that_status_yaml_names_but_that_does_not_exist_is_an_error()
    {
        var api = ApiWithIssues(Issue(4, "WP1.2: Second", "WP1.2"));

        var plan = await PlanIssues(api);

        Assert.Contains(plan.Findings, finding => finding.Severity == Severity.Error && finding.Message.Contains("WP1.1: status.yaml names issue #3, which does not exist", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public async Task A_marker_on_a_different_issue_than_status_yaml_names_is_a_conflict_and_nothing_is_planned_for_it()
    {
        var api = ApiWithIssues(Issue(3, "WP1.1: First", "WP1.1", state: "closed"), Issue(9, "WP1.2: Second", "WP1.2"));
        api.Issues.Add(Issue(4, "Unrelated", "WP7.7"));

        var plan = await PlanIssues(api);

        Assert.Contains(plan.Findings, finding => finding.Message.Contains("status.yaml names issue #4, but issue #9 carries the marker", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Actions, action => action.Description.Contains("WP1.2", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public async Task Two_issues_with_the_same_marker_are_an_error()
    {
        var api = ApiWithIssues(Issue(3, "WP1.1: First", "WP1.1", state: "closed"), Issue(4, "WP1.2: Second", "WP1.2"));
        api.Issues.Add(Issue(5, "Copy", "WP1.2"));

        var plan = await PlanIssues(api);

        Assert.Contains(plan.Findings, finding => finding.Message.Contains("more than one issue carries the marker (#4, #5)", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public async Task An_issue_that_status_yaml_names_but_that_carries_another_work_packages_marker_is_an_error()
    {
        var api = ApiWithIssues(Issue(3, "WP1.1: First", "WP1.1", state: "closed"));
        api.Issues.Add(Issue(4, "Wrong", "WP9.9"));

        var plan = await PlanIssues(api);

        Assert.Contains(plan.Findings, finding => finding.Message.Contains("issue #4 carries the marker of WP9.9", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public async Task A_marker_without_an_issue_number_in_status_yaml_is_a_warning_that_says_which_number_to_record()
    {
        var status = StatusSamples.Parse(StatusSamples.Valid);
        status = status with { Phases = [.. status.Phases.Select(phase => phase with { WorkPackages = [.. phase.WorkPackages.Select(workPackage => workPackage.Id == "WP1.2" ? workPackage with { Issue = null } : workPackage)] })] };
        var api = ApiWithIssues(Issue(3, "WP1.1: First", "WP1.1", state: "closed"), Issue(8, "WP1.2: Second", "WP1.2"));

        var plan = await GitHubSyncPlanner.PlanAsync(api, status, Labels, Milestones, new SyncScope(false, false, true, new HashSet<string> { "1" }), TestContext.Current.CancellationToken);

        var finding = Assert.Single(plan.Findings);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Contains("Set 'issue: 8'", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public async Task Applying_the_plan_makes_github_match_and_a_second_plan_is_empty()
    {
        var api = ApiWithIssues(Issue(3, "Old title", "WP1.1", state: "open", labels: ["triage"]), Issue(4, "WP1.2: Second", "WP1.2", labels: ["size:L"]));
        api.Milestones.Add(new GitHubMilestone(2, "Phase 1: Sample", "Stale", "closed"));

        var first = await GitHubSyncPlanner.PlanAsync(api, Status, Labels, Milestones, new SyncScope(), TestContext.Current.CancellationToken);
        foreach (var action in first.Actions)
        {
            await action.Apply(api, TestContext.Current.CancellationToken);
        }

        var second = await GitHubSyncPlanner.PlanAsync(api, Status, Labels, Milestones, new SyncScope(), TestContext.Current.CancellationToken);

        Assert.NotEmpty(first.Actions);
        Assert.Empty(second.Actions);
        Assert.Empty(second.Findings);
        Assert.Contains("triage", api.Issues[0].Labels);
        Assert.Equal("closed", api.Issues[0].State);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public async Task Planning_never_writes()
    {
        var api = ApiWithIssues(Issue(3, "Old title", "WP1.1"));

        await GitHubSyncPlanner.PlanAsync(api, Status, Labels, Milestones, new SyncScope(), TestContext.Current.CancellationToken);

        Assert.Empty(api.Writes);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public void Labels_are_read_from_the_real_label_file_with_normalized_colors()
    {
        using var repository = new TestRepository().Add(GitHubSyncPlanner.LabelsPath, "[{ \"name\": \"bug\", \"color\": \"#D73A4A\", \"description\": \"Broken\" }, { \"name\": \"plain\", \"color\": \"ffffff\" }]");

        var labels = GitHubSyncPlanner.LoadLabels(repository.Files);

        Assert.Equal(new DesiredLabel("bug", "d73a4a", "Broken"), labels[0]);
        Assert.Equal(string.Empty, labels[1].Description);
    }

    private static Task<SyncPlan> PlanIssues(FakeGitHubApi api) =>
        GitHubSyncPlanner.PlanAsync(api, Status, Labels, Milestones, Scope(issues: true), TestContext.Current.CancellationToken);

    private static SyncScope Scope(bool labels = false, bool milestones = false, bool issues = false) => new(labels, milestones, issues);

    private static FakeGitHubApi ApiWithIssues(params GitHubIssue[] issues)
    {
        var api = new FakeGitHubApi();
        api.Issues.AddRange(issues);
        return api;
    }

    private static GitHubIssue Issue(int number, string title, string workPackage, string state = "open", string[]? labels = null, string? milestone = "Phase 1: Sample", string? body = null)
    {
        var size = workPackage == "WP1.2" ? "size:M" : "size:S";
        return new GitHubIssue(number, title, body ?? $"{GitHubSyncPlanner.Marker(workPackage)}\nText.", state, labels ?? ["work-package", "phase:1", size], milestone);
    }
}
