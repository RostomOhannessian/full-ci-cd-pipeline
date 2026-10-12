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
