using Governance.Auditor.Common;
using Governance.Auditor.Tests.Support;
using Xunit;

namespace Governance.Auditor.Tests;

public sealed class WorkflowRuleTests
{
    private static readonly string[] WorkflowRules =
    [
        "workflow-parse", "workflow-permissions", "action-pinning", "workflow-secrets", "checkout-credentials", "pull-request-target", "workflow-limits",
    ];

    [Fact]
    [Trait("Requirement", "REQ-SEC-007")]
    public async Task A_clean_workflow_passes_every_workflow_rule()
    {
        foreach (var rule in WorkflowRules)
        {
            Assert.Empty(await SecuritySamples.RunWorkflowAsync(SecuritySamples.CleanWorkflow, rule));
        }
    }

    // Permissions.
    [Fact]
    [Trait("Requirement", "REQ-SEC-007")]
    public async Task A_workflow_without_top_level_permissions_is_rejected()
    {
        var findings = await SecuritySamples.RunWorkflowAsync(SecuritySamples.Mutate("permissions: {}\njobs:", "jobs:"), "workflow-permissions");

        var finding = Assert.Single(findings);
        Assert.Contains("sets no top-level 'permissions'", finding.Message, StringComparison.Ordinal);
        Assert.Equal(SecuritySamples.WorkflowPath, finding.Path);
    }

