using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ArchUnitNET.Domain;

namespace Catalog.Architecture.Tests;

/// <summary>
/// The constants of the analyzed assemblies. ArchUnitNET reports a <c>const</c> field as writable, although the compiler replaces every use of it with its value and
/// nothing can change it at run time, so the static-state rule would flag every constant. The metadata of the assembly says which fields are literals, and the
/// rule skips exactly those. The assemblies are read from disk and never loaded.
/// </summary>
internal static class LiteralFields
{
    private static readonly HashSet<string> Names = Read();

    public static bool Contains(FieldMember field) => Names.Contains($"{field.DeclaringType.FullName}::{field.Name}");

    private static HashSet<string> Read()
    {
        HashSet<string> names = new(StringComparer.Ordinal);
        var files = Directory.EnumerateFiles(AppContext.BaseDirectory, "Catalog.*.dll", SearchOption.TopDirectoryOnly);

        foreach (var file in files)
        {
            using var stream = File.OpenRead(file);
            using var image = new PEReader(stream);
            var metadata = image.GetMetadataReader();

            foreach (var handle in metadata.TypeDefinitions)
            {
                var type = metadata.GetTypeDefinition(handle);
                var typeName = FullName(metadata, type);

                foreach (var fieldHandle in type.GetFields())
                {
                    var field = metadata.GetFieldDefinition(fieldHandle);
                    if ((field.Attributes & FieldAttributes.Literal) != 0)
                    {
                        names.Add($"{typeName}::{metadata.GetString(field.Name)}");
                    }
                }
            }
        }

        return names;
    }

    // ArchUnitNET writes the name of a nested type with a plus sign, as reflection does.
    private static string FullName(MetadataReader metadata, TypeDefinition type)
    {
        var name = metadata.GetString(type.Name);
        var declaring = type.GetDeclaringType();

        return declaring.IsNil
            ? string.Join('.', new[] { metadata.GetString(type.Namespace), name }.Where(part => part.Length > 0))
            : $"{FullName(metadata, metadata.GetTypeDefinition(declaring))}+{name}";
    }
}
