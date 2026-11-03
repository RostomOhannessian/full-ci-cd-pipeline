using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Catalog.Architecture.Tests;

/// <summary>
/// Finds what would put the Windows runtime assets of a package into a shipped artifact: a production project that selects a Windows runtime
/// identifier, and a Dockerfile that builds on a Windows image. The license exception for the native Windows runtime of SqlClient holds only
/// while no shipped artifact contains it (ADR-0004, amendment of 2026-11-03), so each of these is a reason to revisit the exception.
/// </summary>
internal static partial class WindowsArtifactPolicy
{
    private static readonly string[] SkippedDirectories = [".git", ".dev", ".docfx", "_site", "bin", "node_modules", "obj", "TestResults"];

    [GeneratedRegex(@"windows|nanoserver|servercore", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex WindowsImagePattern();

    /// <summary>The Windows runtime identifiers that an MSBuild file selects, such as win-x64.</summary>
    public static IReadOnlyList<string> WindowsRuntimeIdentifiers(string msbuildXml) =>
        [.. XDocument.Parse(msbuildXml).Descendants()
            .Where(element => element.Name.LocalName is "RuntimeIdentifier" or "RuntimeIdentifiers")
            .SelectMany(element => element.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(identifier => identifier.StartsWith("win", StringComparison.OrdinalIgnoreCase))];

    /// <summary>The FROM lines of a Dockerfile that name a Windows base image or a Windows platform.</summary>
    public static IReadOnlyList<string> WindowsBaseImages(string dockerfile) =>
        [.. dockerfile.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("FROM ", StringComparison.OrdinalIgnoreCase) && WindowsImagePattern().IsMatch(line))];

    /// <summary>The MSBuild files that shape a production project: the shared build files and everything under src.</summary>
    public static IReadOnlyList<string> ProductionBuildFiles() =>
        [.. Files(SolutionLayout.Root, path => IsSharedBuildFile(path) || (IsBuildFile(path) && IsUnderSrc(path)))
            .Select(path => Path.GetRelativePath(SolutionLayout.Root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)];

    /// <summary>The Dockerfiles of the repository, wherever they are, relative to the repository root with forward slashes.</summary>
    public static IReadOnlyList<string> Dockerfiles() =>
        [.. Files(SolutionLayout.Root, path => IsDockerfile(Path.GetFileName(path)))
            .Select(path => Path.GetRelativePath(SolutionLayout.Root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)];

    private static bool IsDockerfile(string name) =>
        name.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Dockerfile.", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".Dockerfile", StringComparison.OrdinalIgnoreCase);

    // Directory.Build.props and Directory.Build.targets at the root apply to every project, including the ones under src.
    private static bool IsSharedBuildFile(string path) =>
        string.Equals(Path.GetDirectoryName(path), SolutionLayout.Root, StringComparison.OrdinalIgnoreCase)
        && Path.GetFileName(path) is "Directory.Build.props" or "Directory.Build.targets";

    private static bool IsBuildFile(string path) =>
        path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".props", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase);

    private static bool IsUnderSrc(string path) =>
        Path.GetRelativePath(SolutionLayout.Root, path).Replace('\\', '/').StartsWith("src/", StringComparison.Ordinal);

    private static IEnumerable<string> Files(string directory, Func<string, bool> include)
    {
        if (!Directory.Exists(directory))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(directory).Where(include))
        {
            yield return file;
        }

        foreach (var child in Directory.EnumerateDirectories(directory).Where(child => !SkippedDirectories.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase)))
        {
            foreach (var file in Files(child, include))
            {
                yield return file;
            }
        }
    }
}
