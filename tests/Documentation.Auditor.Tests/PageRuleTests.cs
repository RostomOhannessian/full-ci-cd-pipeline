using Documentation.Auditor.Tests.Support;
using Xunit;

namespace Documentation.Auditor.Tests;

/// <summary>REQ-DOC-005: pages declare their front matter, are flagged when stale, and use tested commands, resolvable includes, and accessible diagrams.</summary>
public sealed class FrontMatterTests
{
    [Theory]
    [Trait("Requirement", "REQ-DOC-005")]
    [InlineData("title")]
    [InlineData("description")]
    [InlineData("audience")]
    [InlineData("last-verified")]
    [InlineData("owner")]
    public async Task A_page_that_lacks_a_required_key_fails_and_names_the_key(string key)
    {
        var lines = Samples.Page().Split('\n').Where(line => !line.StartsWith(key + ":", StringComparison.Ordinal));
        using var repository = Samples.Baseline().Add("docs/guide.md", string.Join('\n', lines));

        var result = await CliRunner.RunAsync(repository, "pages");

        Expect.FailsOnlyWith(result, "front-matter-key");
        Assert.Contains($"'{key}'", result.Output, StringComparison.Ordinal);
        Assert.Contains("docs/guide.md:1", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-005")]
    public async Task A_page_with_an_empty_value_fails_like_a_missing_one()
    {
        using var repository = Samples.Baseline().Add("docs/guide.md", Samples.Page().Replace("audience: [contributors]", "audience: []", StringComparison.Ordinal));

        var result = await CliRunner.RunAsync(repository, "pages");

        Expect.FailsOnlyWith(result, "front-matter-key");
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-005")]
    public async Task A_page_with_no_front_matter_fails()
    {
        using var repository = Samples.Baseline().Add("docs/guide.md", "# Guide\n\nText.\n");

        var result = await CliRunner.RunAsync(repository, "pages");

        Expect.FailsOnlyWith(result, "front-matter-missing");
    }

    [Theory]
    [Trait("Requirement", "REQ-DOC-005")]
    [InlineData("---\ntitle: \"Unclosed\"\n\n# Guide\n")]
    [InlineData("---\n- just\n- a list\n---\n\n# Guide\n")]
    [InlineData("---\ntitle: [broken\n---\n\n# Guide\n")]
    [InlineData("---\ntitle: &anchor value\nother: *anchor\n---\n\n# Guide\n")]
    public async Task Front_matter_that_cannot_be_read_fails_instead_of_passing_unchecked(string page)
    {
        using var repository = Samples.Baseline().Add("docs/guide.md", page);

        var result = await CliRunner.RunAsync(repository, "pages");

        Expect.FailsOnlyWith(result, "front-matter-invalid");
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-005")]
    public async Task An_ADR_may_leave_its_lists_empty_but_must_declare_them()
    {
        using var repository = Samples.Baseline();

        Expect.Passes(await CliRunner.RunAsync(repository, "pages"));

        repository.Replace("docs/adr/0001-a-decision.md", "superseded-by: []\n", string.Empty);

        var result = await CliRunner.RunAsync(repository, "pages");

        Expect.FailsOnlyWith(result, "front-matter-key");
        Assert.Contains("'superseded-by'", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-005")]
    public async Task A_page_outside_every_rule_and_a_template_are_not_checked()
    {
        using var repository = Samples.Baseline()
            .Add("README.md", "# No front matter at the root\n")
            .Add("docs/templates/page-template.md", "# {{Title}}\n\n```bash\ndotnet build\n```\n");

        Expect.Passes(await CliRunner.RunAsync(repository, "pages"));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-005")]
    public async Task A_tools_list_that_names_an_unknown_inventory_ID_fails_on_any_page()
    {
        using var repository = Samples.Baseline().Replace("docs/adr/0001-a-decision.md", "tools: [acme-lint]", "tools: [acme-lint, acme-lnt]");

        var result = await CliRunner.RunAsync(repository, "pages");

        Expect.FailsOnlyWith(result, "front-matter-tools");
        Assert.Contains("'acme-lnt'", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Requirement", "REQ-DOC-005")]
    [InlineData("2026-04-11", false)]
    [InlineData("2026-04-10", true)]
    [InlineData("2025-01-01", true)]
    [InlineData("2026-10-08", false)]
    public async Task A_page_is_flagged_for_review_more_than_180_days_after_last_verified(string verified, bool stale)
    {
        using var repository = Samples.Baseline().Add("docs/guide.md", Samples.Page(verified: verified));

        var result = await CliRunner.RunAsync(repository, "pages");

        Expect.Passes(result);
        Assert.Equal(stale, Expect.Warnings(result).Any(line => line.Contains("[page-stale]", StringComparison.Ordinal) && line.Contains("docs/guide.md", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-005")]
    public async Task A_date_that_is_not_a_date_fails_and_a_future_date_is_a_warning()
    {
        using var repository = Samples.Baseline().Add("docs/guide.md", Samples.Page(verified: "yesterday"));

        var invalid = await CliRunner.RunAsync(repository, "pages");

        Expect.FailsOnlyWith(invalid, "front-matter-date");

        repository.Add("docs/guide.md", Samples.Page(verified: "2027-01-01"));

        var future = await CliRunner.RunAsync(repository, "pages");

        Expect.Passes(future);
        Assert.Contains("warning [front-matter-date]", future.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-005")]
    public async Task The_today_option_makes_the_freshness_rule_repeatable()
    {
        using var repository = Samples.Baseline();

        var later = await CliRunner.RunAsync(repository, "pages", "--today", "2027-10-01");
        var invalid = await CliRunner.RunAsync(repository, "pages", "--today", "tomorrow");

        Assert.Contains("[page-stale]", later.Output, StringComparison.Ordinal);
        Assert.Equal(2, invalid.ExitCode);
        Assert.Contains("--today must be a date", invalid.Error, StringComparison.Ordinal);
    }
}

public sealed class CommandBlockTests
{
    private const string Tutorial = "docs/tutorials/start.md";

    private static string PageWith(string block) => Samples.Page("Start", body: block);

    [Theory]
    [Trait("Requirement", "REQ-DOC-005")]
    [InlineData("```bash\ndotnet build\n```")]
    [InlineData("```sh\necho hi\n```")]
    [InlineData("```powershell\nGet-ChildItem\n```")]
    [InlineData("```pwsh\nGet-ChildItem\n```")]
    [InlineData("```text\ndotnet build\n```")]
    [InlineData("```\ndocker compose up\n```")]
    [InlineData("```text\n$ git status\n```")]
    [InlineData("```text\n# restore first\ndotnet restore\n```")]
    [InlineData("```text\n./scripts/run.sh\n```")]
    [InlineData("~~~bash\ndotnet build\n~~~")]
    public async Task A_hand_written_command_block_that_is_not_marked_fails(string block)
    {
        using var repository = Samples.Baseline().Add(Tutorial, PageWith(block));

        var result = await CliRunner.RunAsync(repository, "pages");

        Expect.FailsOnlyWith(result, "command-block-untested");
        Assert.Contains("illustrative", result.Output, StringComparison.Ordinal);
        Assert.Contains($"{Tutorial}:", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Requirement", "REQ-DOC-005")]
    [InlineData("```bash illustrative\ndotnet build\n```")]
    [InlineData("```BASH Illustrative\ndotnet build\n```")]
    [InlineData("```text illustrative\ndocker compose up\n```")]
    [InlineData("```powershell illustrative highlight=1\nGet-ChildItem\n```")]
    public async Task A_block_marked_illustrative_passes(string block)
    {
        using var repository = Samples.Baseline().Add(Tutorial, PageWith(block));

        Expect.Passes(await CliRunner.RunAsync(repository, "pages"));
    }

    [Theory]
    [Trait("Requirement", "REQ-DOC-005")]
    [InlineData("```text\nExpected output:\nBuild succeeded.\n```")]
    [InlineData("```yaml\nimage: nginx\n```")]
    [InlineData("```json\n{ \"a\": 1 }\n```")]
    [InlineData("```csharp\nConsole.WriteLine();\n```")]
    [InlineData("```text\nSuccess: dotnet build finished\n```")]
    [InlineData("```text\n\n```")]
    public async Task Output_configuration_and_code_blocks_are_not_commands(string block)
    {
        using var repository = Samples.Baseline().Add(Tutorial, PageWith(block));

        Expect.Passes(await CliRunner.RunAsync(repository, "pages"));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-005")]
    public async Task A_command_that_comes_from_a_tested_file_through_an_include_needs_no_marker()
    {
        using var repository = Samples.Baseline()
            .Add("scripts/start.sh", "# <start>\ndotnet build\n# </start>\n")
            .Add(Tutorial, PageWith("[!code-bash[Start](~/scripts/start.sh#start)]"));

        Expect.Passes(await CliRunner.RunAsync(repository, "pages"));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-005")]
    public async Task A_record_that_quotes_commands_as_history_is_not_in_scope()
    {
        using var repository = Samples.Baseline().Replace("docs/adr/0001-a-decision.md", "Text.", "```bash\ngit push\n```");

        Expect.Passes(await CliRunner.RunAsync(repository, "pages"));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-005")]
    public async Task Each_unmarked_block_is_reported_at_its_own_line()
    {
        using var repository = Samples.Baseline().Add(Tutorial, PageWith("```bash\none\n```\n\n```bash illustrative\ntwo\n```\n\n```bash\nthree\n```"));

        var result = await CliRunner.RunAsync(repository, "pages");

        var errors = Expect.Errors(result);
        Assert.Equal(2, errors.Count);
        Assert.Contains($"{Tutorial}:11:", errors[0] + errors[1], StringComparison.Ordinal);
        Assert.Contains($"{Tutorial}:19:", errors[0] + errors[1], StringComparison.Ordinal);
    }
}

public sealed class IncludeTests
{
    private const string Page = "docs/tutorials/start.md";

    private static string PageWith(string include) => Samples.Page("Start", body: include);

    [Theory]
    [Trait("Requirement", "REQ-DOC-005")]
    [InlineData("[!code-bash[Run](~/scripts/run.sh)]")]
    [InlineData("[!code-bash[Run](~/scripts/run.sh#build)]")]
    [InlineData("[!code-bash[Run](~/scripts/run.sh#region-one)]")]
    [InlineData("[!code-bash[Run](~/scripts/run.sh?name=build)]")]
    [InlineData("[!code-bash[Run](../../scripts/run.sh#build \"The build step\")]")]
    [InlineData("[!CODE-BASH[Run](~/scripts/run.sh#build)]")]
    [InlineData("[!code[Run](~/scripts/run.sh?range=1-3,5)]")]
    [InlineData("[!code-bash[Run](~/scripts/run.sh?range=2-)]")]
    [InlineData("[!code-bash[Run](~/scripts/run.sh?range=-4)]")]
    [InlineData("[!CODE-bash [Run](~/scripts/run.sh#build)]")]
    public async Task An_include_that_resolves_to_a_tested_file_region_and_range_passes(string include)
    {
        using var repository = Samples.Baseline()
            .Add("scripts/run.sh", "# <build>\ndotnet build\n# </build>\n#region region-one\necho one\n#endregion\n")
            .Add(Page, PageWith(include));

        Expect.Passes(await CliRunner.RunAsync(repository, "pages"));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-005")]
    public async Task An_include_of_a_file_that_does_not_exist_fails()
    {
        using var repository = Samples.Baseline().Add(Page, PageWith("[!code-bash[Run](~/scripts/missing.sh)]"));

        var result = await CliRunner.RunAsync(repository, "pages");

        Expect.FailsOnlyWith(result, "include-target-missing");
        Assert.Contains("scripts/missing.sh", result.Output, StringComparison.Ordinal);
        Assert.Contains($"{Page}:11", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-005")]
    public async Task An_include_that_leaves_the_repository_fails()
    {
        using var repository = Samples.Baseline().Add(Page, PageWith("[!code-bash[Run](../../../../outside.sh)]"));

        var result = await CliRunner.RunAsync(repository, "pages");

        Expect.FailsOnlyWith(result, "include-outside-repository");
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-005")]
    public async Task A_code_include_from_a_place_that_CI_does_not_run_fails()
    {
        using var repository = Samples.Baseline().Add("notes/draft.sh", "echo hi\n").Add(Page, PageWith("[!code-bash[Run](~/notes/draft.sh)]"));

        var result = await CliRunner.RunAsync(repository, "pages");

        Expect.FailsOnlyWith(result, "include-untested-source");
        Assert.Contains("notes/draft.sh", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-005")]
    public async Task A_region_that_the_file_does_not_have_fails()
    {
        using var repository = Samples.Baseline().Add("scripts/run.sh", "# <build>\ndotnet build\n# </build>\n").Add(Page, PageWith("[!code-bash[Run](~/scripts/run.sh#publish)]"));

        var result = await CliRunner.RunAsync(repository, "pages");

        Expect.FailsOnlyWith(result, "include-region-missing");
        Assert.Contains("'publish'", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Requirement", "REQ-DOC-005")]
    [InlineData("?range=1-99")]
    [InlineData("?range=0-2")]
    [InlineData("?range=5-2")]
    [InlineData("?range=abc")]
    [InlineData("?range=99999999999")]
    public async Task A_line_range_that_does_not_fit_the_file_fails(string query)
    {
        using var repository = Samples.Baseline().Add("scripts/run.sh", "one\ntwo\nthree\n").Add(Page, PageWith($"[!code-bash[Run](~/scripts/run.sh{query})]"));

        var result = await CliRunner.RunAsync(repository, "pages");

        Expect.FailsOnlyWith(result, "include-range");
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-005")]
    public async Task A_markdown_include_must_exist_but_may_come_from_anywhere_in_the_repository()
    {
        using var repository = Samples.Baseline().Add("docs/includes/note.md", Samples.Page("Note")).Add(Page, PageWith("[!INCLUDE [Note](../includes/note.md)]"));

        Expect.Passes(await CliRunner.RunAsync(repository, "pages"));

        repository.Add(Page, PageWith("[!include[Note](../includes/gone.md)]"));

        Expect.FailsOnlyWith(await CliRunner.RunAsync(repository, "pages"), "include-target-missing");
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-005")]
    public async Task Include_syntax_inside_a_code_fence_or_an_inline_code_span_is_text_and_not_an_include()
    {
        using var repository = Samples.Baseline().Add(Page, PageWith("Write `[!code-bash[Run](~/scripts/missing.sh)]` to include a file.\n\n```markdown\n[!code-bash[Run](~/scripts/missing.sh)]\n```"));

        Expect.Passes(await CliRunner.RunAsync(repository, "pages"));
    }
}

public sealed class DiagramTests
{
    private const string Page = "docs/explanation/flow.md";

    [Fact]
    [Trait("Requirement", "REQ-DOC-005")]
    public async Task A_diagram_with_a_title_and_a_description_passes()
    {
        using var repository = Samples.Baseline().Add(Page, Samples.Page("Flow", body: "```mermaid\nflowchart LR\n  accTitle: Request flow\n  accDescr: A request goes from the client to the API.\n  A --> B\n```"));

        Expect.Passes(await CliRunner.RunAsync(repository, "pages"));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-005")]
    public async Task A_description_may_use_the_block_form()
    {
        using var repository = Samples.Baseline().Add(Page, Samples.Page("Flow", body: "```mermaid\nsequenceDiagram\n  accTitle: Login\n  accDescr {\n    The user signs in.\n  }\n  A->>B: hi\n```"));

        Expect.Passes(await CliRunner.RunAsync(repository, "pages"));
    }

    [Theory]
    [Trait("Requirement", "REQ-DOC-005")]
    [InlineData("flowchart LR\n  A --> B", 2)]
    [InlineData("flowchart LR\n  accTitle: Only a title\n  A --> B", 1)]
    [InlineData("flowchart LR\n  accDescr: Only a description\n  A --> B", 1)]
    [InlineData("flowchart LR\n  accTitle:\n  accDescr:\n  A --> B", 2)]
    public async Task A_diagram_without_a_title_or_a_description_fails_for_each_missing_part(string diagram, int missing)
    {
        using var repository = Samples.Baseline().Add(Page, Samples.Page("Flow", body: $"```mermaid\n{diagram}\n```"));

        var result = await CliRunner.RunAsync(repository, "pages");

        Expect.FailsOnlyWith(result, "diagram-accessibility");
        Assert.Equal(missing, Expect.Errors(result).Count);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-005")]
    public async Task A_diagram_in_a_page_outside_docs_is_checked_too()
    {
        using var repository = Samples.Baseline().Add("README.md", "# Readme\n\n```mermaid\nflowchart LR\n  A --> B\n```\n");

        var result = await CliRunner.RunAsync(repository, "pages");

        Expect.FailsOnlyWith(result, "diagram-accessibility");
        Assert.Contains("README.md:3", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-005")]
    public async Task A_fence_in_another_language_is_not_a_diagram()
    {
        using var repository = Samples.Baseline().Add(Page, Samples.Page("Flow", body: "```text\nflowchart LR\n  A --> B\n```"));

        Expect.Passes(await CliRunner.RunAsync(repository, "pages"));
    }
}
