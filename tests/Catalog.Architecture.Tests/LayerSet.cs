using System.Text.RegularExpressions;
using ArchUnitNET.Domain;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Catalog.Architecture.Tests;

/// <summary>
/// The six layers as sets of types. Production code is split by assembly, so a type outside any namespace, such as the top-level
/// <c>Program</c>, still belongs to its layer. The fixtures live in one assembly, so they are split by namespace instead.
/// </summary>
internal sealed record LayerSet(
    IObjectProvider<IType> Domain,
    IObjectProvider<IType> Application,
    IObjectProvider<IType> Contracts,
    IObjectProvider<IType> Infrastructure,
    IObjectProvider<IType> CrossCutting,
    IObjectProvider<IType> Api,
    IObjectProvider<IType> OutsideComposition,
    IObjectProvider<Class> ProductionClasses,
    string DomainMayDependOn,
    string ApplicationMayDependOn,
    string ContractsMayDependOn)
{
    // The BCL is every type in a System namespace. A set of types cannot express it, because external types are never loaded into
    // the architecture, so the rules that restrict dependencies match the namespace of the type that is depended on.
    private const string Bcl = "System";

    /// <summary>The production layers, matched by assembly name. The assemblies are read from disk and never loaded into the test process.</summary>
    public static LayerSet ForAssemblies()
    {
        static string Named(string layer) => $@"^Catalog\.{layer}(\s*,.*)?$";

        const string Outside = @"^Catalog\.(Domain|Application|Contracts|Infrastructure|CrossCutting)(\s*,.*)?$";
        const string All = @"^Catalog\.(Domain|Application|Contracts|Infrastructure|CrossCutting|Api)(\s*,.*)?$";

        return new(
            Types().That().ResideInAssemblyMatching(Named("Domain")).As("Domain"),
            Types().That().ResideInAssemblyMatching(Named("Application")).As("Application"),
            Types().That().ResideInAssemblyMatching(Named("Contracts")).As("Contracts"),
            Types().That().ResideInAssemblyMatching(Named("Infrastructure")).As("Infrastructure"),
            Types().That().ResideInAssemblyMatching(Named("CrossCutting")).As("CrossCutting"),
            Types().That().ResideInAssemblyMatching(Named("Api")).As("Api"),
            Types().That().ResideInAssemblyMatching(Outside).As("Production code outside the composition root"),
            Classes().That().ResideInAssemblyMatching(All).As("Production classes"),
            MayDependOn("Catalog.Domain"),
            MayDependOn("Catalog.Application", "Catalog.Domain"),
            MayDependOn("Catalog.Contracts"));
    }

    public static LayerSet ForNamespaces(string root)
    {
        static string Within(string ns) => $@"^{Regex.Escape(ns)}(\..*)?$";

        var all = $@"^{Regex.Escape(root)}\.(Domain|Application|Contracts|Infrastructure|CrossCutting|Api)(\..*)?$";
        var outside = $@"^{Regex.Escape(root)}\.(Domain|Application|Contracts|Infrastructure|CrossCutting)(\..*)?$";

        return new(
            Types().That().ResideInNamespaceMatching(Within($"{root}.Domain")).As("Domain"),
            Types().That().ResideInNamespaceMatching(Within($"{root}.Application")).As("Application"),
            Types().That().ResideInNamespaceMatching(Within($"{root}.Contracts")).As("Contracts"),
            Types().That().ResideInNamespaceMatching(Within($"{root}.Infrastructure")).As("Infrastructure"),
            Types().That().ResideInNamespaceMatching(Within($"{root}.CrossCutting")).As("CrossCutting"),
            Types().That().ResideInNamespaceMatching(Within($"{root}.Api")).As("Api"),
            Types().That().ResideInNamespaceMatching(outside).As("Production code outside the composition root"),
            Classes().That().ResideInNamespaceMatching(all).As("Production classes"),
            MayDependOn($"{root}.Domain"),
            MayDependOn($"{root}.Application", $"{root}.Domain"),
            MayDependOn($"{root}.Contracts"));
    }

    private static string MayDependOn(params string[] namespaces) =>
        $@"^({Bcl}|{string.Join('|', namespaces.Select(Regex.Escape))})(\..*)?$";
}
