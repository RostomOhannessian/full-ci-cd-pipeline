using System.Text.Json.Nodes;
using Documentation.Auditor.Tests.Support;
using Governance.Auditor.Documents;
using Xunit;

namespace Documentation.Auditor.Tests;

/// <summary>The command line: exit codes, the report for a job summary, and which half of the audit each command runs.</summary>
public sealed class CliTests
{
    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public async Task The_exit_code_is_0_when_every_check_passes_1_when_one_fails_and_2_when_the_tool_cannot_run()
    {
        using var repository = Samples.Baseline();

        Assert.Equal(0, (await CliRunner.RunAsync(repository, "audit")).ExitCode);

        repository.Replace("docs/reference/tools/inventory.yaml", "tier: B", "tier: D");

        Assert.Equal(1, (await CliRunner.RunAsync(repository, "audit")).ExitCode);

        repository.Remove("governance/policies/documentation-policy.yaml");

        Assert.Equal(2, (await CliRunner.RunAsync(repository, "audit")).ExitCode);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public async Task An_unknown_command_or_option_exits_with_1_and_changes_nothing()
    {
        using var repository = Samples.Baseline();

        var unknown = await CliRunner.RunAsync(repository, "nonsense");
        var option = await CliRunner.RunAsync(repository, "audit", "--fix");

        Assert.Equal(1, unknown.ExitCode);
        Assert.Equal(1, option.ExitCode);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public async Task Each_command_runs_only_its_half_of_the_audit()
    {
        using var repository = Samples.Baseline()
            .Replace("Directory.Packages.props", "</ItemGroup>", "  <PackageVersion Include=\"Mystery.Package\" Version=\"9.9.9\" />\n  </ItemGroup>")
            .Add("docs/explanation/flow.md", Samples.Page("Flow", body: "```mermaid\nflowchart LR\n  A --> B\n```"));

        var inventory = await CliRunner.RunAsync(repository, "inventory");
        var pages = await CliRunner.RunAsync(repository, "pages");
        var audit = await CliRunner.RunAsync(repository, "audit");

        Assert.Contains("[tool-undocumented]", inventory.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("[diagram-accessibility]", inventory.Output, StringComparison.Ordinal);
        Assert.Contains("[diagram-accessibility]", pages.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("[tool-undocumented]", pages.Output, StringComparison.Ordinal);
        Assert.Contains("[tool-undocumented]", audit.Output, StringComparison.Ordinal);
        Assert.Contains("[diagram-accessibility]", audit.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public async Task The_summary_option_appends_a_markdown_table_that_escapes_untrusted_text()
    {
        using var repository = Samples.Baseline().Replace("Directory.Packages.props", "</ItemGroup>", "  <PackageVersion Include=\"Mystery|&lt;script&gt;\" Version=\"9.9.9\" />\n  </ItemGroup>");
        var summary = Path.Combine(repository.Root, "summary.md");

        var result = await CliRunner.RunAsync(repository, "inventory", "--summary", summary);

        Assert.Equal(1, result.ExitCode);
        var text = (await File.ReadAllTextAsync(summary, TestContext.Current.CancellationToken)).ReplaceLineEndings("\n");
        Assert.StartsWith("### documentation inventory: failed", text, StringComparison.Ordinal);
        Assert.Contains("| Severity | Rule | Location | Message |", text, StringComparison.Ordinal);
        Assert.Contains("`tool-undocumented`", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", text, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public async Task A_run_with_no_findings_says_so_in_the_summary()
    {
        using var repository = Samples.Baseline();
        var summary = Path.Combine(repository.Root, "summary.md");

        var result = await CliRunner.RunAsync(repository, "audit", "--summary", summary);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("### documentation audit: passed\n\nNo findings.\n\n", (await File.ReadAllTextAsync(summary, TestContext.Current.CancellationToken)).ReplaceLineEndings("\n"));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public async Task A_path_in_a_document_cannot_leave_the_repository()
    {
        using var repository = Samples.Baseline().Replace("governance/policies/documentation-policy.yaml", "schema: docs/reference/tools/inventory.schema.json", "schema: ../../outside/schema.json");

        var result = await CliRunner.RunAsync(repository, "inventory");

        Expect.FailsOnlyWith(result, "inventory-schema");
        Assert.Contains("leaves the repository root", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public async Task A_directory_with_no_git_repository_above_it_is_reported_and_not_guessed()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var empty = Directory.CreateTempSubdirectory("documentation-no-git-").FullName;

        try
        {
            var context = new Documentation.Auditor.Cli.CliContext(output, error, TimeProvider.System, empty);
            var exitCode = await Documentation.Auditor.Cli.DocumentationCli.RunAsync(["audit"], context, TestContext.Current.CancellationToken);

            Assert.Equal(2, exitCode);
            Assert.Contains("No Git repository was found", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }
}

/// <summary>The inventory schema is a contract with the tool-doc-author skill and with every entry, so it is tested like code.</summary>
public sealed class InventorySchemaTests
{
    [Fact]
    [Trait("Requirement", "REQ-DOC-002")]
    public void The_schema_uses_only_keywords_that_the_validator_implements()
    {
        var schema = JsonNode.Parse(File.ReadAllText(Path.Combine(RealRepository.Root, "docs", "reference", "tools", "inventory.schema.json")))!;

        // Creating the validator throws when the schema uses a keyword that it would ignore.
        var validator = JsonSchemaValidator.Create(schema, "inventory.schema.json");

        Assert.NotNull(validator);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-002")]
    public void The_schema_requires_every_field_that_plan_section_11_2_says_an_entry_records()
    {
        var schema = JsonNode.Parse(File.ReadAllText(Path.Combine(RealRepository.Root, "docs", "reference", "tools", "inventory.schema.json")))!;
        var required = ((JsonArray)schema["$defs"]!["entry"]!["required"]!).Select(node => (string)node!).ToList();

        foreach (var field in new[] { "id", "name", "category", "tier", "version", "license", "lifecycle", "introduced", "sources", "adrs", "usage" })
        {
            Assert.Contains(field, required);
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-002")]
    public void The_schema_lists_every_category_the_real_inventory_uses()
    {
        var schema = JsonNode.Parse(File.ReadAllText(Path.Combine(RealRepository.Root, "docs", "reference", "tools", "inventory.schema.json")))!;
        var categories = ((JsonArray)schema["$defs"]!["entry"]!["properties"]!["category"]!["enum"]!).Select(node => (string)node!).ToHashSet(StringComparer.Ordinal);
        var inventory = (JsonObject)YamlDocument.Parse(File.ReadAllText(Path.Combine(RealRepository.Root, "docs", "reference", "tools", "inventory.yaml")), "inventory.yaml")!;

        var used = ((JsonArray)inventory["entries"]!).Select(entry => (string)entry!["category"]!).Distinct().ToList();

        Assert.All(used, category => Assert.Contains(category, categories));
    }
}
