using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.Fluent.Syntax.Elements.Types;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Catalog.Architecture.Tests;

/// <summary>One architecture rule, the requirement it proves, and how to build it for a set of layers.</summary>
/// <param name="Id">A stable name that the tests and failure messages use.</param>
/// <param name="Requirement">The requirement the rule proves.</param>
/// <param name="Create">
/// Builds the rule. A strict rule fails when it matches no type, which proves it is not vacuous. A lenient rule passes on an
/// empty layer, which is what production code needs until WP1.6 to WP1.10 add the types.
/// </param>
internal sealed record NamedRule(string Id, string Requirement, Func<LayerSet, bool, IArchRule> Create);

/// <summary>
/// The type-level rules from plan section 8.1. The reference rules in <see cref="ReferencePolicy"/> cover project and package references.
/// Rules about endpoints (for example, that they depend only on handler interfaces) are added with the endpoints in WP1.10, and
/// interface-based handler rules are added with the handler interfaces in WP1.7.
/// </summary>
internal static class ArchitectureRules
{
    // Anything that lets code look up a service by type is a service locator.
    private const string ServiceLocator =
        @"^(System\.IServiceProvider|Microsoft\.Extensions\.DependencyInjection\.(IServiceScopeFactory|IServiceScope|ActivatorUtilities))$";

    public static IReadOnlyList<NamedRule> All { get; } =
    [
        new(
            "domain-depends-on-the-bcl-only",
            "REQ-ARC-001",
            (layers, strict) => Finish(Types().That().Are(layers.Domain).Should().OnlyDependOnTypesThat().ResideInNamespaceMatching(layers.DomainMayDependOn), strict)),

        new(
            "application-depends-on-domain-and-the-bcl-only",
            "REQ-ARC-001",
            (layers, strict) => Finish(Types().That().Are(layers.Application).Should().OnlyDependOnTypesThat().ResideInNamespaceMatching(layers.ApplicationMayDependOn), strict)),

        // Domain types never appear in contracts: a contract may use only itself and the BCL.
        new(
            "contracts-depend-on-the-bcl-only",
            "REQ-ARC-001",
            (layers, strict) => Finish(Types().That().Are(layers.Contracts).Should().OnlyDependOnTypesThat().ResideInNamespaceMatching(layers.ContractsMayDependOn), strict)),

        new(
            "infrastructure-does-not-depend-on-cross-cutting-or-the-composition-root",
            "REQ-ARC-001",
            (layers, strict) => Finish(Types().That().Are(layers.Infrastructure).Should().NotDependOnAnyTypesThat().Are(AnyOf(layers.CrossCutting, layers.Api)), strict)),

        new(
            "cross-cutting-does-not-depend-on-infrastructure-or-the-composition-root",
            "REQ-ARC-001",
            (layers, strict) => Finish(Types().That().Are(layers.CrossCutting).Should().NotDependOnAnyTypesThat().Are(AnyOf(layers.Infrastructure, layers.Api)), strict)),

        new(
            "no-service-locator-outside-the-composition-root",
            "REQ-ARC-002",
            (layers, strict) => Finish(Types().That().Are(layers.OutsideComposition).Should().NotDependOnAnyTypesThat().HaveFullNameMatching(ServiceLocator), strict)),

        new(
            "handlers-are-internal-and-sealed",
            "REQ-ARC-005",
            (layers, strict) => Finish(Classes().That().Are(layers.ProductionClasses).And().HaveNameEndingWith("Handler").Should().NotBePublic().AndShould().BeSealed(), strict)),

        // Compiler-generated static fields, such as the cache of a lambda, are not state that a developer can reach.
        new(
            "no-static-mutable-fields-outside-the-composition-root",
            "REQ-ARC-005",
            (layers, strict) => Finish(
                FieldMembers().That().AreStatic().And().AreDeclaredIn(layers.OutsideComposition)
                    .And().FollowCustomPredicate(field => !field.IsCompilerGenerated, "are not compiler generated")
                    .Should().BeImmutable(),
                strict)),

        new(
            "no-static-mutable-properties-outside-the-composition-root",
            "REQ-ARC-005",
            (layers, strict) => Finish(PropertyMembers().That().AreStatic().And().AreDeclaredIn(layers.OutsideComposition).Should().BeImmutable(), strict)),
    ];

    // The layers a type may not depend on, as a set of types. Both layers are loaded, so a set can describe them.
    private static GivenTypesConjunction AnyOf(IObjectProvider<IType> first, IObjectProvider<IType> second) =>
        Types().That().Are(first).Or().Are(second);

    private static ArchRule<TObject> Finish<TObject>(ArchRule<TObject> rule, bool strict)
        where TObject : ICanBeAnalyzed =>
        strict ? rule : rule.WithoutRequiringPositiveResults();
}
