using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Documentation.Auditor.Tests.Support;
using Governance.Auditor.Documents;
using Xunit;

namespace Documentation.Auditor.Tests;

/// <summary>REQ-CI-005: the documentation checks run on the intended events, from pinned tools, with the least permission each job needs.</summary>
public sealed class DocsWorkflowTests
{
    private static readonly JsonObject Workflow = (JsonObject)YamlDocument.Parse(File.ReadAllText(Path.Combine(RealRepository.Root, ".github", "workflows", "docs.yml")), "docs.yml")!;

    private static readonly string Compose = File.ReadAllText(Path.Combine(RealRepository.Root, "tools", "lint", "compose.yaml")).ReplaceLineEndings("\n");

    private static readonly string[] PullRequestJobs = ["markdown", "links", "secrets", "spelling", "diagrams", "docs-site", "docs-audit"];

    [Fact]
    [Trait("Requirement", "REQ-CI-005")]
    public void Internal_checks_run_on_every_pull_request_to_master_and_phase_branches_with_no_path_filter()
    {
        var on = (JsonObject)Workflow["on"]!;
        var pullRequest = (JsonObject)on["pull_request"]!;

        Assert.Equal(["master", "phase/**"], ((JsonArray)pullRequest["branches"]!).Select(branch => (string)branch!));
        Assert.False(pullRequest.ContainsKey("paths"), "A path filter would let a pull request skip a required check.");
        Assert.False(pullRequest.ContainsKey("paths-ignore"), "A path filter would let a pull request skip a required check.");
        Assert.False(on.ContainsKey("pull_request_target"));
        Assert.Equal("master", (string?)on["push"]!["branches"]![0]);
        Assert.True(on.ContainsKey("workflow_dispatch"));
    }

