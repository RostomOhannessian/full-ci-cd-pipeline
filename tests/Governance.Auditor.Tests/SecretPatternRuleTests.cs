using Governance.Auditor.Common;
using Governance.Auditor.Tests.Support;
using Xunit;

namespace Governance.Auditor.Tests;

public sealed class SecretPatternRuleTests
{
    // Each sample is built at run time from pieces, so this file never contains a complete token and no scanner mistakes it for one.
    public static TheoryData<string, string> Samples => new()
    {
        { "github-token", "token = \"ghp_" + new string('a', 36) + "\"" },
        { "github-token", "export GH=github_pat_" + new string('b', 30) },
        { "aws-access-key-id", "key: AKIA" + "IOSFODNN7EXAMPLE" },
        { "private-key-block", "<begin>RSA " + "PRIVATE KEY-----" },
        { "private-key-block", "<begin>" + "PRIVATE KEY-----" },
        { "slack-token", "SLACK=xoxb-" + "1234567890-abcdefghij" },
        { "nuget-api-key", "nuget push --api-key oy2" + new string('c', 43) },
        { "json-web-token", "Authorization: Bearer eyJ" + "hbGciOiJIUzI1NiJ9.eyJ" + "zdWIiOiIxMjM0NTY3ODkwIn0." + "abcdefghijklmn" },
        { "url-credentials", "git clone https://" + "user:" + "pass" + "@example.test/repo.git" },
    };

    [Theory]
    [MemberData(nameof(Samples))]
    [Trait("Requirement", "REQ-SEC-001")]
    public async Task Text_that_has_the_shape_of_a_credential_is_found_by_rule_and_line_and_never_shown(string expectedPattern, string sample)
    {
        // The marker keeps the PEM header out of the test display name, so the TRX report is not mistaken for a leaked key.
        var line = sample.Replace("<begin>", "-----" + "BEGIN ", StringComparison.Ordinal);
        using var repository = SecuritySamples.RepositoryWithPolicy().Add("docs/notes.txt", $"first line\n{line}\nthird line\n");

        var findings = await SecuritySamples.RunAsync(repository, "secret-patterns");

        var finding = Assert.Single(findings);
        Assert.Equal(Severity.Error, finding.Severity);
        Assert.Equal("docs/notes.txt", finding.Path);
        Assert.Equal(2, finding.Line);
        Assert.Contains($"pattern '{expectedPattern}'", finding.Message, StringComparison.Ordinal);
        Assert.Contains("The value is not shown", finding.Message, StringComparison.Ordinal);

        foreach (var piece in line.Split([' ', '"', '=', ':'], StringSplitOptions.RemoveEmptyEntries).Where(piece => piece.Length > 20))
        {
            Assert.DoesNotContain(piece, finding.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [Trait("Requirement", "REQ-SEC-001")]
    [InlineData("token = ghp_replace_with_your_token")]
    [InlineData("export GH=ghp_short")]
    [InlineData("key: AKIA-not-a-real-key")]
    [InlineData("-----BEGIN CERTIFICATE-----")]
    [InlineData("git clone https://${USER}:${TOKEN}@example.test/repo.git")]
    [InlineData("git clone https://example.test:8443/repo.git")]
    [InlineData("see https://example.test/a@b for details")]
    [InlineData("image: docker://alpine:3.20@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData("Authorization: Bearer <token>")]
    public async Task Placeholders_and_look_alikes_are_not_reported(string line)
    {
        using var repository = SecuritySamples.RepositoryWithPolicy().Add("docs/notes.txt", line);

        Assert.Empty(await SecuritySamples.RunAsync(repository, "secret-patterns"));
    }

    [Fact]
    [Trait("Requirement", "REQ-SEC-001")]
    public async Task A_binary_file_and_build_output_are_skipped()
    {
        var secret = "ghp_" + new string('a', 36);
        using var repository = SecuritySamples.RepositoryWithPolicy()
            .Add("src/App/bin/Release/output.txt", secret)
            .Add("src/App/obj/cache.txt", secret)
            .Add("TestResults/log.txt", secret);
        await File.WriteAllBytesAsync(Path.Combine(repository.Root, "image.bin"), [0, 1, 2, .. System.Text.Encoding.ASCII.GetBytes(secret)], TestContext.Current.CancellationToken);

        Assert.Empty(await SecuritySamples.RunAsync(repository, "secret-patterns"));
    }

    [Fact]
    [Trait("Requirement", "REQ-SEC-001")]
    public async Task A_path_excluded_by_the_policy_is_skipped_and_the_exclusion_does_not_reach_other_paths()
    {
        var secret = "token: ghp_" + new string('a', 36);
        using var repository = SecuritySamples.RepositoryWithPolicy().Add("docs/research/example.md", secret).Add("docs/other.md", secret);
        repository.Add("governance/policies/security-policy.yaml", repository.Read("governance/policies/security-policy.yaml").Replace("exclude-paths: []", "exclude-paths: ['docs/research/**']", StringComparison.Ordinal));

        var findings = await SecuritySamples.RunAsync(repository, "secret-patterns");

        Assert.Equal("docs/other.md", Assert.Single(findings).Path);
    }

    [Fact]
    [Trait("Requirement", "REQ-SEC-001")]
    public async Task The_policy_file_does_not_trigger_its_own_patterns()
    {
        using var repository = SecuritySamples.RepositoryWithPolicy();

        Assert.Empty(await SecuritySamples.RunAsync(repository, "secret-patterns"));
    }

    [Fact]
    [Trait("Requirement", "REQ-SEC-001")]
    public void A_broken_pattern_in_the_policy_fails_loudly_before_any_file_is_scanned()
    {
        using var repository = SecuritySamples.RepositoryWithPolicy();
        repository.Add("governance/policies/security-policy.yaml", repository.Read("governance/policies/security-policy.yaml").Replace("'\\bxox[abposr]-[A-Za-z0-9-]{10,}'", "'(unclosed'", StringComparison.Ordinal));

        var exception = Assert.Throws<GovernanceException>(() => Security.SecurityPolicy.Load(repository.Files));

        Assert.Contains("slack-token", exception.Message, StringComparison.Ordinal);
    }
}
