using System.Diagnostics;
using Governance.Auditor.Common;
using Governance.Auditor.Security;
using Governance.Auditor.Tests.Support;
using Xunit;

namespace Governance.Auditor.Tests;

public sealed class CommitIdentityRuleTests
{
    private const string FieldSeparator = "\u001f";
    private const string RecordSeparator = "\u001e";
    private const string Noreply = "260491873+RostomOhannessian@users.noreply.github.com";
    private static readonly GitRange Range = new("base123", "head456");

    [Fact]
    [Trait("Requirement", "REQ-GOV-007")]
    public async Task Commits_with_noreply_identities_pass()
    {
        var git = GitLog(
            Commit("1111111aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", Noreply, Noreply, "Work.\n\nCo-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"),
            Commit("2222222bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", Noreply, "noreply@github.com", "Merged on the web."),
            Commit("3333333ccccccccccccccccccccccccccccccccc", "49699333+dependabot[bot]@users.noreply.github.com", "noreply@github.com", "Bump."));
        using var repository = SecuritySamples.RepositoryWithPolicy();

        var findings = await SecuritySamples.RunAsync(repository, "commit-identity", Range, git);

        Assert.Empty(findings);
        Assert.Contains("base123..head456", string.Join(' ', git.Requests.Single().Arguments), StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Requirement", "REQ-GOV-007")]
    [InlineData("jane@example.test", Noreply, "Work.", "author")]
    [InlineData(Noreply, "jane@example.test", "Work.", "committer")]
    [InlineData(Noreply, Noreply, "Work.\n\nCo-authored-by: Jane <jane@example.test>", "co-author")]
    [InlineData("jane@users.noreply.example.test", Noreply, "Work.", "author")]
    [InlineData("jane@github.com", Noreply, "Work.", "author")]
    [InlineData("", Noreply, "Work.", "author")]
    public async Task A_personal_email_in_any_role_is_an_error_that_names_the_commit_and_the_role_but_not_the_address(string author, string committer, string body, string role)
    {
        var git = GitLog(Commit("abcdef0123456789abcdef0123456789abcdef01", author, committer, body));
        using var repository = SecuritySamples.RepositoryWithPolicy();

        var findings = await SecuritySamples.RunAsync(repository, "commit-identity", Range, git);

        var finding = Assert.Single(findings);
        Assert.Equal(Severity.Error, finding.Severity);
        Assert.Contains($"Commit abcdef0: the {role} email is not a GitHub noreply address", finding.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("jane", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-007")]
    public async Task Without_a_base_the_rule_warns_that_nothing_was_checked_and_does_not_start_git()
    {
        var git = new FakeProcessRunner();
        using var repository = SecuritySamples.RepositoryWithPolicy();

        var findings = await SecuritySamples.RunAsync(repository, "commit-identity", range: null, git);

        var finding = Assert.Single(findings);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Empty(git.Requests);
    }

    [Theory]
    [Trait("Requirement", "REQ-GOV-007")]
    [InlineData("--output=/tmp/x", "HEAD")]
    [InlineData("main", "-n1")]
    [InlineData("main; rm -rf /", "HEAD")]
    [InlineData("a b", "HEAD")]
    public async Task A_reference_that_could_be_read_as_an_option_or_a_command_is_refused(string baseReference, string head)
    {
        using var repository = SecuritySamples.RepositoryWithPolicy();
        var git = new FakeProcessRunner();

        var exception = await Assert.ThrowsAsync<GovernanceException>(() => SecuritySamples.RunAsync(repository, "commit-identity", new GitRange(baseReference, head), git));

        Assert.Contains("not a valid Git reference", exception.Message, StringComparison.Ordinal);
        Assert.Empty(git.Requests);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-007")]
    public async Task A_git_failure_is_a_tool_error_and_not_a_pass()
    {
        var git = new FakeProcessRunner().On(_ => true, string.Empty, exitCode: 128, standardError: "fatal: bad revision");
        using var repository = SecuritySamples.RepositoryWithPolicy();

        var exception = await Assert.ThrowsAsync<GovernanceException>(() => SecuritySamples.RunAsync(repository, "commit-identity", Range, git));

        Assert.Contains("Fetch the full history first", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-007")]
    public async Task An_empty_range_is_reported_as_a_note()
    {
        using var repository = SecuritySamples.RepositoryWithPolicy();

        var findings = await SecuritySamples.RunAsync(repository, "commit-identity", Range, GitLog());

        Assert.Equal(Severity.Note, Assert.Single(findings).Severity);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-007")]
    public async Task Real_git_commits_are_checked_by_the_real_command()
    {
        using var repository = SecuritySamples.RepositoryWithPolicy();
        Git(repository.Root, "init", "--quiet", "--initial-branch=main");
        CommitFile(repository.Root, "first", Noreply, Noreply);
        Git(repository.Root, "branch", "base");
        CommitFile(repository.Root, "good", Noreply, "noreply@github.com");
        CommitFile(repository.Root, "bad", "someone@example.test", Noreply);

        var findings = await SecuritySamples.RunAsync(repository, "commit-identity", new GitRange("base", "HEAD"), new SystemProcessRunner());

        var finding = Assert.Single(findings);
        Assert.Contains("the author email is not a GitHub noreply address (domain: example.test)", finding.Message, StringComparison.Ordinal);
    }

    private static FakeProcessRunner GitLog(params string[] commits) =>
        new FakeProcessRunner().On(request => request.FileName == "git" && request.Arguments[0] == "log", string.Concat(commits));

    private static string Commit(string sha, string authorEmail, string committerEmail, string body) =>
        $"{sha}{FieldSeparator}{authorEmail}{FieldSeparator}{committerEmail}{FieldSeparator}{body}{RecordSeparator}\n";

    private static void CommitFile(string directory, string message, string authorEmail, string committerEmail)
    {
        File.AppendAllText(Path.Combine(directory, "file.txt"), message + "\n");
        Git(directory, "add", "file.txt");
        Git(
            directory,
            ["-c", "commit.gpgsign=false", "-c", "user.name=Test", "-c", $"user.email={authorEmail}", "commit", "--quiet", "-m", message],
            new Dictionary<string, string> { ["GIT_COMMITTER_EMAIL"] = committerEmail, ["GIT_COMMITTER_NAME"] = "Test" });
    }

    private static void Git(string directory, params string[] arguments) => Git(directory, arguments, []);

    private static void Git(string directory, string[] arguments, Dictionary<string, string> environment)
    {
        var startInfo = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in environment)
        {
            startInfo.Environment[name] = value;
        }

        using var process = Process.Start(startInfo)!;
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)} failed: {process.StandardError.ReadToEnd()}");
    }
}
