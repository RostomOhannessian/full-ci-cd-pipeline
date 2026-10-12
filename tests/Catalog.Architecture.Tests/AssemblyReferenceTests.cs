using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using Xunit;

namespace Catalog.Architecture.Tests;

/// <summary>
/// REQ-ARC-001: the Domain and the Contracts reference the base class library and nothing else. The type rules in <see cref="ArchitectureRules"/> match the namespace
/// <c>System</c>, and a NuGet package can declare types in that namespace, so these tests read the assembly references from the metadata of the built assemblies
/// and accept an assembly only when it ships in the runtime directory of .NET, which is where the base class library lives. The assemblies are read from disk and are
/// never loaded. The last test shows that the check fails on an assembly with outside references, so a pass means something.
/// </summary>
public sealed class AssemblyReferenceTests
{
    private static readonly string RuntimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();

    private static string BinaryPath(string assemblyName) => Path.Combine(AppContext.BaseDirectory, $"{assemblyName}.dll");

    private static List<string> ReferencedAssemblies(string path)
    {
        using var stream = File.OpenRead(path);
        using var image = new PEReader(stream);
        var metadata = image.GetMetadataReader();

        return [.. metadata.AssemblyReferences.Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name)).Order(StringComparer.Ordinal)];
    }

    private static List<string> OutsideTheBaseClassLibrary(IEnumerable<string> referencedAssemblies) =>
        [.. referencedAssemblies.Where(name => !File.Exists(Path.Combine(RuntimeDirectory, $"{name}.dll")))];

    /// <summary>The namespaces of the top-level types of an assembly that are not its own, such as a tracker that a coverage tool injected. A type with no namespace is the module or a compiler type.</summary>
    private static List<string> ForeignTypes(string path, string assemblyName)
    {
        using var stream = File.OpenRead(path);
        using var image = new PEReader(stream);
        var metadata = image.GetMetadataReader();

        return
        [
            .. metadata.TypeDefinitions
                .Select(metadata.GetTypeDefinition)
                .Where(type => type.GetDeclaringType().IsNil)
                .Select(type => (Namespace: metadata.GetString(type.Namespace), Name: metadata.GetString(type.Name)))
                .Where(type => type.Namespace.Length > 0 && type.Namespace != assemblyName && !type.Namespace.StartsWith($"{assemblyName}.", StringComparison.Ordinal))
                .Select(type => $"{type.Namespace}.{type.Name}")
                .Order(StringComparer.Ordinal),
        ];
    }

    [Theory]
    [InlineData("Catalog.Domain")]
    [InlineData("Catalog.Application")]
    [InlineData("Catalog.Contracts")]
    [InlineData("Catalog.Infrastructure")]
    [InlineData("Catalog.CrossCutting")]
    [InlineData("Catalog.Api")]
    [Trait("Requirement", "REQ-ARC-001")]
    public void The_rules_read_assemblies_that_hold_only_the_types_of_their_own_layer(string assemblyName)
    {
        // On Linux the coverage extension instruments the assemblies in the output folder and adds a tracker type to each one that has code, which makes every
        // type look as if it depends on it. This project excludes the production assemblies from instrumentation (coverage.settings.xml), and this test fails
        // when that stops working, so a rule never judges a type that nobody wrote.
        var foreign = ForeignTypes(BinaryPath(assemblyName), assemblyName);

        Assert.True(foreign.Count == 0, $"{assemblyName} holds types outside its own namespace, which means that a tool changed the assembly on disk: {string.Join(", ", foreign)}. Check tests/Catalog.Architecture.Tests/coverage.settings.xml.");
    }

    [Fact]
    [Trait("Requirement", "REQ-ARC-001")]
    public void The_foreign_type_check_reports_types_outside_the_layer_and_never_the_layers_own_types()
    {
        // The test assembly holds its own types and generated types of the test framework. Asked as the Domain, every one of them is foreign. Asked as itself,
        // none of the types that it wrote is reported, which is what a layer assembly relies on.
        var asDomain = ForeignTypes(typeof(AssemblyReferenceTests).Assembly.Location, "Catalog.Domain");
        var asItself = ForeignTypes(typeof(AssemblyReferenceTests).Assembly.Location, "Catalog.Architecture.Tests");

        Assert.Contains(typeof(AssemblyReferenceTests).FullName, asDomain);
        Assert.DoesNotContain(typeof(AssemblyReferenceTests).FullName, asItself);
        Assert.True(asDomain.Count > asItself.Count);
    }

    [Fact]
    [Trait("Requirement", "REQ-ARC-001")]
    public void The_domain_references_only_assemblies_of_the_base_class_library()
    {
        var references = ReferencedAssemblies(BinaryPath("Catalog.Domain"));

        Assert.Contains("System.Runtime", references);
        Assert.Empty(OutsideTheBaseClassLibrary(references));
    }

    [Fact]
    [Trait("Requirement", "REQ-ARC-001")]
    public void The_contracts_reference_only_assemblies_of_the_base_class_library()
    {
        Assert.Empty(OutsideTheBaseClassLibrary(ReferencedAssemblies(BinaryPath("Catalog.Contracts"))));
    }

    [Fact]
    [Trait("Requirement", "REQ-ARC-001")]
    public void The_check_names_every_reference_that_is_not_part_of_the_base_class_library()
    {
        string[] planted =
        [
            "System.Runtime",
            "System.Linq",
            "Newtonsoft.Json",
            "Microsoft.Extensions.Logging.Abstractions",
            "System.Data.SqlClient",
            "Catalog.Application",
        ];

        Assert.Equal(
            ["Catalog.Application", "Microsoft.Extensions.Logging.Abstractions", "Newtonsoft.Json", "System.Data.SqlClient"],
            OutsideTheBaseClassLibrary(planted).Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Requirement", "REQ-ARC-001")]
    public void The_metadata_reader_finds_the_outside_references_of_a_real_assembly()
    {
        // This test assembly references xUnit and ArchUnitNET, which are not part of the base class library.
        var outside = OutsideTheBaseClassLibrary(ReferencedAssemblies(typeof(AssemblyReferenceTests).Assembly.Location));

        Assert.Contains(outside, name => name.StartsWith("xunit", StringComparison.Ordinal));
    }
}
