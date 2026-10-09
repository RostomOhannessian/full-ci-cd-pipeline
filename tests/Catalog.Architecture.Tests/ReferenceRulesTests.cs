using Xunit;

namespace Catalog.Architecture.Tests;

/// <summary>
/// Checks the project reference graph against plan section 8.1. The checks read the project files, because the compiler drops
/// a project reference from the assembly metadata when no code uses it, so an empty layer would otherwise pass without proof.
/// </summary>
public sealed class ReferenceRulesTests
{
    private static readonly Dictionary<string, (ProjectFacts Project, string ExpectedMessage)> ViolatingProjects = new()
    {
        ["domain-references-application"] = (Facts("Catalog.Domain", projects: ["Catalog.Application"]), "Catalog.Domain must not reference Catalog.Application"),
        ["domain-uses-a-package"] = (Facts("Catalog.Domain", packages: ["Newtonsoft.Json"]), "must not use the package Newtonsoft.Json"),
        ["domain-uses-the-web-sdk"] = (Facts("Catalog.Domain", sdk: "Microsoft.NET.Sdk.Web"), "Only the composition root may use the web SDK"),
        ["application-references-infrastructure"] = (Facts("Catalog.Application", projects: ["Catalog.Domain", "Catalog.Infrastructure"]), "Catalog.Application must not reference Catalog.Infrastructure"),
        ["application-uses-entity-framework"] = (Facts("Catalog.Application", projects: ["Catalog.Domain"], packages: ["Microsoft.EntityFrameworkCore.SqlServer"]), "must not use the infrastructure package Microsoft.EntityFrameworkCore.SqlServer"),
        ["application-adds-a-framework-reference"] = (Facts("Catalog.Application", projects: ["Catalog.Domain"], frameworks: ["Microsoft.AspNetCore.App"]), "must not add the framework reference Microsoft.AspNetCore.App"),
        ["contracts-reference-domain"] = (Facts("Catalog.Contracts", projects: ["Catalog.Domain"]), "Catalog.Contracts must not reference Catalog.Domain"),
        ["infrastructure-references-cross-cutting"] = (Facts("Catalog.Infrastructure", projects: ["Catalog.Application", "Catalog.CrossCutting"]), "Catalog.Infrastructure must not reference Catalog.CrossCutting"),
        ["cross-cutting-references-infrastructure"] = (Facts("Catalog.CrossCutting", projects: ["Catalog.Application", "Catalog.Infrastructure"]), "Catalog.CrossCutting must not reference Catalog.Infrastructure"),
        ["client-references-a-server-project"] = (Facts("Catalog.Client", projects: ["Catalog.Contracts"]), "Catalog.Client must not reference Catalog.Contracts"),
        ["production-references-a-test-project"] = (Facts("Catalog.Domain", projects: ["Catalog.Architecture.Tests"]), "Catalog.Domain must not reference Catalog.Architecture.Tests"),
        ["project-missing-from-the-policy"] = (Facts("Catalog.Mystery"), "Catalog.Mystery is not in the layer policy"),
    };

    [Fact]
    [Trait("Requirement", "REQ-ARC-001")]
    public void Every_production_project_follows_the_layer_reference_policy()
    {
        var violations = SolutionLayout.ProjectFilesOnDisk()
            .Where(path => path.StartsWith("src/", StringComparison.Ordinal))
            .SelectMany(path => ReferencePolicy.Evaluate(ProjectFacts.Load(Path.Combine(SolutionLayout.Root, path))))
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    [Trait("Requirement", "REQ-ARC-001")]
    public void Every_project_file_is_listed_in_the_solution_and_every_listed_project_exists()
    {
        Assert.Equal(SolutionLayout.ProjectFilesOnDisk(), SolutionLayout.SolutionProjects());
    }

    [Fact]
    [Trait("Requirement", "REQ-ARC-001")]
    public void A_project_that_follows_the_policy_has_no_violations()
    {
        var composition = Facts(
            "Catalog.Api",
            projects: ["Catalog.Application", "Catalog.Contracts", "Catalog.CrossCutting", "Catalog.Infrastructure"],
            packages: ["Any.Package.Is.Allowed.In.The.Composition.Root"],
            sdk: "Microsoft.NET.Sdk.Web");

        Assert.Empty(ReferencePolicy.Evaluate(composition));
    }

    [Theory]
    [Trait("Requirement", "REQ-ARC-001")]
    [InlineData("domain-references-application")]
    [InlineData("domain-uses-a-package")]
    [InlineData("domain-uses-the-web-sdk")]
    [InlineData("application-references-infrastructure")]
    [InlineData("application-uses-entity-framework")]
    [InlineData("application-adds-a-framework-reference")]
    [InlineData("contracts-reference-domain")]
    [InlineData("infrastructure-references-cross-cutting")]
    [InlineData("cross-cutting-references-infrastructure")]
    [InlineData("client-references-a-server-project")]
    [InlineData("production-references-a-test-project")]
    [InlineData("project-missing-from-the-policy")]
    public void The_policy_rejects_a_project_that_breaks_a_layer_rule(string caseName)
    {
        var (project, expectedMessage) = ViolatingProjects[caseName];

        var violations = ReferencePolicy.Evaluate(project);

        Assert.Contains(violations, violation => violation.Contains(expectedMessage, StringComparison.Ordinal));
    }

    private static ProjectFacts Facts(
        string name,
        string[]? projects = null,
        string[]? packages = null,
        string[]? frameworks = null,
        string sdk = "Microsoft.NET.Sdk") =>
        new(name, sdk, projects ?? [], packages ?? [], frameworks ?? []);
}
