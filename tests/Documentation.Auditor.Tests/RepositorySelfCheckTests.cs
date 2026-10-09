using Documentation.Auditor.Discovery;
using Documentation.Auditor.Policy;
using Documentation.Auditor.Tests.Support;
using Governance.Auditor.Common;
using Xunit;

namespace Documentation.Auditor.Tests;

/// <summary>
/// Runs the documentation commands over this repository, as the <c>docs</c> workflow does. A change that leaves a tool out of the inventory,
/// drops a page, breaks an include, or leaves a diagram without a description fails here before it fails in CI.
/// </summary>
public sealed class RepositorySelfCheckTests
{
    [Fact]
    [Trait("Requirement", "REQ-DOC-002")]
    public async Task The_real_inventory_matches_what_the_repository_uses()
    {
        var result = await CliRunner.RunOnRealRepositoryAsync("inventory");

        Assert.True(result.ExitCode == 0, "Run 'dotnet run --project tools/Documentation.Auditor -- inventory', read the findings, and fix the inventory or the file.\n" + result.Output + result.Error);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-005")]
    public async Task Every_real_page_has_its_front_matter_resolvable_includes_tested_commands_and_accessible_diagrams()
    {
        var result = await CliRunner.RunOnRealRepositoryAsync("pages");

        Assert.True(result.ExitCode == 0, result.Output + result.Error);
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public async Task The_full_audit_of_the_real_repository_passes_and_reports_the_owed_pages_as_notes()
    {
        var result = await CliRunner.RunOnRealRepositoryAsync("audit");

        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        Assert.Empty(Expect.Errors(result));
        Assert.All(Expect.Notes(result), note => Assert.Contains("[tool-page-owed]", note, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public void The_scanners_see_the_real_repository_so_a_pass_is_not_an_empty_scan()
    {
        // A scanner that finds nothing would make every discovery check pass without checking anything.
        var files = RealRepository.Files;
        List<Finding> findings = [];
        var policy = DocumentationPolicy.Load(files);

        var tools = Scanners.ScanAll(files, [.. files.ListFiles().Where(path => !Glob.IsMatchAny(policy.Discovery.ExcludePaths, path))], findings);

        Assert.Empty(findings);
        Assert.Contains(tools, tool => tool is { Kind: DiscoveryKinds.Nuget, Identifier: "xunit.v3" });
        Assert.Contains(tools, tool => tool is { Kind: DiscoveryKinds.DotnetTool, Identifier: "docfx" });
        Assert.Contains(tools, tool => tool is { Kind: DiscoveryKinds.Action, Identifier: "actions/checkout" });
        Assert.Contains(tools, tool => tool.Kind == DiscoveryKinds.Image && tool.Identifier.EndsWith("/cspell", StringComparison.Ordinal));
        Assert.Contains(tools, tool => tool.Kind == DiscoveryKinds.Image && tool.Identifier.EndsWith("lycheeverse/lychee", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-004")]
    public void Every_tool_page_that_exists_is_for_a_tool_that_is_in_use_or_has_been_documented_ahead_of_use()
    {
        var pages = Directory.EnumerateFiles(Path.Combine(RealRepository.Root, "docs", "reference", "tools"), "*.md").Select(Path.GetFileNameWithoutExtension).ToList();

        // index and the dependency catalog are not tool pages, and the six pages of WP1.3 are.
        foreach (var expected in new[] { "docfx", "cspell", "mermaid-cli", "lychee", "markdownlint-cli2", "documentation-auditor" })
        {
            Assert.Contains(expected, pages);
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-DOC-003")]
    public void The_pages_the_policy_owes_are_exactly_the_tools_in_use_that_have_no_page_today()
    {
        var policy = DocumentationPolicy.Load(RealRepository.Files);
        var owed = policy.ToolPages.Owed.SelectMany(group => group.Tools).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(owed.Distinct(StringComparer.Ordinal).Count(), owed.Count);
        Assert.All(owed, tool => Assert.False(File.Exists(Path.Combine(RealRepository.Root, "docs", "reference", "tools", tool + ".md")), $"{tool} has a page and must leave the owed list."));
    }
}
