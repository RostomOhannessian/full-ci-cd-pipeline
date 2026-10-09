using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Governance.Auditor.BlastRadius;
using Governance.Auditor.Cli;
using Governance.Auditor.Documents;
using Governance.Auditor.Security;
using Governance.Auditor.Tests.Support;
using Xunit;

namespace Governance.Auditor.Tests;

public sealed class GovernanceWorkflowTests
{
    private static readonly JsonObject Workflow = (JsonObject)YamlDocument.Parse(File.ReadAllText(Path.Combine(RealRepository.Root, ".github", "workflows", "governance.yml")), "governance.yml")!;

    [Fact]
    [Trait("Requirement", "REQ-CI-002")]
    public void The_workflow_runs_on_every_pull_request_to_master_and_phase_branches_with_no_path_filter()
    {
        var pullRequest = (JsonObject)Workflow["on"]!["pull_request"]!;
        var branches = ((JsonArray)pullRequest["branches"]!).Select(branch => (string)branch!).ToList();

        Assert.Equal(["master", "phase/**"], branches);
        Assert.False(pullRequest.ContainsKey("paths"), "A path filter would let a pull request skip the auditors.");
        Assert.False(pullRequest.ContainsKey("paths-ignore"), "A path filter would let a pull request skip the auditors.");
        Assert.False(((JsonObject)Workflow["on"]!).ContainsKey("pull_request_target"));
    }

    [Fact]
    [Trait("Requirement", "REQ-CI-002")]
    public void The_workflow_also_runs_on_master_and_by_hand()
    {
        var on = (JsonObject)Workflow["on"]!;

        Assert.Equal("master", (string?)on["push"]!["branches"]![0]);
        Assert.True(on.ContainsKey("workflow_dispatch"));
    }

