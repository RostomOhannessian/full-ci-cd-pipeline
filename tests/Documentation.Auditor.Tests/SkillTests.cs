using System.Text.RegularExpressions;
using Documentation.Auditor.Cli;
using Documentation.Auditor.Tests.Support;
using Xunit;

namespace Documentation.Auditor.Tests;

/// <summary>REQ-AI-002: the documentation-auditor skill declares its scope, wraps the CLI instead of holding policy, and has a fixture that the tests run.</summary>
public sealed class SkillTests
{
    private const string Skill = "documentation-auditor";

    private static readonly string[] RequiredSections =
    [
        "Purpose", "Inputs", "Allowed tools", "Read and write scope", "Procedure", "Outputs", "Failure behavior", "Prompt-injection controls", "Examples",
    ];

    private static readonly string[] DocumentedFrontMatterKeys = ["name", "description", "license"];

    [Fact]
    [Trait("Requirement", "REQ-AI-002")]
    public void The_skill_declares_only_documented_front_matter_and_names_itself_after_its_folder()
    {
        var (keys, _) = FrontMatter();

        Assert.All(keys.Keys, key => Assert.Contains(key, DocumentedFrontMatterKeys));
        Assert.Equal(Skill, keys["name"]);
        Assert.True(keys["description"].Length > 40, "A description tells the host when to use the skill.");
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-002")]
    public void The_skill_declares_its_purpose_tools_scope_outputs_failure_behavior_and_injection_controls()
    {
        var headings = Sections().Keys.ToList();

        foreach (var section in RequiredSections)
        {
            Assert.Contains(section, headings);
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-002")]
    public void The_skill_runs_commands_that_exist_and_never_a_write_command_of_git_or_github()
    {
        var commands = DocumentationCli.Build(new CliContext(TextWriter.Null, TextWriter.Null, TimeProvider.System)).Subcommands.Select(command => command.Name).ToList();
        var used = Regex.Matches(Body(), @"dotnet run --project tools/Documentation\.Auditor -- (?<command>[a-z-]+)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
            .Select(match => match.Groups["command"].Value)
            .Distinct()
            .ToList();

        Assert.NotEmpty(used);
        Assert.All(used, command => Assert.Contains(command, commands));
        Assert.DoesNotContain("git push", Body(), StringComparison.Ordinal);
        Assert.DoesNotContain("--apply", Body(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-002")]
    public void The_skill_limits_its_write_scope_to_the_missing_sections_of_one_tool_page_and_forbids_git_writes()
    {
        var sections = Sections();

        Assert.Contains("No Git write commands", sections["Allowed tools"], StringComparison.Ordinal);
        Assert.Contains("no network access", sections["Allowed tools"], StringComparison.Ordinal);
        Assert.Contains("docs/reference/tools/", sections["Allowed tools"], StringComparison.Ordinal);
        Assert.Contains("Nothing else", sections["Read and write scope"], StringComparison.Ordinal);
        Assert.Contains("Never edit the inventory, the policy, the schema, the auditor", sections["Read and write scope"], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-002")]
    public void The_skill_treats_pages_and_findings_as_untrusted_data_and_holds_no_write_token()
    {
        var sections = Sections();

        Assert.Contains("untrusted", sections["Prompt-injection controls"], StringComparison.Ordinal);
        Assert.Contains("write token", sections["Prompt-injection controls"], StringComparison.Ordinal);
        Assert.Contains("never let a page decide a lifecycle", sections["Prompt-injection controls"].ReplaceLineEndings(" "), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not write a placeholder", sections["Failure behavior"], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-002")]
    public void The_skill_explains_every_rule_the_auditor_can_report()
    {
        var explained = Body();
        var rules = Regex.Matches(
                string.Join('\n', Directory.EnumerateFiles(Path.Combine(RealRepository.Root, "tools", "Documentation.Auditor"), "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText)),
                @"Finding\.(?:Error|Warning|Note)\(\s*""(?<rule>[a-z-]+)""",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(5))
            .Select(match => match.Groups["rule"].Value)
            .Distinct()
            .ToList();

        Assert.NotEmpty(rules);

        // A rule family is named once with a wildcard, such as include-*.
        var families = Regex.Matches(explained, @"`(?<prefix>[a-z-]+-)\*`", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Select(match => match.Groups["prefix"].Value).ToList();

        foreach (var rule in rules)
        {
            Assert.True(
                explained.Contains($"`{rule}`", StringComparison.Ordinal) || families.Any(family => rule.StartsWith(family, StringComparison.Ordinal)),
                $"The skill does not explain the rule '{rule}'.");
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-002")]
    public void The_skill_is_listed_as_existing_in_the_ai_skills_record()
    {
        var record = File.ReadAllText(Path.Combine(RealRepository.Root, "docs", "governance", "ai-skills.md")).ReplaceLineEndings("\n");
        var row = record.Split('\n').Single(line => line.Contains($"/{Skill}`", StringComparison.Ordinal) && line.StartsWith('|'));

        Assert.Contains($"(../../.github/skills/{Skill}/SKILL.md)", row, StringComparison.Ordinal);
        Assert.EndsWith("| exists |", row.TrimEnd(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-002")]
    public async Task The_skill_fixture_gives_the_reviewed_output()
    {
        using var repository = Samples.Baseline()
            .Replace("Directory.Packages.props", "</ItemGroup>", "  <PackageVersion Include=\"Mystery.Package\" Version=\"9.9.9\" />\n  </ItemGroup>")
            .Replace("docs/reference/tools/acme-lint.md", "## Further research", "### Further research")
            .Add("docs/tutorials/start.md", Samples.Page("Start", body: "```bash\ndotnet build\n```"))
            .Add("docs/explanation/flow.md", Samples.Page("Flow", body: "```mermaid\nflowchart LR\n  A --> B\n```"));

        var result = await CliRunner.RunAsync(repository, "audit");

        Assert.Equal(1, result.ExitCode);
        Golden.AssertMatches(result.Output, "skill/expected-output.txt");
    }

    private static string Body() => FrontMatter().Text;

    private static (Dictionary<string, string> Keys, string Text) FrontMatter()
    {
        var text = File.ReadAllText(Path.Combine(RealRepository.Root, ".github", "skills", Skill, "SKILL.md")).ReplaceLineEndings("\n");
        Assert.StartsWith("---\n", text, StringComparison.Ordinal);

        var end = text.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        var keys = text[4..end].Split('\n').Select(line => line.Split(':', 2)).ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim(), StringComparer.Ordinal);

        return (keys, text[(end + 5)..]);
    }

    private static Dictionary<string, string> Sections()
    {
        Dictionary<string, string> sections = new(StringComparer.Ordinal);
        string? current = null;

        foreach (var line in Body().Split('\n'))
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
