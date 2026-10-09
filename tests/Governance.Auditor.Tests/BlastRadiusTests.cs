using System.Text.Json;
using Governance.Auditor.BlastRadius;
using Governance.Auditor.Common;
using Governance.Auditor.Tests.Support;
using Xunit;

namespace Governance.Auditor.Tests;

public sealed class GlobTests
{
    [Theory]
    [Trait("Requirement", "REQ-CI-004")]
    [InlineData("src/Catalog.Domain/**", "src/Catalog.Domain/Thing.cs", true)]
    [InlineData("src/Catalog.Domain/**", "src/Catalog.Domain/Deep/Er/Thing.cs", true)]
    [InlineData("src/Catalog.Domain/**", "src/Catalog.Application/Thing.cs", false)]
    [InlineData("src/**/Migrations/**", "src/Catalog.Infrastructure/Migrations/001.cs", true)]
    [InlineData("src/**/Migrations/**", "src/Migrations/001.cs", true)]
    [InlineData("src/**/Migrations/**", "src/Catalog.Infrastructure/Data/001.cs", false)]
    [InlineData("*.md", "README.md", true)]
    [InlineData("*.md", "docs/page.md", false)]
    [InlineData("docs/**", "docs/page.md", true)]
    [InlineData("**/*.csproj", "Root.csproj", true)]
    [InlineData("**/*.csproj", "src/App/App.csproj", true)]
    [InlineData("**/*.csproj", "src/App/App.cs", false)]
    [InlineData("**/packages.lock.json", "tests/A/packages.lock.json", true)]
    [InlineData("k8s/apps/*/overlays/prod/**", "k8s/apps/catalog/overlays/prod/x.yaml", true)]
    [InlineData("k8s/apps/*/overlays/prod/**", "k8s/apps/catalog/overlays/dev/x.yaml", false)]
    [InlineData("file?.txt", "file1.txt", true)]
    [InlineData("file?.txt", "file12.txt", false)]
    [InlineData("a.b", "aXb", false)]
    [InlineData("global.json", "global.json", true)]
    [InlineData("global.json", "Global.json", false)]
    public void A_glob_matches_paths_by_segment_and_depth(string pattern, string path, bool expected)
    {
        Assert.Equal(expected, Glob.IsMatch(pattern, path));
    }
}

public sealed class BlastRadiusTests
{
    private static readonly BlastRadiusMap Map = BlastRadiusMap.Load(RealRepository.Files);

    [Fact]
    [Trait("Requirement", "REQ-CI-004")]
    public void A_change_to_production_code_names_its_layer_and_requires_the_dotnet_jobs()
    {
        var result = BlastRadiusEvaluator.Evaluate(Map, ["src/Catalog.Domain/Money.cs"]);

        Assert.Equal(["Domain"], result.Layers);
        Assert.Equal(["production-code"], result.Areas);
        Assert.True(result.Run["build-test"]);
        Assert.True(result.Run["format"]);
        Assert.True(result.Run["license"]);
        Assert.False(result.Run["script-tests"]);
    }

    [Fact]
    [Trait("Requirement", "REQ-CI-004")]
    public void A_docs_only_change_runs_only_the_documentation_jobs_and_the_always_jobs()
    {
        var result = BlastRadiusEvaluator.Evaluate(Map, ["docs/adr/0021-x.md", "README.md"]);

        Assert.Equal(["diagrams", "docs-audit", "docs-site", "links", "markdown", "pr-title", "secrets", "spelling"], result.Jobs);
        Assert.False(result.Run["build-test"]);
        Assert.Empty(result.Layers);
        Assert.Equal(["documentation"], result.Areas);
        Assert.Empty(result.Unclassified);
    }

    [Fact]
    [Trait("Requirement", "REQ-CI-004")]
    public void A_workflow_change_runs_every_job_and_asks_for_security_and_code_owner_review()
    {
        var result = BlastRadiusEvaluator.Evaluate(Map, [".github/workflows/ci.yml"]);

        Assert.Equal(Map.Jobs.Count, result.Jobs.Count);
        Assert.All(result.Run.Values, run => Assert.True(run));
        Assert.Contains(result.Reviews, review => review.Id == "codeowners");
        Assert.Contains(result.Reviews, review => review.Id == "security-review");
        Assert.Contains("workflow-change", result.Flags);
        Assert.Contains(result.Docs, doc => doc.Id == "threat-model");
    }

