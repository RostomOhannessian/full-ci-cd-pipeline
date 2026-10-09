using Documentation.Auditor.Markdown;
using Xunit;

namespace Documentation.Auditor.Tests;

/// <summary>The Markdown reader that every page rule stands on. Text in a fence or an inline code span is never a heading or an include.</summary>
public sealed class MarkdownPageTests
{
    [Fact]
    [Trait("Requirement", "REQ-DOC-005")]
    public void Front_matter_headings_fences_and_includes_are_read_with_their_lines()
    {
        var page = MarkdownPage.Parse("docs/a.md", "---\ntitle: \"A\"\nowner: x\n---\n\n# A\n\n## Section\n\n```bash illustrative\necho hi\n```\n\n[!code-bash[Run](~/scripts/run.sh#build)]\n");

        Assert.True(page.HasFrontMatter);
        Assert.Equal("A", (string?)page.FrontMatter!["title"]);
        Assert.Equal([("A", 1, 6), ("Section", 2, 8)], page.Headings.Select(heading => (heading.Text, heading.Level, heading.Line)));

        var fence = Assert.Single(page.Fences);
        Assert.Equal(("bash", 10), (fence.Language, fence.Line));
        Assert.Equal(["bash", "illustrative"], fence.Words);
        Assert.Equal(["echo hi"], fence.Content);

        var include = Assert.Single(page.Includes);
        Assert.Equal((14, true, "~/scripts/run.sh", "build"), (include.Line, include.IsCode, include.Target, include.Region));
    }

    [Theory]
    [Trait("Requirement", "REQ-DOC-005")]
    [InlineData("```text\n# Not a heading\n```\n")]
    [InlineData("~~~\n## Not a heading\n~~~\n")]
    [InlineData("````markdown\n```bash\n# Not a heading\n```\n````\n")]
    public void A_heading_inside_a_fence_is_not_a_heading(string text)
    {
        Assert.Empty(MarkdownPage.Parse("a.md", text).Headings);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-005")]
    public void A_longer_fence_is_closed_only_by_a_fence_that_is_at_least_as_long()
    {
        var page = MarkdownPage.Parse("a.md", "````markdown\n```bash\ninner\n```\nstill inside\n````\n\n## After\n");

        var fence = Assert.Single(page.Fences);
        Assert.Equal(["```bash", "inner", "```", "still inside"], fence.Content);
        Assert.Equal("After", Assert.Single(page.Headings).Text);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-005")]
    public void A_fence_that_is_never_closed_runs_to_the_end_of_the_page()
    {
        var page = MarkdownPage.Parse("a.md", "```bash\ndotnet build\n## inside\n");

        Assert.Equal(["dotnet build", "## inside", string.Empty], Assert.Single(page.Fences).Content);
        Assert.Empty(page.Headings);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-005")]
    public void An_info_string_with_a_backtick_in_a_backtick_fence_is_inline_code_and_not_a_fence()
    {
        var page = MarkdownPage.Parse("a.md", "```not a fence```\n\ntext\n");

        Assert.Empty(page.Fences);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-005")]
    public void A_section_is_the_content_under_a_level_2_heading_without_blank_lines_and_comments()
    {
        var page = MarkdownPage.Parse("a.md", "# T\n\n## First\n\n<!-- a comment\nthat spans two lines -->\n\nText one.\n\n### Child\n\nText two.\n\n## Second\n\n## Third\n\nLast.\n");

        Assert.Equal(["Text one.", "### Child", "Text two."], page.Section("First"));
        Assert.Empty(page.Section("Second")!);
        Assert.Equal(["Last."], page.Section("Third"));
        Assert.Null(page.Section("Missing"));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-005")]
    public void Only_a_level_2_heading_names_a_section()
    {
        var page = MarkdownPage.Parse("a.md", "# Overview\n\ntext\n\n### Overview\n\ntext\n");

        Assert.Null(page.Section("Overview"));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-005")]
    public void A_page_without_front_matter_has_none_and_an_empty_block_is_an_empty_mapping()
    {
        Assert.False(MarkdownPage.Parse("a.md", "# A\n").HasFrontMatter);

        var empty = MarkdownPage.Parse("a.md", "---\n---\n\n# A\n");

        Assert.True(empty.HasFrontMatter);
        Assert.Empty(empty.FrontMatter!);
    }

    [Theory]
    [Trait("Requirement", "REQ-DOC-005")]
    [InlineData("[!code-csharp[Main](Program.cs#Snippet1)]", true, "Program.cs", "Snippet1", null)]
    [InlineData("[!code-csharp[Main](Program.cs?name=Snippet1)]", true, "Program.cs", "Snippet1", null)]
    [InlineData("[!code[Main](Program.cs?range=1-5)]", true, "Program.cs", null, "1-5")]
    [InlineData("[!INCLUDE [Note](note.md)]", false, "note.md", null, null)]
    [InlineData("[!include[Note](../note.md \"title\")]", false, "../note.md", null, null)]
    public void Include_syntax_is_read_in_every_form_that_DocFX_accepts(string text, bool isCode, string target, string? region, string? range)
    {
        var include = Assert.Single(MarkdownPage.Parse("a.md", text).Includes);

        Assert.Equal((isCode, target, region, range), (include.IsCode, include.Target, include.Region, include.Range));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-005")]
    public void An_include_inside_an_inline_code_span_is_not_an_include()
    {
        Assert.Empty(MarkdownPage.Parse("a.md", "Write `[!code-bash[Run](x.sh)]` to include.\n").Includes);
    }

    [Theory]
    [Trait("Requirement", "REQ-DOC-005")]
    [InlineData("docs/tutorials/a.md", "~/scripts/run.sh", "scripts/run.sh")]
    [InlineData("docs/tutorials/a.md", "../../scripts/run.sh", "scripts/run.sh")]
    [InlineData("docs/tutorials/a.md", "./b.md", "docs/tutorials/b.md")]
    [InlineData("a.md", "scripts/run.sh", "scripts/run.sh")]
    [InlineData("docs/a.md", "../../outside.sh", null)]
    [InlineData("a.md", "../outside.sh", null)]
    public void An_include_path_resolves_against_the_page_or_the_root_and_never_leaves_the_repository(string page, string target, string? expected)
    {
        Assert.Equal(expected, Pages.IncludeRules.Resolve(page, target));
    }
}
