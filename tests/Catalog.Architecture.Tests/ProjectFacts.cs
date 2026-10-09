using System.Xml.Linq;

namespace Catalog.Architecture.Tests;

/// <summary>What the architecture rules need to know about one project file.</summary>
internal sealed record ProjectFacts(
    string Name,
    string Sdk,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<string> PackageReferences,
    IReadOnlyList<string> FrameworkReferences)
{
    public static ProjectFacts Load(string projectFilePath)
    {
        var project = XDocument.Load(projectFilePath).Root!;

        return new ProjectFacts(
            Path.GetFileNameWithoutExtension(projectFilePath),
            (string?)project.Attribute("Sdk") ?? string.Empty,
            Includes(project, "ProjectReference", include => Path.GetFileNameWithoutExtension(include.Replace('\\', '/'))),
            Includes(project, "PackageReference", include => include),
            Includes(project, "FrameworkReference", include => include));
    }

    private static List<string> Includes(XElement project, string element, Func<string, string> select) =>
        [.. project.Descendants(element).Select(e => select((string)e.Attribute("Include")!)).Order(StringComparer.Ordinal)];
}
