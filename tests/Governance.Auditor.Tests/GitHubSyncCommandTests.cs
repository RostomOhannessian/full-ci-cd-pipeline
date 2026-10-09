using System.Text.Json;
using Governance.Auditor.GitHubSync;
using Governance.Auditor.Status;
using Governance.Auditor.Tests.Support;
using Xunit;

namespace Governance.Auditor.Tests;

public sealed class GitHubSyncCommandTests
{
    private const string LabelsJson = """[{ "name": "work-package", "color": "5319e7", "description": "A planned work package" }]""";
    private const string MilestonesJson = """[{ "title": "Phase 1: Sample", "description": "The first phase.", "state": "open" }]""";

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public async Task A_dry_run_prints_the_diff_and_makes_no_write_call()
    {
        using var repository = Repository();
        var gh = Gh(issueState: "open");

        var result = await CliRunner.RunAsync(repository, gh, "github-sync");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("would issue #3 (WP1.1): close it, because WP1.1 is completed.", result.Output, StringComparison.Ordinal);
        Assert.Contains("dry run. 1 changes would be made", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(gh.Requests, request => request.Arguments.Contains("--method"));
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public async Task The_fail_on_drift_option_turns_a_difference_into_exit_code_1()
    {
        using var repository = Repository();

        var result = await CliRunner.RunAsync(repository, Gh(issueState: "open"), "github-sync", "--fail-on-drift");

        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public async Task When_github_already_matches_the_command_says_so_and_exits_with_0_even_with_fail_on_drift()
    {
        using var repository = Repository();

        var result = await CliRunner.RunAsync(repository, Gh(issueState: "closed"), "github-sync", "--fail-on-drift");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("GitHub already matches the repository", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public async Task Apply_sends_one_patch_through_standard_input_and_never_puts_text_on_the_command_line()
    {
        using var repository = Repository();
        var gh = Gh(issueState: "open").On(request => request.Arguments.Contains("PATCH"), "{}");

        var result = await CliRunner.RunAsync(repository, gh, "github-sync", "--apply");

        Assert.Equal(0, result.ExitCode);
        var write = Assert.Single(gh.Requests, request => request.Arguments.Contains("--method"));
        Assert.Equal(["api", "--method", "PATCH", "repos/owner/name/issues/3", "--input", "-"], write.Arguments);
        using var body = JsonDocument.Parse(write.StandardInput!);
        Assert.Equal("closed", body.RootElement.GetProperty("state").GetString());
        Assert.Equal("completed", body.RootElement.GetProperty("state_reason").GetString());
        Assert.False(body.RootElement.TryGetProperty("body", out _));
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public async Task Apply_refuses_to_run_in_github_actions_so_a_workflow_never_holds_a_write_sign_in()
    {
        using var repository = Repository();
        var gh = Gh(issueState: "open");

        var result = await CliRunner.RunAsync(repository, gh, new Dictionary<string, string> { ["GITHUB_ACTIONS"] = "true" }, "github-sync", "--apply");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("refuses to run in GitHub Actions", result.Error, StringComparison.Ordinal);
        Assert.Empty(gh.Requests);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public async Task A_conflict_stops_apply_before_anything_is_changed()
    {
        using var repository = Repository();
        var gh = Gh(issueState: "open", markerOnIssue: "WP9.9");

        var result = await CliRunner.RunAsync(repository, gh, "github-sync", "--apply");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("nothing was changed", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(gh.Requests, request => request.Arguments.Contains("--method"));
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public async Task The_only_option_limits_the_kinds_that_are_read_and_an_unknown_kind_is_rejected()
    {
        using var repository = Repository();
        var gh = Gh(issueState: "open");

        var only = await CliRunner.RunAsync(repository, gh, "github-sync", "--only", "labels");
        var unknown = await CliRunner.RunAsync(repository, gh, "github-sync", "--only", "pages");

        Assert.Contains("GitHub already matches", only.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(gh.Requests, request => request.Arguments.Any(argument => argument.Contains("/issues", StringComparison.Ordinal)));
        Assert.Equal(2, unknown.ExitCode);
        Assert.Contains("Unknown kind 'pages'", unknown.Error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public async Task A_repository_name_that_is_not_owner_slash_name_is_refused()
    {
        using var repository = Repository();

        var result = await CliRunner.RunAsync(repository, new FakeProcessRunner(), "github-sync", "--repo", "owner/name; rm -rf /");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("is not a repository name", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public async Task A_failing_gh_command_gives_a_plain_message_without_the_response_body()
    {
        using var repository = Repository();
        var gh = new FakeProcessRunner().On(_ => true, string.Empty, exitCode: 1, standardError: "gh: Not Found (HTTP 404)\nSECRET-BODY");

        var result = await CliRunner.RunAsync(repository, gh, "github-sync");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("gh auth login", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET-BODY", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public async Task Pages_are_flattened_and_pull_requests_are_ignored_when_reading_issues()
    {
        var gh = new FakeProcessRunner().On(
            request => request.Arguments.Contains("repos/owner/name/issues?state=all&per_page=100"),
            """
            [
              [ { "number": 1, "title": "One", "body": null, "state": "open", "labels": [{ "name": "bug" }], "milestone": null },
                { "number": 2, "title": "A pull request", "body": "x", "state": "open", "labels": [], "milestone": null, "pull_request": {} } ],
              [ { "number": 3, "title": "Three", "body": "text", "state": "closed", "labels": [], "milestone": { "title": "Phase 1" } } ]
            ]
            """);
        var api = new GitHubCliApi(gh, "owner/name", Directory.GetCurrentDirectory());

        var issues = await api.GetIssuesAsync(TestContext.Current.CancellationToken);

        Assert.Equal([1, 3], issues.Select(issue => issue.Number));
        Assert.Equal(string.Empty, issues[0].Body);
        Assert.Equal(["bug"], issues[0].Labels);
        Assert.Equal("Phase 1", issues[1].MilestoneTitle);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public async Task A_label_name_with_a_colon_is_escaped_in_the_url_and_unset_members_are_left_out_of_the_body()
    {
        var gh = new FakeProcessRunner().On(request => request.Arguments.Contains("PATCH"), "{}");
        var api = new GitHubCliApi(gh, "owner/name", Directory.GetCurrentDirectory());

        await api.UpdateLabelAsync("phase:1", "c5def5", null, TestContext.Current.CancellationToken);

        var request = Assert.Single(gh.Requests);
        Assert.Contains("repos/owner/name/labels/phase%3A1", request.Arguments);
        Assert.Equal("""{"color":"c5def5"}""", request.StandardInput);
    }

    private static FakeProcessRunner Gh(string issueState, string markerOnIssue = "WP1.1")
    {
        var issues = $$"""
            [[
              { "number": 3, "title": "WP1.1: First", "body": "{{GitHubSyncPlanner.Marker(markerOnIssue)}}\nText.", "state": "{{issueState}}", "labels": [{ "name": "work-package" }, { "name": "phase:1" }, { "name": "size:S" }], "milestone": { "title": "Phase 1: Sample" } },
              { "number": 4, "title": "WP1.2: Second", "body": "{{GitHubSyncPlanner.Marker("WP1.2")}}\nText.", "state": "open", "labels": [{ "name": "work-package" }, { "name": "phase:1" }, { "name": "size:M" }], "milestone": { "title": "Phase 1: Sample" } }
            ]]
            """;

        return new FakeProcessRunner()
            .On(request => request.Arguments.Contains("repos/owner/name/labels?per_page=100"), $"[{LabelsJson}]")
            .On(request => request.Arguments.Contains("repos/owner/name/milestones?state=all&per_page=100"), """[[{ "number": 2, "title": "Phase 1: Sample", "description": "The first phase.", "state": "open" }]]""")
            .On(request => request.Arguments.Contains("repos/owner/name/issues?state=all&per_page=100"), issues);
    }

    private static TestRepository Repository() =>
        new TestRepository()
            .CopyFromRepository(StatusFile.SchemaPath)
            .Add(StatusFile.DocumentPath, StatusSamples.Valid)
            .Add("docs/evidence/first.md", "Evidence.")
            .Add(GitHubSyncPlanner.LabelsPath, LabelsJson)
            .Add(GitHubSyncPlanner.MilestonesPath, MilestonesJson);
}
