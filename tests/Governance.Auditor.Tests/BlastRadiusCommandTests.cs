using System.Text.Json;
using Governance.Auditor.BlastRadius;
using Governance.Auditor.Tests.Support;
using Xunit;

namespace Governance.Auditor.Tests;

public sealed class BlastRadiusCommandTests
{
    [Fact]
    [Trait("Requirement", "REQ-CI-004")]
    public async Task Paths_from_git_diff_are_evaluated_and_the_request_to_git_is_a_plain_argument_list()
    {
        using var repository = RepositoryWithMap();
        var git = new FakeProcessRunner().On(request => request.FileName == "git" && request.Arguments[0] == "diff", "src/Catalog.Domain/A.cs\0docs/page.md\0");

        var result = await CliRunner.RunAsync(repository, git, "blast-radius", "--base", "origin/phase/1-api-core", "--head", "abc123", "--json");

        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        Assert.Equal(2, document.RootElement.GetProperty("changedPaths").GetInt32());

        var request = Assert.Single(git.Requests);
        Assert.Equal(["diff", "--name-only", "--no-renames", "-z", "origin/phase/1-api-core...abc123"], request.Arguments);
    }

    [Fact]
    [Trait("Requirement", "REQ-CI-004")]
    public async Task Explicit_paths_are_used_without_starting_git()
    {
        using var repository = RepositoryWithMap();
        var git = new FakeProcessRunner();

        var result = await CliRunner.RunAsync(repository, git, "blast-radius", "--path", "src/Catalog.Api/Program.cs");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("layers:  Api", result.Output, StringComparison.Ordinal);
        Assert.Empty(git.Requests);
    }

    [Fact]
    [Trait("Requirement", "REQ-CI-004")]
    public async Task The_github_output_file_gets_one_line_per_output_including_a_run_flag_for_every_job()
    {
        using var repository = RepositoryWithMap();
        var output = Path.Combine(repository.Root, "github-output.txt");

        await CliRunner.RunAsync(repository, "blast-radius", "--path", "docs/page.md", "--github-output", output);

        var lines = (await File.ReadAllLinesAsync(output, TestContext.Current.CancellationToken)).ToDictionary(line => line[..line.IndexOf('=', StringComparison.Ordinal)], line => line[(line.IndexOf('=', StringComparison.Ordinal) + 1)..], StringComparer.Ordinal);

        Assert.Equal("[]", lines["layers"]);
        Assert.Equal("[\"documentation\"]", lines["areas"]);
        Assert.Equal("[\"links\",\"markdown\",\"pr-title\",\"secrets\"]", lines["jobs"]);
        Assert.Equal("false", lines["run-build-test"]);
        Assert.Equal("true", lines["run-markdown"]);
        Assert.Equal("0", lines["unclassified-count"]);
        Assert.All(lines.Keys.Where(key => key.StartsWith("run-", StringComparison.Ordinal)), key => Assert.True(lines[key] is "true" or "false"));
    }

    [Fact]
    [Trait("Requirement", "REQ-CI-004")]
    public async Task The_markdown_summary_lists_documentation_and_reviews_and_escapes_unclassified_paths()
    {
        using var repository = RepositoryWithMap();
        var summary = Path.Combine(repository.Root, "summary.md");

        await CliRunner.RunAsync(repository, "blast-radius", "--path", ".github/workflows/ci.yml", "--path", "weird|name`.xyz", "--summary", summary);

        var markdown = await File.ReadAllTextAsync(summary, TestContext.Current.CancellationToken);
        Assert.Contains("### Blast radius: 2 changed paths", markdown, StringComparison.Ordinal);
        Assert.Contains("Documentation to update:", markdown, StringComparison.Ordinal);
        Assert.Contains("Reviews required:", markdown, StringComparison.Ordinal);
        Assert.Contains("`security-review`", markdown, StringComparison.Ordinal);
        Assert.Contains("`weird|name'.xyz`", markdown, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-CI-004")]
    public async Task An_unclassified_path_is_a_warning_and_not_a_failure()
    {
        using var repository = RepositoryWithMap();

        var result = await CliRunner.RunAsync(repository, "blast-radius", "--path", "mystery.xyz");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("warning [blast-radius-unclassified] mystery.xyz", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-CI-004")]
    public async Task With_neither_a_base_nor_paths_the_command_asks_for_one()
    {
        using var repository = RepositoryWithMap();

        var result = await CliRunner.RunAsync(repository, "blast-radius");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("--base", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Requirement", "REQ-CI-004")]
    [InlineData("--output=x", "HEAD")]
    [InlineData("main", "-n1")]
    [InlineData("a b", "HEAD")]
    [InlineData("a..b", "HEAD")]
    public async Task A_reference_that_could_be_read_as_an_option_is_refused_before_git_starts(string baseReference, string head)
    {
        using var repository = RepositoryWithMap();
        var git = new FakeProcessRunner();

        var result = await CliRunner.RunAsync(repository, git, "blast-radius", "--base", baseReference, "--head", head);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Empty(git.Requests);
    }

    [Fact]
    [Trait("Requirement", "REQ-CI-004")]
    public async Task A_git_failure_is_a_tool_error()
    {
        using var repository = RepositoryWithMap();
        var git = new FakeProcessRunner().On(_ => true, string.Empty, exitCode: 128, standardError: "fatal: bad revision");

        var result = await CliRunner.RunAsync(repository, git, "blast-radius", "--base", "nope");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Fetch the full history first", result.Error, StringComparison.Ordinal);
    }

    private static TestRepository RepositoryWithMap() => new TestRepository().CopyFromRepository(BlastRadiusMap.Path);
}