    [Fact]
    [Trait("Requirement", "REQ-CI-005")]
    public void The_external_link_check_runs_nightly_and_by_hand_and_never_on_a_pull_request()
    {
        var on = (JsonObject)Workflow["on"]!;
        var schedule = Assert.Single((JsonArray)on["schedule"]!);
        var job = (JsonObject)Workflow["jobs"]!["external-links"]!;
        var condition = (string)job["if"]!;

        Assert.Matches(@"^\d{1,2} \d{1,2} \* \* \*$", (string)schedule!["cron"]!);
        Assert.Contains("schedule", condition, StringComparison.Ordinal);
        Assert.Contains("workflow_dispatch", condition, StringComparison.Ordinal);
        Assert.DoesNotContain("pull_request", condition, StringComparison.Ordinal);
        Assert.Contains("run --rm -T external-links", Run(job), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-CI-005")]
    public void The_pull_request_jobs_skip_the_nightly_run_so_it_checks_only_the_external_links()
    {
        foreach (var id in PullRequestJobs)
        {
            var job = (JsonObject)Workflow["jobs"]![id]!;

            Assert.Equal("${{ github.event_name != 'schedule' }}", (string?)job["if"]);
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-CI-005")]
    public void The_three_required_checks_keep_their_names_and_the_new_jobs_have_theirs()
    {
        var jobs = (JsonObject)Workflow["jobs"]!;
        var names = jobs.ToDictionary(pair => pair.Key, pair => (string)pair.Value!["name"]!, StringComparer.Ordinal);

        Assert.Equal("Markdown style", names["markdown"]);
        Assert.Equal("Internal links", names["links"]);
        Assert.Equal("Secret scan (full history)", names["secrets"]);
        Assert.Equal("Spelling", names["spelling"]);
        Assert.Equal("Diagrams", names["diagrams"]);
        Assert.Equal("Documentation site", names["docs-site"]);
        Assert.Equal("Documentation auditor", names["docs-audit"]);
        Assert.Equal("External links", names["external-links"]);

        var rulesets = File.ReadAllText(Path.Combine(RealRepository.Root, "governance", "github", "rulesets", "master.json"))
            + File.ReadAllText(Path.Combine(RealRepository.Root, "governance", "github", "rulesets", "phase.json"));

        foreach (var required in new[] { "Markdown style", "Internal links", "Secret scan (full history)" })
        {
            Assert.Contains($"\"{required}\"", rulesets, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-CI-005")]
    public void Every_compose_service_that_the_workflow_runs_is_defined_in_the_lint_compose_file()
    {
        var defined = Regex.Matches(Compose, @"^  (?<name>[a-z][a-z-]*):\s*(?:&\S+)?$", RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Select(match => match.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);
        var used = Regex.Matches(AllCommands(), @"compose -f tools/lint/compose\.yaml (?:pull|run --rm(?: -T)?) (?<service>[a-z-]+)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Select(match => match.Groups["service"].Value).Distinct().ToList();

        Assert.Equal(["markdownlint", "links", "secrets", "spelling", "diagrams", "external-links"], used);
        Assert.All(used, service => Assert.Contains(service, defined));
    }

    [Fact]
    [Trait("Requirement", "REQ-SEC-007")]
    public void Every_image_in_the_lint_compose_file_is_pinned_by_tag_and_digest()
    {
        var images = Regex.Matches(Compose, @"^\s+image:\s*(?<image>\S+)\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Select(match => match.Groups["image"].Value).ToList();

        Assert.True(images.Count >= 5, "The compose file should define the lint and documentation images.");
        Assert.All(images, image => Assert.Matches(@"^[a-z0-9./_-]+:[A-Za-z0-9._-]+@sha256:[0-9a-f]{64}$", image));
    }

    [Fact]
    [Trait("Requirement", "REQ-SEC-007")]
    public void The_workflow_holds_no_permission_by_default_and_every_job_only_reads_contents()
    {
        Assert.Empty((JsonObject)Workflow["permissions"]!);

        foreach (var (id, job) in (JsonObject)Workflow["jobs"]!)
        {
            var permissions = (JsonObject)job!["permissions"]!;

            Assert.True(permissions.Count == 1 && (string?)permissions["contents"] == "read", $"The job '{id}' should only read contents.");
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-SEC-007")]
    public void Every_checkout_keeps_no_credentials_and_every_job_has_a_timeout()
    {
        foreach (var (id, job) in (JsonObject)Workflow["jobs"]!)
        {
            Assert.NotNull(job!["timeout-minutes"]);

            foreach (var checkout in ((JsonArray)job["steps"]!).OfType<JsonObject>().Where(step => ((string?)step["uses"] ?? string.Empty).StartsWith("actions/checkout@", StringComparison.Ordinal)))
            {
                Assert.False((bool?)checkout["with"]!["persist-credentials"], $"The checkout in '{id}' should not persist credentials.");
            }
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-CI-005")]
    public void The_site_job_runs_the_metadata_and_the_build_through_the_script_and_keeps_a_preview_for_a_week()
    {
        var job = (JsonObject)Workflow["jobs"]!["docs-site"]!;
        var commands = Run(job);
        var upload = ((JsonArray)job["steps"]!).OfType<JsonObject>().Single(step => ((string?)step["uses"] ?? string.Empty).StartsWith("actions/upload-artifact@", StringComparison.Ordinal));

        Assert.Contains("tools/ci/Invoke-DocFx.ps1 -Command metadata", commands, StringComparison.Ordinal);
        Assert.Contains("tools/ci/Invoke-DocFx.ps1 -Command build", commands, StringComparison.Ordinal);
        Assert.Contains("dotnet restore ProductCatalog.slnx --locked-mode", commands, StringComparison.Ordinal);
        Assert.Equal("docs-preview", (string?)upload["with"]!["name"]);
        Assert.Equal("_site", (string?)upload["with"]!["path"]);
        Assert.Equal(7L, (long?)upload["with"]!["retention-days"]);
    }

    [Fact]
    [Trait("Requirement", "REQ-CI-005")]
    public void The_audit_job_builds_the_auditor_from_its_lock_file_and_writes_the_job_summary()
    {
        var commands = Run((JsonObject)Workflow["jobs"]!["docs-audit"]!);

        Assert.Contains("dotnet restore tools/Documentation.Auditor/Documentation.Auditor.csproj --locked-mode", commands, StringComparison.Ordinal);
        Assert.Contains("documentation.dll audit --summary \"$GITHUB_STEP_SUMMARY\"", commands, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-CI-005")]
    public void The_script_runs_the_build_with_warnings_as_errors_and_the_metadata_step_with_one_named_exception()
    {
        var script = File.ReadAllText(Path.Combine(RealRepository.Root, "tools", "ci", "Invoke-DocFx.ps1"));
        var module = File.ReadAllText(Path.Combine(RealRepository.Root, "tools", "ci", "DocFx.psm1"));

        Assert.Contains("--warningsAsErrors", script, StringComparison.Ordinal);
        Assert.Contains("-eq 'build'", script, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(module, @"return @\('\^No \\\.NET API detected'\)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));
    }

    private static string AllCommands() => string.Join('\n', ((JsonObject)Workflow["jobs"]!).Select(pair => Run((JsonObject)pair.Value!)));

    private static string Run(JsonObject job) =>
        string.Join('\n', ((JsonArray)job["steps"]!).OfType<JsonObject>().Select(step => (string?)step["run"] ?? string.Empty));
}

/// <summary>REQ-DOC-005: the DocFX configuration builds the site with the modern template and does not hide warnings.</summary>
public sealed class DocFxConfigurationTests
{
    private static readonly JsonObject Config = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(RealRepository.Root, "docfx.json")), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip })!;

    [Fact]
    [Trait("Requirement", "REQ-DOC-005")]
    public void The_site_uses_the_modern_template_because_only_it_renders_mermaid_diagrams()
    {
        var templates = ((JsonArray)Config["build"]!["template"]!).Select(node => (string)node!).ToList();

        Assert.Equal(["default", "modern"], templates);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-005")]
    public void The_configuration_does_not_lower_the_severity_of_any_warning()
    {
        Assert.False(Config.ContainsKey("rules"), "A rules entry would hide a warning that the build must treat as an error.");
        Assert.False(((JsonObject)Config["build"]!).ContainsKey("rules"));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-005")]
    public void The_api_metadata_reads_the_projects_under_src_and_leaves_the_restore_to_the_caller()
    {
        var metadata = (JsonObject)((JsonArray)Config["metadata"]!)[0]!;
        var source = (JsonObject)((JsonArray)metadata["src"]!)[0]!;

        Assert.Equal("src", (string?)source["src"]);
        Assert.Contains("**/*.csproj", ((JsonArray)source["files"]!).Select(node => (string)node!));
        Assert.True((bool?)metadata["noRestore"]);
        Assert.Equal(".docfx/api", (string?)metadata["dest"]);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-005")]
    public void The_pages_come_from_docs_and_the_templates_are_resources_so_that_a_link_to_one_resolves()
    {
        var content = ((JsonArray)Config["build"]!["content"]!).OfType<JsonObject>().ToList();
        var pages = content.Single(group => (string?)group["src"] == "docs");
        var resources = ((JsonArray)Config["build"]!["resource"]!).OfType<JsonObject>().ToList();

        Assert.Contains("**/*.md", ((JsonArray)pages["files"]!).Select(node => (string)node!));
        Assert.Contains("templates/**", ((JsonArray)pages["exclude"]!).Select(node => (string)node!));
        Assert.Contains(resources, group => (string?)group["src"] == "docs" && ((JsonArray)group["files"]!).Any(node => (string?)node == "templates/**"));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-005")]
    public void The_resource_globs_name_the_hidden_folders_that_the_catch_all_pattern_skips()
    {
        var resource = ((JsonArray)Config["build"]!["resource"]!).OfType<JsonObject>().First();
        var files = ((JsonArray)resource["files"]!).Select(node => (string)node!).ToList();

        foreach (var hidden in new[] { ".github/**/*", ".config/**/*", ".cspell/**/*", ".*" })
        {
            Assert.Contains(hidden, files);
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-005")]
    public void Git_ignores_the_build_output_and_the_documentation_tool_is_pinned_exactly()
    {
        var ignore = File.ReadAllText(Path.Combine(RealRepository.Root, ".gitignore")).ReplaceLineEndings("\n").Split('\n');
        var tools = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(RealRepository.Root, ".config", "dotnet-tools.json")))!["tools"]!;

        Assert.Contains("_site/", ignore);
        Assert.Contains(".docfx/", ignore);
        Assert.Matches(@"^\d+\.\d+\.\d+$", (string)tools["docfx"]!["version"]!);
        Assert.False((bool)tools["docfx"]!["rollForward"]!);
    }
}