    [Fact]
    [Trait("Requirement", "REQ-CI-004")]
    public void A_migration_is_flagged_and_needs_code_owner_review_and_migration_notes()
    {
        var result = BlastRadiusEvaluator.Evaluate(Map, ["src/Catalog.Infrastructure/Migrations/20260101_Drop.cs"]);

        Assert.Contains("migration", result.Flags);
        Assert.Contains(result.Reviews, review => review.Id == "codeowners");
        Assert.Contains(result.Docs, doc => doc.Id == "migration-notes");
        Assert.Contains("database-migration", result.Areas);
    }

    [Fact]
    [Trait("Requirement", "REQ-CI-004")]
    public void A_path_can_match_several_rules_and_the_results_are_combined()
    {
        var result = BlastRadiusEvaluator.Evaluate(Map, ["src/Catalog.Infrastructure/Catalog.Infrastructure.csproj"]);

        Assert.Contains("Infrastructure", result.Layers);
        Assert.Contains("Build", result.Layers);
        Assert.Contains("dependency-change", result.Flags);
        Assert.Contains("production-code", result.Areas);
        Assert.Contains("build-configuration", result.Areas);
    }

    [Fact]
    [Trait("Requirement", "REQ-CI-004")]
    public void A_change_to_the_auditor_itself_asks_for_a_close_human_read()
    {
        var result = BlastRadiusEvaluator.Evaluate(Map, ["tools/Governance.Auditor/Security/SecurityAuditor.cs"]);

        Assert.Contains(result.Reviews, review => review.Id == "governance-tooling");
        Assert.Contains("auditor-change", result.Flags);
    }

    [Fact]
    [Trait("Requirement", "REQ-CI-004")]
    public void A_path_that_no_rule_covers_runs_everything_and_is_listed_as_unclassified()
    {
        var result = BlastRadiusEvaluator.Evaluate(Map, ["mystery/file.xyz", "docs/page.md"]);

        Assert.Equal(["mystery/file.xyz"], result.Unclassified);
        Assert.All(result.Run.Values, run => Assert.True(run));
        Assert.Contains(result.Reviews, review => review.Id == "maintainer");
    }

    [Fact]
    [Trait("Requirement", "REQ-CI-004")]
    public void An_empty_change_requires_only_the_always_jobs()
    {
        var result = BlastRadiusEvaluator.Evaluate(Map, []);

        Assert.Equal(0, result.ChangedPaths);
        Assert.Equal(Map.Always.Order(StringComparer.Ordinal), result.Jobs);
    }

    [Fact]
    [Trait("Requirement", "REQ-CI-004")]
    public void Paths_are_normalized_and_counted_once_and_the_output_is_sorted()
    {
        var result = BlastRadiusEvaluator.Evaluate(Map, [".\\src\\Catalog.Domain\\A.cs", "./src/Catalog.Domain/A.cs", "src/Catalog.Domain/A.cs", "src/Catalog.Api/Program.cs"]);

        Assert.Equal(2, result.ChangedPaths);
        Assert.Equal(["Api", "Domain"], result.Layers);
    }

    [Theory]
    [Trait("Requirement", "REQ-CI-004")]
    [InlineData("../outside")]
    [InlineData("/absolute/path")]
    [InlineData("src/../../escape")]
    [InlineData("")]
    public void A_path_that_leaves_the_repository_is_refused(string path)
    {
        Assert.Throws<GovernanceException>(() => BlastRadiusEvaluator.Evaluate(Map, [path]));
    }