    [Theory]
    [Trait("Requirement", "REQ-SEC-007")]
    [InlineData("permissions: {}\njobs:", "permissions: write-all\njobs:", "'write-all'")]
    [InlineData("permissions: {}\njobs:", "permissions: read-all\njobs:", "'read-all'")]
    [InlineData("permissions: {}\njobs:", "permissions:\n  contents: write\njobs:", "grants write access to 'contents'")]
    public async Task Broad_or_write_permissions_at_the_top_level_are_rejected(string find, string replace, string expectedMessage)
    {
        var findings = await SecuritySamples.RunWorkflowAsync(SecuritySamples.Mutate(find, replace), "workflow-permissions");

        Assert.Contains(findings, finding => finding.Message.Contains(expectedMessage, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Requirement", "REQ-SEC-007")]
    public async Task A_job_that_sets_no_permissions_is_rejected_with_its_line()
    {
        var findings = await SecuritySamples.RunWorkflowAsync(SecuritySamples.Mutate("    permissions:\n      contents: read\n", string.Empty), "workflow-permissions");

        var finding = Assert.Single(findings);
        Assert.Contains("The job 'build' sets no permissions", finding.Message, StringComparison.Ordinal);
        Assert.Equal(7, finding.Line);
    }

    [Fact]
    [Trait("Requirement", "REQ-SEC-007")]
    public async Task A_write_scope_on_a_job_is_rejected_unless_the_policy_names_that_workflow_job_and_scope()
    {
        var workflow = SecuritySamples.Mutate("      contents: read", "      contents: read\n      id-token: write");

        var denied = await SecuritySamples.RunWorkflowAsync(workflow, "workflow-permissions");

        using var repository = SecuritySamples.RepositoryWithWorkflow(workflow);
        var policy = repository.Read("governance/policies/security-policy.yaml").Replace(
            "allowed-write-permissions: []",
            "allowed-write-permissions:\n    - { workflow: test.yml, job: build, permission: id-token, reason: OIDC for a sample }",
            StringComparison.Ordinal);
        repository.Add("governance/policies/security-policy.yaml", policy);
        var allowed = await SecuritySamples.RunAsync(repository, "workflow-permissions");

        Assert.Contains(denied, finding => finding.Message.Contains("grants write access to 'id-token'", StringComparison.Ordinal));
        Assert.Empty(allowed);
    }

    [Fact]
    [Trait("Requirement", "REQ-SEC-007")]
    public async Task An_exception_for_one_job_or_scope_does_not_cover_another()
    {
        var workflow = SecuritySamples.Mutate("      contents: read", "      contents: read\n      packages: write");
        using var repository = SecuritySamples.RepositoryWithWorkflow(workflow);
        var policy = repository.Read("governance/policies/security-policy.yaml").Replace(
            "allowed-write-permissions: []",
            "allowed-write-permissions:\n    - { workflow: test.yml, job: other, permission: packages, reason: wrong job }\n    - { workflow: test.yml, job: build, permission: id-token, reason: wrong scope }",
            StringComparison.Ordinal);
        repository.Add("governance/policies/security-policy.yaml", policy);

        var findings = await SecuritySamples.RunAsync(repository, "workflow-permissions");

        Assert.Single(findings, finding => finding.Message.Contains("write access to 'packages'", StringComparison.Ordinal));
    }

    // Action pinning.
    [Theory]
    [Trait("Requirement", "REQ-SEC-002")]
    [InlineData("actions/checkout@v4")]
    [InlineData("actions/checkout@main")]
    [InlineData("actions/checkout@3d3c42e")]
    [InlineData("actions/checkout")]
    [InlineData("actions/checkout@3D3C42E5AAC5BA805825DA76410C181273BA90B1")]
    public async Task An_action_that_is_not_pinned_to_a_full_lowercase_commit_sha_is_rejected(string reference)
    {
        var workflow = SecuritySamples.Mutate($"actions/checkout@{SecuritySamples.Sha} # v7.0.1", $"{reference} # v7.0.1");

        var findings = await SecuritySamples.RunWorkflowAsync(workflow, "action-pinning");

        var finding = Assert.Single(findings, finding => finding.Severity == Severity.Error);
        Assert.Contains("is not pinned to a full 40-character commit SHA", finding.Message, StringComparison.Ordinal);
        Assert.Equal(15, finding.Line);
    }

    [Fact]
    [Trait("Requirement", "REQ-SEC-002")]
    public async Task A_pinned_action_without_a_version_comment_gets_a_warning_and_quoted_references_are_checked_too()
    {
        var noComment = await SecuritySamples.RunWorkflowAsync(SecuritySamples.Mutate(" # v7.0.1", string.Empty), "action-pinning");
        var wrongComment = await SecuritySamples.RunWorkflowAsync(SecuritySamples.Mutate("# v7.0.1", "# trust me"), "action-pinning");
        var quoted = await SecuritySamples.RunWorkflowAsync(SecuritySamples.Mutate($"uses: actions/checkout@{SecuritySamples.Sha}", "uses: \"actions/checkout@v4\""), "action-pinning");

        Assert.Equal(Severity.Warning, Assert.Single(noComment).Severity);
        Assert.Equal(Severity.Warning, Assert.Single(wrongComment).Severity);
        Assert.Contains(quoted, finding => finding.Severity == Severity.Error);
    }

    [Fact]
    [Trait("Requirement", "REQ-SEC-002")]
    public async Task A_local_action_is_allowed_and_a_reusable_workflow_must_be_pinned_like_an_action()
    {
        var local = await SecuritySamples.RunWorkflowAsync(SecuritySamples.Mutate("      - name: Say hello\n", "      - uses: ./.github/actions/local\n      - name: Say hello\n"), "action-pinning");
        var reusable = SecuritySamples.CleanWorkflow.Replace("    steps:", "    uses: owner/repo/.github/workflows/build.yml@v1\n    steps:", StringComparison.Ordinal);
        var reusableFindings = await SecuritySamples.RunWorkflowAsync(reusable, "action-pinning");

        Assert.Empty(local);
        Assert.Contains(reusableFindings, finding => finding.Message.Contains("owner/repo/.github/workflows/build.yml@v1", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Requirement", "REQ-SEC-002")]
    public async Task A_container_action_and_a_job_container_and_a_service_image_need_a_digest()
    {
        const string digest = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var unpinned = SecuritySamples.CleanWorkflow
            .Replace("    steps:", "    container: ubuntu:24.04\n    services:\n      db:\n        image: postgres:17\n    steps:", StringComparison.Ordinal)
            .Replace("      - name: Say hello\n", "      - uses: docker://alpine:3.20\n      - name: Say hello\n", StringComparison.Ordinal);
        var pinned = SecuritySamples.CleanWorkflow
            .Replace("    steps:", $"    container:\n      image: ubuntu:24.04@{digest}\n    services:\n      db:\n        image: postgres:17@{digest}\n    steps:", StringComparison.Ordinal)
            .Replace("      - name: Say hello\n", $"      - uses: docker://alpine:3.20@{digest}\n      - name: Say hello\n", StringComparison.Ordinal);

        var unpinnedFindings = await SecuritySamples.RunWorkflowAsync(unpinned, "action-pinning");
        var pinnedFindings = await SecuritySamples.RunWorkflowAsync(pinned, "action-pinning");

        Assert.Equal(3, unpinnedFindings.Count);
        Assert.Contains(unpinnedFindings, finding => finding.Message.Contains("service 'db' image 'postgres:17'", StringComparison.Ordinal));
        Assert.Contains(unpinnedFindings, finding => finding.Message.Contains("container image 'ubuntu:24.04'", StringComparison.Ordinal));
        Assert.Contains(unpinnedFindings, finding => finding.Message.Contains("docker://alpine:3.20", StringComparison.Ordinal));
        Assert.Empty(pinnedFindings);
    }

    // Secrets.
    [Fact]
    [Trait("Requirement", "REQ-SEC-007")]
    public async Task A_workflow_may_read_the_platform_token_and_no_other_secret()
    {
        var token = await SecuritySamples.RunWorkflowAsync(SecuritySamples.Mutate("run: echo hello", "run: echo ${{ secrets.GITHUB_TOKEN }}"), "workflow-secrets");
        var other = await SecuritySamples.RunWorkflowAsync(SecuritySamples.Mutate("run: echo hello", "run: echo ${{ secrets.DEPLOY_KEY }}"), "workflow-secrets");

        Assert.Empty(token);
        var finding = Assert.Single(other);
        Assert.Contains("secrets.DEPLOY_KEY", finding.Message, StringComparison.Ordinal);
        Assert.Equal(19, finding.Line);
    }

    [Theory]
    [Trait("Requirement", "REQ-SEC-007")]
    [InlineData("run: echo ${{ secrets['DEPLOY_KEY'] }}", "computed name")]
    [InlineData("run: echo ${{ secrets [ 'X' ] }}", "computed name")]
    public async Task A_secret_read_by_a_computed_name_is_rejected_because_it_cannot_be_checked(string step, string expectedMessage)
    {
        var findings = await SecuritySamples.RunWorkflowAsync(SecuritySamples.Mutate("run: echo hello", step), "workflow-secrets");

        Assert.Contains(findings, finding => finding.Message.Contains(expectedMessage, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Requirement", "REQ-SEC-007")]
    public async Task Secrets_inherit_is_rejected_and_a_comment_that_mentions_a_secret_is_not()
    {
        var inherit = SecuritySamples.CleanWorkflow.Replace("    steps:", "    secrets: inherit\n    steps:", StringComparison.Ordinal);
        var comment = SecuritySamples.Mutate("      - name: Say hello", "      # Never use secrets.DEPLOY_KEY here.\n      - name: Say hello");

        Assert.Contains(await SecuritySamples.RunWorkflowAsync(inherit, "workflow-secrets"), finding => finding.Message.Contains("secrets: inherit", StringComparison.Ordinal));
        Assert.Empty(await SecuritySamples.RunWorkflowAsync(comment, "workflow-secrets"));
    }

    // Checkout.
    [Theory]
    [Trait("Requirement", "REQ-SEC-002")]
    [InlineData("persist-credentials: false", "persist-credentials: true", true)]
    [InlineData("persist-credentials: false", "fetch-depth: 0", true)]
    [InlineData("persist-credentials: false", "persist-credentials: \"false\"", false)]
    public async Task A_checkout_must_not_keep_the_token_in_the_git_configuration(string find, string replace, bool rejected)
    {
        var findings = await SecuritySamples.RunWorkflowAsync(SecuritySamples.Mutate(find, replace), "checkout-credentials");

        Assert.Equal(rejected, findings.Count == 1);
    }

    [Fact]
    [Trait("Requirement", "REQ-SEC-002")]
    public async Task A_checkout_with_no_with_block_is_rejected()
    {
        var workflow = SecuritySamples.Mutate("        with:\n          persist-credentials: false\n", string.Empty);

        var finding = Assert.Single(await SecuritySamples.RunWorkflowAsync(workflow, "checkout-credentials"));

        Assert.Contains("persist-credentials: false", finding.Message, StringComparison.Ordinal);
    }

    // pull_request_target.
    [Theory]
    [Trait("Requirement", "REQ-SEC-002")]
    [InlineData("on:\n  pull_request:\n    branches: [master]", "on: pull_request_target")]
    [InlineData("on:\n  pull_request:\n    branches: [master]", "on: [push, pull_request_target]")]
    [InlineData("on:\n  pull_request:\n    branches: [master]", "on:\n  pull_request_target:\n    branches: [master]")]
    public async Task The_pull_request_target_trigger_is_rejected_in_every_form(string find, string replace)
    {
        var findings = await SecuritySamples.RunWorkflowAsync(SecuritySamples.Mutate(find, replace), "pull-request-target");

        Assert.Single(findings);
    }

    [Fact]
    [Trait("Requirement", "REQ-SEC-002")]
    public async Task A_documented_exception_allows_pull_request_target_for_that_workflow_only()
    {
        var workflow = SecuritySamples.Mutate("on:\n  pull_request:\n    branches: [master]", "on: pull_request_target");
        using var repository = SecuritySamples.RepositoryWithWorkflow(workflow);
        repository.Add("governance/policies/security-policy.yaml", repository.Read("governance/policies/security-policy.yaml").Replace(
            "allowed-pull-request-target: []",
            "allowed-pull-request-target:\n    - { workflow: test.yml, reason: Labels only and never checks out code }",
            StringComparison.Ordinal));
        repository.Add(".github/workflows/other.yml", workflow);

        var findings = await SecuritySamples.RunAsync(repository, "pull-request-target");

        var finding = Assert.Single(findings);
        Assert.Equal(".github/workflows/other.yml", finding.Path);
    }

    // Limits.
    [Fact]
    [Trait("Requirement", "REQ-CI-004")]
    public async Task A_job_without_a_timeout_is_rejected()
    {
        var findings = await SecuritySamples.RunWorkflowAsync(SecuritySamples.Mutate("    timeout-minutes: 10\n", string.Empty), "workflow-limits");

        var finding = Assert.Single(findings);
        Assert.Contains("sets no 'timeout-minutes'", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-CI-004")]
    public async Task A_job_that_calls_a_reusable_workflow_needs_no_timeout()
    {
        const string workflow = """
            name: caller
            on: workflow_dispatch
            permissions: {}
            jobs:
              call:
                permissions:
                  contents: read
                uses: ./.github/workflows/build.yml
            """;

        Assert.Empty(await SecuritySamples.RunWorkflowAsync(workflow, "workflow-limits"));
    }

    [Fact]
    [Trait("Requirement", "REQ-CI-004")]
    public async Task An_artifact_upload_must_set_how_long_the_artifact_is_kept()
    {
        const string upload = "      - uses: actions/upload-artifact@cf430e030ddbb5b0abf93d22962f4752f3646cd9 # v7.0.2\n        with:\n          name: results\n          path: out\n";
        var without = SecuritySamples.Mutate("      - name: Say hello", upload + "      - name: Say hello");
        var with = SecuritySamples.Mutate("      - name: Say hello", upload + "          retention-days: 7\n      - name: Say hello");

        var findings = await SecuritySamples.RunWorkflowAsync(without, "workflow-limits");

        Assert.Contains("sets no 'retention-days'", Assert.Single(findings).Message, StringComparison.Ordinal);
        Assert.Empty(await SecuritySamples.RunWorkflowAsync(with, "workflow-limits"));
    }

    // Parsing.
    [Fact]
    [Trait("Requirement", "REQ-SEC-002")]
    public async Task A_workflow_that_cannot_be_parsed_is_an_error_and_not_a_silent_skip()
    {
        var findings = await SecuritySamples.RunWorkflowAsync("jobs: [unclosed\n", "workflow-parse");

        var finding = Assert.Single(findings);
        Assert.Equal(Severity.Error, finding.Severity);
        Assert.Equal(SecuritySamples.WorkflowPath, finding.Path);
    }

    [Fact]
    [Trait("Requirement", "REQ-SEC-002")]
    public async Task A_workflow_that_uses_yaml_anchors_is_refused_so_it_cannot_be_expanded_into_a_bomb()
    {
        var workflow = SecuritySamples.CleanWorkflow.Replace("permissions: {}", "permissions: &none {}", StringComparison.Ordinal);

        var findings = await SecuritySamples.RunWorkflowAsync(workflow, "workflow-parse");

        Assert.Contains("anchors and aliases are not supported", Assert.Single(findings).Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-SEC-002")]
    public async Task Only_files_directly_under_the_workflow_directory_are_workflows()
    {
        using var repository = SecuritySamples.RepositoryWithPolicy()
            .Add(".github/workflows/nested/ignored.yml", "jobs: [unclosed\n")
            .Add(".github/workflows/readme.md", "not a workflow")
            .Add(".github/workflows/real.yaml", SecuritySamples.CleanWorkflow);

        Assert.Empty(await SecuritySamples.RunAsync(repository, "workflow-parse"));
    }
}
