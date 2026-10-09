using System.Text.Json;

namespace Catalog.Architecture.Tests;

/// <summary>A package the repository must never link, from governance/policies/forbidden-packages.json.</summary>
/// <param name="Id">The NuGet package ID.</param>
/// <param name="MinVersion">The first forbidden version, or null when every version is forbidden.</param>
/// <param name="Reason">Why, in one sentence.</param>
internal sealed record ForbiddenPackage(string Id, Version? MinVersion, string Reason);

/// <summary>
/// Checks the packages that a restore resolved against the forbidden list. It reads the lock files, which hold every direct and
/// transitive package, so a forbidden package cannot arrive through another package. The license allow-list is enforced separately
/// by nuget-license, which has no forbidden-list option.
/// </summary>
internal static class DependencyPolicy
{
    private const string PoliciesDirectory = "governance/policies";

    public static IReadOnlyList<ForbiddenPackage> LoadForbidden()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(SolutionLayout.Root, PoliciesDirectory, "forbidden-packages.json")));

        return
        [
            .. document.RootElement.GetProperty("forbidden").EnumerateArray().Select(entry => new ForbiddenPackage(
                entry.GetProperty("id").GetString()!,
                entry.TryGetProperty("minVersion", out var min) ? Version.Parse(min.GetString()!) : null,
                entry.GetProperty("reason").GetString()!)),
        ];
    }

    /// <summary>The package IDs that have a license exception. Each one must stay out of production projects.</summary>
    public static IReadOnlyList<string> LoadLicenseExceptions()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(SolutionLayout.Root, PoliciesDirectory, "license-overrides.json")));

        return [.. document.RootElement.EnumerateArray().Select(entry => entry.GetProperty("Id").GetString()!)];
    }

    /// <summary>Lock files under src and tests, relative to the repository root with forward slashes.</summary>
    public static IReadOnlyList<string> LockFiles() =>
        [.. SolutionLayout.ProjectRoots
            .SelectMany(directory => Directory.EnumerateFiles(Path.Combine(SolutionLayout.Root, directory), "packages.lock.json", SearchOption.AllDirectories))
            .Select(path => Path.GetRelativePath(SolutionLayout.Root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)];

    /// <summary>Every package a lock file resolved, as ID and version. Project references have no resolved version and are skipped.</summary>
    public static IReadOnlyList<(string Id, string Version)> ResolvedPackages(string lockFileJson)
    {
        using var document = JsonDocument.Parse(lockFileJson);

        return
        [
            .. document.RootElement.GetProperty("dependencies").EnumerateObject()
                .SelectMany(framework => framework.Value.EnumerateObject())
                .Where(package => package.Value.TryGetProperty("resolved", out _))
                .Select(package => (package.Name, package.Value.GetProperty("resolved").GetString()!))
                .Distinct(),
        ];
    }

    public static IReadOnlyList<string> Violations(string lockFileName, string lockFileJson, IReadOnlyList<ForbiddenPackage> forbidden)
    {
        List<string> violations = [];

        foreach (var (id, version) in ResolvedPackages(lockFileJson))
        {
            foreach (var rule in forbidden.Where(rule => string.Equals(rule.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                if (rule.MinVersion is null || ParseVersion(version) >= rule.MinVersion)
                {
                    violations.Add($"{lockFileName}: {id} {version} is forbidden. {rule.Reason}");
                }
            }
        }

        return violations;
    }

    // A pre-release such as 8.0.0-beta.1 is treated as 8.0.0, so a forbidden line cannot be entered through its pre-releases.
    private static Version ParseVersion(string version) => Version.Parse(version.Split('-', '+')[0]);
}