    [Fact]
    [Trait("Requirement", "REQ-CI-004")]
    public void The_json_result_has_the_fields_a_workflow_needs_and_no_paths()
    {
        var result = BlastRadiusEvaluator.Evaluate(Map, ["src/Catalog.Domain/A.cs", "secret/unclassified/path.txt"]);

        using var document = JsonDocument.Parse(result.ToJson());
        var root = document.RootElement;

        Assert.Equal(2, root.GetProperty("changedPaths").GetInt32());
        Assert.Equal(1, root.GetProperty("unclassifiedCount").GetInt32());
        Assert.True(root.GetProperty("run").GetProperty("build-test").GetBoolean());
        Assert.DoesNotContain("secret/unclassified", result.ToJson(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-CI-004")]
    public void Every_code_owner_path_in_the_repository_is_covered_by_a_rule_that_asks_for_code_owner_review()
    {
        var owned = File.ReadAllLines(Path.Combine(RealRepository.Root, ".github", "CODEOWNERS"))
            .Select(line => line.Trim())
            .Where(line => line.StartsWith('/'))
            .Select(line => line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)[0])
            .ToList();

        Assert.NotEmpty(owned);

        foreach (var entry in owned)
        {
            // A directory entry such as /infra/ covers everything under it, and a sample path under it must ask for the review.
            var sample = entry.TrimStart('/').Replace("*", "sample", StringComparison.Ordinal).Replace("**/", "deep/", StringComparison.Ordinal) + "file.txt";
            var result = BlastRadiusEvaluator.Evaluate(Map, [sample]);

            Assert.True(result.Reviews.Any(review => review.Id == "codeowners"), $"CODEOWNERS covers '{entry}', but the blast-radius map does not ask for code owner review of '{sample}'.");
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-CI-004")]
    public void Every_job_in_the_map_exists_in_the_workflow_that_the_map_names()
    {
        foreach (var (job, definition) in Map.Jobs)
        {
            var workflow = File.ReadAllText(Path.Combine(RealRepository.Root, ".github", "workflows", definition.Workflow));

            Assert.True(
                workflow.Contains($"\n  {job}:", StringComparison.Ordinal) && workflow.Contains($"name: {definition.Name}", StringComparison.Ordinal),
                $"{definition.Workflow} has no job '{job}' named '{definition.Name}'.");
        }
    }

    // The map file.
    [Fact]
    [Trait("Requirement", "REQ-CI-004")]
    public void A_map_that_names_an_unknown_job_doc_or_review_or_repeats_a_rule_is_refused()
    {
        const string map = """
            schema-version: 1
            jobs: { a: { workflow: ci.yml, name: A } }
            always: [missing-job]
            docs-catalog: { d: Doc }
            reviews-catalog: { r: Review }
            rules:
              - { id: one, paths: ['x/**'], area: x, jobs: [nope], docs: [nodoc], reviews: [noreview] }
              - { id: one, paths: [], area: y }
            fallback: { area: unclassified, jobs: [all] }
            """;
        using var repository = new TestRepository().Add("map.yaml", map);

        var exception = Assert.Throws<GovernanceException>(() => BlastRadiusMap.Load(repository.Files, "map.yaml"));

        foreach (var expected in new[] { "'always' names the unknown job 'missing-job'", "unknown job 'nope'", "unknown documentation item 'nodoc'", "unknown review 'noreview'", "'one' appears 2 times", "'one' lists no paths" })
        {
            Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-CI-004")]
    public void A_map_with_an_unknown_key_or_version_is_refused()
    {
        using var repository = new TestRepository()
            .Add("unknown.yaml", "schema-version: 1\nsurprise: true\njobs: {}\nalways: []\ndocs-catalog: {}\nreviews-catalog: {}\nrules: []\nfallback: { area: x }\n")
            .Add("version.yaml", "schema-version: 2\njobs: {}\nalways: []\ndocs-catalog: {}\nreviews-catalog: {}\nrules: []\nfallback: { area: x }\n");

        Assert.Throws<GovernanceException>(() => BlastRadiusMap.Load(repository.Files, "unknown.yaml"));
        Assert.Contains("schema-version 2 is not supported", Assert.Throws<GovernanceException>(() => BlastRadiusMap.Load(repository.Files, "version.yaml")).Message, StringComparison.Ordinal);
    }
}
