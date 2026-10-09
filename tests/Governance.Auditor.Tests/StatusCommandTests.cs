using Governance.Auditor.Status;
using Governance.Auditor.Tests.Support;
using Xunit;

namespace Governance.Auditor.Tests;

public sealed class StatusCommandTests
{
    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public async Task Validate_passes_for_a_consistent_status_file()
    {
        using var repository = RepositoryWith(StatusSamples.Valid);

        var result = await CliRunner.RunAsync(repository, "status", "validate");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("status validate: passed", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public async Task Validate_fails_and_lists_every_problem_for_an_inconsistent_status_file()
    {
        using var repository = RepositoryWith(StatusSamples.Build(("wp11Evidence", "[]"), ("wp12DependsOn", "[\"WP9.9\"]")));

        var result = await CliRunner.RunAsync(repository, "status", "validate");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("WP1.1 is completed but lists no evidence", result.Output, StringComparison.Ordinal);
        Assert.Contains("depends on WP9.9", result.Output, StringComparison.Ordinal);
        Assert.Contains("status validate: failed (2 errors", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public async Task Validate_appends_a_markdown_report_to_the_summary_file()
    {
        using var repository = RepositoryWith(StatusSamples.Build(("wp11Evidence", "[]")));
        var summary = Path.Combine(repository.Root, "summary.md");

        await CliRunner.RunAsync(repository, "status", "validate", "--summary", summary);

        var markdown = File.ReadAllText(summary);
        Assert.Contains("### status validate: failed", markdown, StringComparison.Ordinal);
        Assert.Contains("| Severity | Rule | Location | Message |", markdown, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public async Task A_missing_status_file_fails_the_check_with_a_plain_message()
    {
        using var repository = new TestRepository().CopyFromRepository(StatusFile.SchemaPath);

        var result = await CliRunner.RunAsync(repository, "status", "validate");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("The file 'docs/project/status.yaml' does not exist", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", result.Output + result.Error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public async Task A_missing_schema_is_a_tool_error_with_exit_code_2()
    {
        using var repository = new TestRepository().Add(StatusFile.DocumentPath, StatusSamples.Valid);

        var result = await CliRunner.RunAsync(repository, "status", "validate");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("error: The file 'docs/project/status.schema.json' does not exist", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public async Task Render_writes_the_page_and_then_the_drift_check_passes()
    {
        using var repository = RepositoryWith(StatusSamples.Valid);

        var render = await CliRunner.RunAsync(repository, "status", "render");
        var check = await CliRunner.RunAsync(repository, "status", "render", "--check");

        Assert.Equal(0, render.ExitCode);
        Assert.True(repository.Exists(StatusFile.PagePath));
        Assert.Equal(StatusRenderer.Render(StatusSamples.Parse(StatusSamples.Valid)), repository.Read(StatusFile.PagePath));
        Assert.Equal(0, check.ExitCode);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public async Task The_drift_check_fails_when_the_page_was_edited_by_hand_and_names_the_first_changed_line()
    {
        using var repository = RepositoryWith(StatusSamples.Valid);
        await CliRunner.RunAsync(repository, "status", "render");
        var edited = repository.Read(StatusFile.PagePath).Replace("## Resume here", "## Resume somewhere", StringComparison.Ordinal);
        repository.Add(StatusFile.PagePath, edited);

        var check = await CliRunner.RunAsync(repository, "status", "render", "--check");

        Assert.Equal(1, check.ExitCode);
        Assert.Contains("status-drift", check.Output, StringComparison.Ordinal);
        Assert.Contains("docs/project/STATUS.md:", check.Output, StringComparison.Ordinal);
        Assert.Equal(edited, repository.Read(StatusFile.PagePath));
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public async Task The_drift_check_fails_when_status_yaml_changed_and_the_page_was_not_regenerated()
    {
        using var repository = RepositoryWith(StatusSamples.Valid);
        await CliRunner.RunAsync(repository, "status", "render");
        repository.Add(StatusFile.DocumentPath, StatusSamples.Build(("currentBranch", "wp/1.2-second"), ("openPullRequests", "[{ number: 20, title: \"feat: x\", branch: \"wp/1.2-second\" }]")));

        var check = await CliRunner.RunAsync(repository, "status", "render", "--check");

        Assert.Equal(1, check.ExitCode);
        Assert.Contains("status-drift", check.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public async Task The_drift_check_fails_when_the_page_does_not_exist()
    {
        using var repository = RepositoryWith(StatusSamples.Valid);

        var check = await CliRunner.RunAsync(repository, "status", "render", "--check");

        Assert.Equal(1, check.ExitCode);
        Assert.False(repository.Exists(StatusFile.PagePath));
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public async Task Render_refuses_to_render_an_invalid_status_file_and_writes_nothing()
    {
        using var repository = RepositoryWith(StatusSamples.Build(("wp11Evidence", "[]")));

        var result = await CliRunner.RunAsync(repository, "status", "render");

        Assert.Equal(1, result.ExitCode);
        Assert.False(repository.Exists(StatusFile.PagePath));
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public async Task Render_ignores_the_line_endings_of_the_page_it_compares_with()
    {
        using var repository = RepositoryWith(StatusSamples.Valid);
        await CliRunner.RunAsync(repository, "status", "render");
        var page = repository.Read(StatusFile.PagePath);
        await File.WriteAllTextAsync(Path.Combine(repository.Root, StatusFile.PagePath), page.ReplaceLineEndings("\r\n"), TestContext.Current.CancellationToken);

        var check = await CliRunner.RunAsync(repository, "status", "render", "--check");

        Assert.Equal(0, check.ExitCode);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-002")]
    public async Task An_unknown_command_is_not_a_pass()
    {
        using var repository = new TestRepository();

        var result = await CliRunner.RunAsync(repository, "status", "nonsense");

        Assert.NotEqual(0, result.ExitCode);
    }

    private static TestRepository RepositoryWith(string statusYaml)
    {
        var repository = new TestRepository()
            .CopyFromRepository(StatusFile.SchemaPath)
            .Add(StatusFile.DocumentPath, statusYaml)
            .Add("docs/evidence/first.md", "Evidence.");

        return repository;
    }
}