    [Fact]
    [Trait("Requirement", "REQ-CI-002")]
    public void The_job_runs_every_auditor_and_checks_the_generated_pages()
    {
        var job = (JsonObject)Workflow["jobs"]!["governance"]!;
        var commands = string.Join('\n', ((JsonArray)job["steps"]!).OfType<JsonObject>().Select(step => (string?)step["run"] ?? string.Empty));

        Assert.Equal("Governance auditors", (string?)job["name"]);

        foreach (var expected in new[] { "status validate", "status render --check", "trace --check", "security ", "blast-radius " })
        {
            Assert.Contains(expected, commands, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-CI-002")]
    public void The_security_auditor_gets_the_commit_range_of_the_pull_request_through_environment_variables_only()
    {
        var steps = ((JsonArray)Workflow["jobs"]!["governance"]!["steps"]!).OfType<JsonObject>().ToList();
        var security = steps.Single(step => ((string?)step["run"] ?? string.Empty).Contains("security ", StringComparison.Ordinal));

        Assert.Contains("github.event.pull_request.base.sha", (string?)security["env"]!["BASE_SHA"], StringComparison.Ordinal);
        Assert.Contains("github.event.pull_request.head.sha", (string?)security["env"]!["HEAD_SHA"], StringComparison.Ordinal);
        Assert.DoesNotContain("${{", (string?)security["run"], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-CI-004")]
    public void The_job_publishes_one_output_per_selectable_job_in_the_blast_radius_map()
    {
        var outputs = ((JsonObject)Workflow["jobs"]!["governance"]!["outputs"]!).Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
        var map = BlastRadiusMap.Load(RealRepository.Files);

        foreach (var job in map.Jobs.Keys)
        {
            Assert.Contains($"run-{job}", outputs);
        }

        foreach (var summary in new[] { "layers", "areas", "jobs", "docs", "reviews", "flags" })
        {
            Assert.Contains(summary, outputs);
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-CI-004")]
    public void The_job_has_an_explicit_timeout_and_the_workflow_cancels_superseded_runs()
    {
        var job = (JsonObject)Workflow["jobs"]!["governance"]!;

        Assert.Equal(15L, (long?)job["timeout-minutes"]);
        Assert.True((bool?)Workflow["concurrency"]!["cancel-in-progress"]);
    }

    [Fact]
    [Trait("Requirement", "REQ-SEC-007")]
    public void The_workflow_holds_no_permission_by_default_and_the_job_only_reads_contents()
    {
        var job = (JsonObject)Workflow["jobs"]!["governance"]!;

        Assert.Empty((JsonObject)Workflow["permissions"]!);
        Assert.Equal("read", (string?)job["permissions"]!["contents"]);
        Assert.Single((JsonObject)job["permissions"]!);
    }

    [Fact]
    [Trait("Requirement", "REQ-SEC-002")]
    public void The_checkout_fetches_the_history_the_commit_check_needs_and_keeps_no_credentials()
    {
        var checkout = ((JsonArray)Workflow["jobs"]!["governance"]!["steps"]!).OfType<JsonObject>().First(step => ((string?)step["uses"] ?? string.Empty).StartsWith("actions/checkout@", StringComparison.Ordinal));

        Assert.Equal(0L, (long?)checkout["with"]!["fetch-depth"]);
        Assert.False((bool?)checkout["with"]!["persist-credentials"]);
    }

    [Fact]
    [Trait("Requirement", "REQ-CI-004")]
    public void The_workflow_restores_the_auditor_from_its_lock_file()
    {
        var commands = ((JsonArray)Workflow["jobs"]!["governance"]!["steps"]!).OfType<JsonObject>().Select(step => (string?)step["run"] ?? string.Empty).ToList();

        Assert.Contains(commands, command => command.Contains("dotnet restore tools/Governance.Auditor/Governance.Auditor.csproj --locked-mode", StringComparison.Ordinal));
    }
}

public sealed class RepositoryPolicyTests
{
    private static string Read(string relativePath) => File.ReadAllText(Path.Combine(RealRepository.Root, relativePath)).ReplaceLineEndings("\n");

    [Fact]
    [Trait("Requirement", "REQ-GOV-004")]
    public void The_pull_request_template_carries_the_nine_items_of_the_definition_of_done()
    {
        var template = Read(".github/PULL_REQUEST_TEMPLATE.md");
        var items = Regex.Matches(template, @"^- \[ \] (?<text>.+)$", RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
            .Select(match => match.Groups["text"].Value)
            .Take(9)
            .ToList();

        Assert.Equal(9, items.Count);

        foreach (var expected in new[] { "Required checks pass", "requirement IDs", "Documentation updated", "status.yaml", "CHANGELOG.md", "ADR", "Threat model", "Governance auditors", "Branch pushed" })
        {
            Assert.Contains(items, item => item.Contains(expected, StringComparison.Ordinal));
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-004")]
    public void The_work_package_issue_form_asks_for_every_item_of_the_definition_of_ready()
    {
        var form = Read(".github/ISSUE_TEMPLATE/work-package.yml");

        foreach (var expected in new[] { "Work package ID", "Scope and non-goals", "Acceptance criteria", "Requirement IDs", "Planned tests", "Documentation deliverables", "Size", "Depends on" })
        {
            Assert.Contains($"label: {expected}", form, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-004")]
    public void The_plan_lists_the_same_nine_items_for_done_as_the_pull_request_template()
    {
        var plan = Read("docs/plans/implementation-plan.md");
        var done = plan[plan.IndexOf("**Done:**", StringComparison.Ordinal)..];
        var count = Regex.Count(done[..done.IndexOf("### 5.5", StringComparison.Ordinal)], @"^\d\. ", RegexOptions.Multiline, TimeSpan.FromSeconds(1));

        Assert.Equal(9, count);
    }

    [Theory]
    [Trait("Requirement", "REQ-GOV-007")]
    [InlineData("governance/github/rulesets/master.json")]
    [InlineData("governance/github/rulesets/phase.json")]
    public void Both_rulesets_require_pull_requests_and_forbid_force_pushes(string path)
    {
        var rules = RuleTypes(path);

        Assert.Contains("pull_request", rules);
        Assert.Contains("non_fast_forward", rules);
        Assert.Contains("required_status_checks", rules);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-007")]
    public void The_master_ruleset_also_forbids_deletion_and_requires_code_owner_review()
    {
        var ruleset = JsonNode.Parse(Read("governance/github/rulesets/master.json"))!;
        var pullRequest = ((JsonArray)ruleset["rules"]!).Single(rule => (string?)rule!["type"] == "pull_request")!["parameters"]!;

        Assert.Contains("deletion", RuleTypes("governance/github/rulesets/master.json"));
        Assert.True((bool?)pullRequest["require_code_owner_review"]);
        Assert.Equal("active", (string?)ruleset["enforcement"]);
    }

    [Fact]
    [Trait("Requirement", "REQ-GOV-007")]
    public void The_repository_noreply_identity_in_agents_md_passes_the_commit_identity_policy()
    {
        var match = Regex.Match(Read("AGENTS.md"), @"`(?<email>\d+\+[A-Za-z0-9-]+@users\.noreply\.github\.com)`", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var policy = SecurityPolicy.Load(RealRepository.Files);

        Assert.True(match.Success, "AGENTS.md should name the repository noreply identity.");
        Assert.Contains(policy.CommitIdentity.AllowedEmailPatterns, pattern => SafeRegex.Create(pattern, "policy").IsMatch(match.Groups["email"].Value));
    }

    [Theory]
    [Trait("Requirement", "REQ-GOV-007")]
    [InlineData("jane.doe@gmail.com")]
    [InlineData("jane@example.test")]
    [InlineData("jane@users.noreply.example.test")]
    [InlineData("noreply@example.test")]
    [InlineData("x@github.com")]
    public void A_personal_address_does_not_pass_the_commit_identity_policy(string email)
    {
        var policy = SecurityPolicy.Load(RealRepository.Files);

        Assert.DoesNotContain(policy.CommitIdentity.AllowedEmailPatterns, pattern => SafeRegex.Create(pattern, "policy").IsMatch(email));
    }

    private static IReadOnlyList<string> RuleTypes(string path) =>
        [.. ((JsonArray)JsonNode.Parse(Read(path))!["rules"]!).Select(rule => (string)rule!["type"]!)];
}

public sealed class SkillTests
{
    private static readonly string[] RequiredSections =
    [
        "Purpose", "Inputs", "Allowed tools", "Read and write scope", "Procedure", "Outputs", "Failure behavior", "Prompt-injection controls", "Examples",
    ];

    private static readonly string[] DocumentedFrontMatterKeys = ["name", "description", "license"];

    public static TheoryData<string> SkillNames => new() { "advanced-security-auditor", "blast-radius-evaluator" };

    public static TheoryData<string> AllSkillNames
    {
        get
        {
            var data = new TheoryData<string>();

            foreach (var directory in Directory.EnumerateDirectories(Path.Combine(RealRepository.Root, ".github", "skills")).Order(StringComparer.Ordinal))
            {
                data.Add(Path.GetFileName(directory));
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(AllSkillNames))]
    [Trait("Requirement", "REQ-AI-002")]
    public void Every_skill_declares_only_documented_front_matter_and_names_itself_after_its_folder(string skill)
    {
        var (keys, text) = FrontMatter(skill);

        Assert.All(keys.Keys, key => Assert.Contains(key, DocumentedFrontMatterKeys));
        Assert.Equal(skill, keys["name"]);
        Assert.True(keys["description"].Length > 40, "A description tells the host when to use the skill.");
        Assert.Contains($"# ", text, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(AllSkillNames))]
    [Trait("Requirement", "REQ-AI-002")]
    public void Every_skill_declares_its_purpose_tools_scope_outputs_failure_behavior_and_injection_controls(string skill)
    {
        var headings = Headings(Body(skill));

        foreach (var section in RequiredSections)
        {
            Assert.Contains(section, headings);
        }
    }

    [Theory]
    [MemberData(nameof(SkillNames))]
    [Trait("Requirement", "REQ-AI-002")]
    public void A_cli_backed_skill_runs_a_command_that_exists_and_never_the_write_command(string skill)
    {
        var body = Body(skill);
        var subcommands = GovernanceCli.Build(new CliContext(TextWriter.Null, TextWriter.Null, new FakeProcessRunner(), _ => null)).Subcommands.Select(command => command.Name).ToList();
        var used = Regex.Matches(body, @"dotnet run --project tools/Governance\.Auditor -- (?<command>[a-z-]+)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
            .Select(match => match.Groups["command"].Value)
            .Distinct()
            .ToList();

        Assert.NotEmpty(used);
        Assert.All(used, command => Assert.Contains(command, subcommands));
        Assert.DoesNotContain("--apply", body, StringComparison.Ordinal);
        Assert.DoesNotContain("github-sync", body, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(SkillNames))]
    [Trait("Requirement", "REQ-AI-002")]
    public void A_cli_backed_skill_forbids_git_writes_and_edits_and_treats_inputs_as_untrusted(string skill)
    {
        var sections = Sections(Body(skill));

        Assert.Contains("No Git write commands", sections["Allowed tools"], StringComparison.Ordinal);
        Assert.Contains("no edits", sections["Allowed tools"], StringComparison.Ordinal);
        Assert.Contains("Write: nothing", sections["Read and write scope"], StringComparison.Ordinal);
        Assert.Contains("untrusted", sections["Prompt-injection controls"], StringComparison.Ordinal);
        Assert.Contains("write token", sections["Prompt-injection controls"], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-002")]
    public void The_security_skill_never_auto_fixes_secrets_and_the_blast_radius_skill_never_edits_the_map()
    {
        var security = Body("advanced-security-auditor");
        var blast = Body("blast-radius-evaluator");

        Assert.Contains("Never rewrite history yourself", security, StringComparison.Ordinal);
        Assert.Contains("you must not search for one", security, StringComparison.Ordinal);
        Assert.Contains("Never edit the map", blast, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(SkillNames))]
    [Trait("Requirement", "REQ-AI-002")]
    public void The_governance_skills_are_listed_as_existing_in_the_ai_skills_record(string skill)
    {
        var record = File.ReadAllText(Path.Combine(RealRepository.Root, "docs", "governance", "ai-skills.md")).ReplaceLineEndings("\n");
        var row = record.Split('\n').Single(line => line.Contains($"/{skill}`", StringComparison.Ordinal) && line.StartsWith('|'));

        Assert.Contains($"(../../.github/skills/{skill}/SKILL.md)", row, StringComparison.Ordinal);
        Assert.EndsWith("| exists |", row.TrimEnd(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-002")]
    public async Task The_security_skill_fixture_gives_the_reviewed_output()
    {
        using var repository = SecuritySamples.RepositoryWithPolicy()
            .CopyFromRepository("governance/policies/license-policy.json")
            .CopyFromRepository("governance/policies/license-overrides.json")
            .CopyFromRepository("governance/policies/forbidden-packages.json")
            .Add(".github/workflows/violations.yml", File.ReadAllText(Golden.PathOf("skills/advanced-security-auditor/violations.yml")));

        var result = await CliRunner.RunAsync(repository, "security");

        Assert.Equal(1, result.ExitCode);
        Golden.AssertMatches(result.Output, "skills/advanced-security-auditor/expected-output.txt");
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-002")]
    public async Task The_blast_radius_skill_fixture_gives_the_reviewed_output()
    {
        using var repository = new TestRepository().CopyFromRepository(BlastRadiusMap.Path);
        var paths = Golden.Read("skills/blast-radius-evaluator/changed-paths.txt").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var result = await CliRunner.RunAsync(repository, ["blast-radius", .. paths.SelectMany(path => new[] { "--path", path })]);

        Assert.Equal(0, result.ExitCode);
        Golden.AssertMatches(result.Output, "skills/blast-radius-evaluator/expected-output.txt");
    }

    private static string Body(string skill) => FrontMatter(skill).Text;

    private static (Dictionary<string, string> Keys, string Text) FrontMatter(string skill)
    {
        var text = File.ReadAllText(Path.Combine(RealRepository.Root, ".github", "skills", skill, "SKILL.md")).ReplaceLineEndings("\n");
        Assert.StartsWith("---\n", text, StringComparison.Ordinal);

        var end = text.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        var keys = text[4..end].Split('\n').Select(line => line.Split(':', 2)).ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim(), StringComparer.Ordinal);

        return (keys, text[(end + 5)..]);
    }

    private static List<string> Headings(string body) =>
        [.. body.Split('\n').Where(line => line.StartsWith("## ", StringComparison.Ordinal)).Select(line => line[3..].Trim())];

    private static Dictionary<string, string> Sections(string body)
    {
        Dictionary<string, string> sections = new(StringComparer.Ordinal);
        string? current = null;

        foreach (var line in body.Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                current = line[3..].Trim();
                sections[current] = string.Empty;
            }
            else if (current is not null)
            {
                sections[current] += line + "\n";
            }
        }

        return sections;
    }
}
