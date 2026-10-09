using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;
using Xunit;
using ArchitectureModel = ArchUnitNET.Domain.Architecture;

namespace Catalog.Architecture.Tests;

/// <summary>
/// Runs the type-level rules against the production assemblies, and proves that each rule can both pass and fail by running it
/// against conforming and violating fixtures. A rule that has never failed is not known to work, and production code has few
/// types until WP1.6 to WP1.10, so the fixtures are the evidence that these rules bite.
/// </summary>
public sealed class ArchitectureRulesTests
{
    private const string CleanRoot = "Catalog.Architecture.Tests.Fixtures.Clean";
    private const string ViolationsRoot = "Catalog.Architecture.Tests.Fixtures.Violations";

    private static readonly string[] ProductionAssemblies =
    [
        "Catalog.Domain",
        "Catalog.Application",
        "Catalog.Contracts",
        "Catalog.Infrastructure",
        "Catalog.CrossCutting",
        "Catalog.Api",
    ];

    // The production assemblies are analyzed from their files and never loaded, so the tests cannot run production code and a host
    // that restricts which binaries may load cannot break them. ArchUnitNET caches rule results by rule description, and the clean
    // and violating fixtures share descriptions, so the cache is switched off: a result must never depend on which test ran first.
    private static readonly ArchitectureModel ProductionModel = ProductionAssemblies
        .Aggregate(new ArchLoader().WithoutRuleEvaluationCache(), (loader, assembly) => loader.LoadFilteredDirectory(AppContext.BaseDirectory, $"{assembly}.dll", SearchOption.TopDirectoryOnly))
        .Build();

    private static readonly LayerSet ProductionLayers = LayerSet.ForAssemblies();

    private static readonly ArchitectureModel FixtureModel = new ArchLoader().WithoutRuleEvaluationCache().LoadAssembly(typeof(ArchitectureRulesTests).Assembly).Build();
    private static readonly LayerSet CleanLayers = LayerSet.ForNamespaces(CleanRoot);
    private static readonly LayerSet ViolatingLayers = LayerSet.ForNamespaces(ViolationsRoot);

    // The type that each rule must report in the violating fixtures.
    private static readonly Dictionary<string, string> ReportedType = new()
    {
        ["domain-depends-on-the-bcl-only"] = "LeakyProduct",
        ["application-depends-on-domain-and-the-bcl-only"] = "LeakyCoordinator",
        ["contracts-depend-on-the-bcl-only"] = "LeakyDto",
        ["infrastructure-does-not-depend-on-cross-cutting-or-the-composition-root"] = "ApiAwareStore",
        ["cross-cutting-does-not-depend-on-infrastructure-or-the-composition-root"] = "DirectDatabaseDecorator",
        ["no-service-locator-outside-the-composition-root"] = "ServiceLocatorUser",
        ["handlers-are-internal-and-sealed"] = "CreateProductHandler",
        ["no-static-mutable-fields-outside-the-composition-root"] = "GlobalCounter",
        ["no-static-mutable-properties-outside-the-composition-root"] = "StaticSettings",
    };

    public static TheoryData<string> RuleIds => [.. ArchitectureRules.All.Select(rule => rule.Id)];

    [Fact]
    [Trait("Requirement", "REQ-ARC-001")]
    public void The_production_model_contains_every_layer_assembly()
    {
        // Lenient production rules pass on an empty set, so this guards against a layer that silently matches nothing.
        var loaded = ProductionModel.Assemblies.Where(assembly => !assembly.IsOnlyReferenced).Select(assembly => assembly.Name).Order(StringComparer.Ordinal);

        Assert.Equal(ProductionAssemblies.Order(StringComparer.Ordinal), loaded);
    }

    [Fact]
    [Trait("Requirement", "REQ-ARC-001")]
    public void Production_code_follows_the_layer_dependency_rules() => AssertProductionFollowsRulesFor("REQ-ARC-001");

    [Fact]
    [Trait("Requirement", "REQ-ARC-002")]
    public void Production_code_uses_no_service_locator_outside_the_composition_root() => AssertProductionFollowsRulesFor("REQ-ARC-002");

    [Fact]
    [Trait("Requirement", "REQ-ARC-005")]
    public void Production_handlers_are_internal_and_sealed_and_no_static_state_is_mutable() => AssertProductionFollowsRulesFor("REQ-ARC-005");

    [Fact]
    [Trait("Requirement", "REQ-ARC-001")]
    [Trait("Requirement", "REQ-ARC-002")]
    [Trait("Requirement", "REQ-ARC-005")]
    public void Every_requirement_has_at_least_one_rule_and_every_rule_has_a_violating_fixture()
    {
        Assert.Equal(
            ArchitectureRules.All.Select(rule => rule.Id).Order(StringComparer.Ordinal),
            ReportedType.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(
            ["REQ-ARC-001", "REQ-ARC-002", "REQ-ARC-005"],
            ArchitectureRules.All.Select(rule => rule.Requirement).Distinct().Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(RuleIds))]
    [Trait("Requirement", "REQ-ARC-001")]
    [Trait("Requirement", "REQ-ARC-002")]
    [Trait("Requirement", "REQ-ARC-005")]
    public void A_strict_rule_passes_on_conforming_code_and_matches_at_least_one_type(string ruleId)
    {
        var rule = ArchitectureRules.All.Single(candidate => candidate.Id == ruleId).Create(CleanLayers, true);

        AssertNoFailures(Failures(rule, FixtureModel));
    }

    [Theory]
    [MemberData(nameof(RuleIds))]
    [Trait("Requirement", "REQ-ARC-001")]
    [Trait("Requirement", "REQ-ARC-002")]
    [Trait("Requirement", "REQ-ARC-005")]
    public void A_rule_fails_and_names_the_type_that_breaks_it(string ruleId)
    {
        var rule = ArchitectureRules.All.Single(candidate => candidate.Id == ruleId).Create(ViolatingLayers, true);

        var failures = Failures(rule, FixtureModel);

        Assert.Contains(failures, failure => failure.Contains(ReportedType[ruleId], StringComparison.Ordinal));
    }

    private static void AssertProductionFollowsRulesFor(string requirement)
    {
        var failures = ArchitectureRules.All
            .Where(rule => rule.Requirement == requirement)
            .SelectMany(rule => Failures(rule.Create(ProductionLayers, false), ProductionModel).Select(failure => $"{rule.Id}: {failure}"))
            .ToList();

        AssertNoFailures(failures);
    }

    // Assert.Empty shortens long strings, and a rule failure is only useful when it names the type and the dependency in full.
    private static void AssertNoFailures(List<string> failures)
    {
        if (failures.Count > 0)
        {
            Assert.Fail(string.Join(Environment.NewLine, failures));
        }
    }

    private static List<string> Failures(IArchRule rule, ArchitectureModel model) =>
        [.. rule.Evaluate(model).Where(result => !result.Passed).Select(result => result.Description)];
}
