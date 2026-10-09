namespace Catalog.Architecture.Tests;

/// <summary>Locates the repository root and lists the project files that the architecture rules read.</summary>
internal static class SolutionLayout
{
    private const string SolutionFileName = "ProductCatalog.slnx";

    public static IReadOnlyList<string> ProjectRoots { get; } = ["src", "tests"];

    public static string Root { get; } = FindRoot();

    /// <summary>Project paths listed in the solution file, relative to the repository root and with forward slashes.</summary>
    public static IReadOnlyList<string> SolutionProjects() =>
        [.. System.Xml.Linq.XDocument.Load(Path.Combine(Root, SolutionFileName))
            .Descendants("Project")
            .Select(project => ((string)project.Attribute("Path")!).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)];

    /// <summary>Every project file under src and tests, relative to the repository root and with forward slashes.</summary>
    public static IReadOnlyList<string> ProjectFilesOnDisk() =>
        [.. ProjectRoots
            .SelectMany(directory => Directory.EnumerateFiles(Path.Combine(Root, directory), "*.csproj", SearchOption.AllDirectories))
            .Select(path => Path.GetRelativePath(Root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)];

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"Could not find {SolutionFileName} above {AppContext.BaseDirectory}. Run the tests from a checkout of the repository.");
    }
}
