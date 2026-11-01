using System.Text.RegularExpressions;
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
/// Rules about endpoints (for example, that they depend only on handler interfaces) are added with the endpoints in WP1.10. The rules about
/// handlers, ports, and decorators arrived with the application layer (WP1.7, ADR-0032).
/// </summary>
internal static class ArchitectureRules
{
    // Anything that lets code look up a service by type is a service locator.
    private const string ServiceLocator =
        @"^(System\.IServiceProvider|Microsoft\.Extensions\.DependencyInjection\.(IServiceScopeFactory|IServiceScope|ActivatorUtilities))$";

    // A port that grows past this number of operations is asking its callers to depend on more than they use. Split it by capability instead.
    private const int MaxPortMembers = 5;

    // A decorator wraps a handler through the handler interface, and only a pipeline type names the decorators, so the order of the chain is written in one place (ADR-0032).
    // A generic class has the arity in its name, as in TelemetryCommandDecorator`2.
    private const string DecoratorName = @"Decorator(`\d+)?$";

    // The two handler interfaces of plan section 8.2, matched by name so the fixtures can declare their own. A generic type has the arity in its name.
    private const string CommandHandlerInterface = @"\.ICommandHandler`2$";
    private const string QueryHandlerInterface = @"\.IQueryHandler`2$";

    // Ports that write (a query must not need them) and the ports that read (a command must not need them).
    private const string WritePorts = @"^(IUnitOfWork|I\w+Repository|IIdempotencyStore|IOutboxDispatcher)$";
    private const string ReadPorts = @"^I\w+ReadStore$";

    // Anything that looks a member up or calls it by name is runtime dispatch, which the explicit pipeline replaces. Reading the name of a type is not, so MemberInfo and Type stay allowed.
    private const string Reflection = @"^(System\.Reflection\.(MethodInfo|MethodBase|ConstructorInfo|PropertyInfo|FieldInfo|Assembly|Emit(\..*)?)|System\.Activator)$";

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

        new(
            "application-ports-are-segregated",
            "REQ-ARC-003",
            (layers, strict) => Finish(
                Types().That().Are(layers.Application).And().FollowCustomPredicate(type => type is Interface, "are interfaces")
                    .Should().FollowCustomCondition(type => DeclaredMemberCount(type) <= MaxPortMembers, $"declare at most {MaxPortMembers} members", $"declares more than {MaxPortMembers} members"),
                strict)),

        new(
            "handlers-implement-a-handler-interface",
            "REQ-APP-001",
            (layers, strict) => Finish(
                Classes().That().Are(layers.Application).And().HaveNameEndingWith("Handler")
                    .Should().FollowCustomCondition(type => Implements(type, CommandHandlerInterface) || Implements(type, QueryHandlerInterface), "implement a handler interface", "implements neither handler interface"),
                strict)),

        new(
            "pipeline-code-uses-no-reflection-or-activator",
            "REQ-APP-001",
            (layers, strict) => Finish(Types().That().Are(AnyOf(layers.Application, layers.CrossCutting)).Should().NotDependOnAnyTypesThat().HaveFullNameMatching(Reflection), strict)),

        new(
            "query-handlers-depend-on-no-write-port",
            "REQ-ARC-003",
            (layers, strict) => Finish(
                Classes().That().Are(layers.Application).And().FollowCustomPredicate(type => Implements(type, QueryHandlerInterface), "implement the query handler interface")
                    .Should().NotDependOnAnyTypesThat().HaveNameMatching(WritePorts),
                strict)),

        new(
            "command-handlers-depend-on-no-read-store",
            "REQ-ARC-003",
            (layers, strict) => Finish(
                Classes().That().Are(layers.Application).And().FollowCustomPredicate(type => Implements(type, CommandHandlerInterface), "implement the command handler interface")
                    .Should().NotDependOnAnyTypesThat().HaveNameMatching(ReadPorts),
                strict)),

        new(
            "decorators-are-composed-only-by-pipelines",
            "REQ-APP-002",
            (layers, strict) => Finish(
                Classes().That().Are(layers.CrossCutting).And().HaveNameMatching(DecoratorName)
                    .Should().FollowCustomCondition(type => !NamesAnotherDecorator(type), "name no other decorator", "names another decorator"),
                strict)),

        // Compiler-generated static fields, such as the cache of a lambda, are not state that a developer can reach. Constants cannot change, and the members of an enum are constants.
        new(
            "no-static-mutable-fields-outside-the-composition-root",
            "REQ-ARC-005",
            (layers, strict) => Finish(
                FieldMembers().That().AreStatic().And().AreDeclaredIn(layers.OutsideComposition)
                    .And().FollowCustomPredicate(field => !field.IsCompilerGenerated, "are not compiler generated")
                    .And().FollowCustomPredicate(field => !LiteralFields.Contains(field), "are not constants")
                    .Should().BeImmutable(),
                strict)),

        new(
            "no-static-mutable-properties-outside-the-composition-root",
            "REQ-ARC-005",
            (layers, strict) => Finish(PropertyMembers().That().AreStatic().And().AreDeclaredIn(layers.OutsideComposition).Should().BeImmutable(), strict)),
    ];

    private static bool Implements(Class type, string interfacePattern) =>
        type.ImplementedInterfaces.Any(@interface => Regex.IsMatch(@interface.FullName, interfacePattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));

    // A decorator may name itself, as a generic class does for its own closed type.
    private static bool NamesAnotherDecorator(Class type) =>
        type.Dependencies.Any(dependency =>
            dependency.Target.FullName != type.FullName
            && Regex.IsMatch(dependency.Target.Name, DecoratorName, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));

    // The operations of an interface: its properties and its methods, without the accessors of the properties.
    private static int DeclaredMemberCount(IType type) =>
        type.Members.OfType<PropertyMember>().Count() + type.Members.OfType<MethodMember>().Count(method => !IsAccessor(method.Name));

    private static bool IsAccessor(string name) =>
        name.StartsWith("get_", StringComparison.Ordinal) || name.StartsWith("set_", StringComparison.Ordinal);

    // The layers a type may not depend on, as a set of types. Both layers are loaded, so a set can describe them.
    private static GivenTypesConjunction AnyOf(IObjectProvider<IType> first, IObjectProvider<IType> second) =>
        Types().That().Are(first).Or().Are(second);

    private static ArchRule<TObject> Finish<TObject>(ArchRule<TObject> rule, bool strict)
        where TObject : ICanBeAnalyzed =>
        strict ? rule : rule.WithoutRequiringPositiveResults();
}
