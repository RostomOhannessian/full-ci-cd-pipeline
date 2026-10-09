namespace Catalog.Architecture.Tests;

/// <summary>
/// The project reference table from plan section 8.1, as data. A project may reference only the projects listed for it,
/// and only the packages its layer allows. Changing a row is a design decision, so it needs an ADR.
/// </summary>
internal static class ReferencePolicy
{
    private const string Domain = "Catalog.Domain";
    private const string Application = "Catalog.Application";
    private const string Contracts = "Catalog.Contracts";
    private const string Infrastructure = "Catalog.Infrastructure";
    private const string CrossCutting = "Catalog.CrossCutting";
    private const string Client = "Catalog.Client";

    private const string PlainSdk = "Microsoft.NET.Sdk";
    private const string WebSdk = "Microsoft.NET.Sdk.Web";

    // Packages that belong to the outer layers. Application and Domain must not use them, so ports stay free of infrastructure types.
    private static readonly string[] InfrastructurePackagePrefixes =
    [
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.Data.SqlClient",
        "System.Data.SqlClient",
        "Microsoft.Extensions.Caching",
        "Microsoft.Extensions.DependencyInjection",
        "Microsoft.Extensions.Hosting",
        "StackExchange.Redis",
        "ZiggyCreatures",
        "OpenTelemetry",
    ];

    private static readonly Dictionary<string, LayerPolicy> Layers = new()
    {
        [Domain] = new([], PackageRule.None, PlainSdk),
        [Application] = new([Domain], PackageRule.NoInfrastructure, PlainSdk),
        [Contracts] = new([], PackageRule.None, PlainSdk),
        [Infrastructure] = new([Application], PackageRule.Any, PlainSdk),
        [CrossCutting] = new([Application], PackageRule.Any, PlainSdk),
        ["Catalog.Api"] = new([Application, Infrastructure, CrossCutting, Contracts], PackageRule.Any, WebSdk),

        // Planned for Phase 4 (WP4.1 and WP4.2). The client stays independent of every server project.
        [Client] = new([], PackageRule.Any, PlainSdk),
        ["Catalog.SyntheticShopper"] = new([Client], PackageRule.Any, PlainSdk),
    };

    public static IReadOnlyList<string> Evaluate(ProjectFacts project)
    {
        if (!Layers.TryGetValue(project.Name, out var policy))
        {
            return [$"{project.Name} is not in the layer policy. Add it to ReferencePolicy with the plan section 8.1 rule that governs it."];
        }

        List<string> violations = [];

        foreach (var reference in project.ProjectReferences.Where(reference => !policy.AllowedProjects.Contains(reference, StringComparer.Ordinal)))
        {
            violations.Add($"{project.Name} must not reference {reference}. Allowed project references: {Describe(policy.AllowedProjects)}.");
        }

        if (!string.Equals(project.Sdk, policy.Sdk, StringComparison.Ordinal))
        {
            violations.Add($"{project.Name} uses the SDK '{project.Sdk}' but must use '{policy.Sdk}'. Only the composition root may use the web SDK.");
        }

        foreach (var framework in project.FrameworkReferences)
        {
            violations.Add($"{project.Name} must not add the framework reference {framework}.");
        }

        foreach (var package in project.PackageReferences.Where(package => IsForbidden(policy.Packages, package)))
        {
            violations.Add(policy.Packages == PackageRule.None
                ? $"{project.Name} must not use the package {package}. This layer allows no packages."
                : $"{project.Name} must not use the infrastructure package {package}.");
        }

        return violations;
    }

    private static bool IsForbidden(PackageRule rule, string package) => rule switch
    {
        PackageRule.None => true,
        PackageRule.NoInfrastructure => InfrastructurePackagePrefixes.Any(prefix => package.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)),
        _ => false,
    };

    private static string Describe(IReadOnlyList<string> projects) => projects.Count == 0 ? "none" : string.Join(", ", projects);

    private enum PackageRule
    {
        /// <summary>No package at all.</summary>
        None,

        /// <summary>Any package except the infrastructure packages listed above.</summary>
        NoInfrastructure,

        /// <summary>Any package. Each one still passes the license gate.</summary>
        Any,
    }

    private sealed record LayerPolicy(IReadOnlyList<string> AllowedProjects, PackageRule Packages, string Sdk);
}
