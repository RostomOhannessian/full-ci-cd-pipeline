using System.Text.Json;

namespace Catalog.Architecture.Tests;

/// <summary>A package the repository must never link, from governance/policies/forbidden-packages.json.</summary>
/// <param name="Id">The NuGet package ID.</param>
/// <param name="MinVersion">The first forbidden version, or null when every version is forbidden.</param>
/// <param name="Reason">Why, in one sentence.</param>
internal sealed record ForbiddenPackage(string Id, Version? MinVersion, string Reason);

/// <summary>A license exception, from an entry of governance/policies/license-overrides.json. The gate reads Id, Version, License, and LicenseUrl, and ignores the rest.</summary>
/// <param name="Id">The NuGet package ID, or empty when the entry has none.</param>
/// <param name="Version">The exact package version that the exception covers.</param>
/// <param name="License">The license reference that the allow-list admits for this package.</param>
/// <param name="Kind">The kind of exception: <see cref="DependencyPolicy.ToolOnlyKind"/> or <see cref="DependencyPolicy.WindowsNativeRuntimeKind"/>.</param>
/// <param name="Scope">For a Windows native runtime, which assets the exception covers.</param>
/// <param name="Reason">For a Windows native runtime, why no shipped artifact contains them.</param>
internal sealed record LicenseException(string Id, string? Version, string? License, string? Kind, string? Scope, string? Reason);

/// <summary>A package that a lock file resolved. The type is Direct, Transitive, or CentralTransitive.</summary>
internal sealed record ResolvedPackage(string Id, string Version, string Type);

/// <summary>
/// Checks the packages that a restore resolved against the forbidden list and the license exceptions. It reads the lock files, which hold
/// every direct and transitive package, so a forbidden package cannot arrive through another package. The license allow-list is enforced
/// separately by nuget-license, which has no forbidden-list option.
/// </summary>
internal static class DependencyPolicy
{
    private const string PoliciesDirectory = "governance/policies";

    /// <summary>A tool that is used at build or test time and is never linked into a production project (ADR-0004).</summary>
    public const string ToolOnlyKind = "tool-only";

    /// <summary>A native runtime of the Windows host that no shipped artifact contains (ADR-0004, amendment of 2026-11-03).</summary>
    public const string WindowsNativeRuntimeKind = "windows-native-runtime";

    /// <summary>
    /// The one Windows native runtime exception that ADR-0004 approves, by exact package, version, and license reference. A different package or
    /// a different version needs a new decision in ADR-0004, so a Dependabot update of SqlClient fails here until someone reads the new terms.
    /// </summary>
    public static LicenseException ApprovedWindowsNativeRuntime { get; } = new(
        "Microsoft.Data.SqlClient.SNI.runtime",
        "7.1.0",
        "LicenseRef-Microsoft-SqlClient-SNI-WindowsRuntime",
        WindowsNativeRuntimeKind,
        Scope: null,
        Reason: null);

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

    /// <summary>The license exceptions of governance/policies/license-overrides.json.</summary>
    public static IReadOnlyList<LicenseException> LoadLicenseExceptions() =>
        ParseLicenseExceptions(File.ReadAllText(Path.Combine(SolutionLayout.Root, PoliciesDirectory, "license-overrides.json")));

    public static IReadOnlyList<LicenseException> ParseLicenseExceptions(string overridesJson)
    {
        using var document = JsonDocument.Parse(overridesJson);

        return
        [
            .. document.RootElement.EnumerateArray().Select(entry => new LicenseException(
                Text(entry, "Id") ?? string.Empty,
                Text(entry, "Version"),
                Text(entry, "License"),
                Text(entry, "Kind"),
                Text(entry, "Scope"),
                Text(entry, "Reason"))),
        ];
    }

    /// <summary>Lock files under src, tests, and tools, relative to the repository root with forward slashes.</summary>
    public static IReadOnlyList<string> LockFiles() =>
        [.. SolutionLayout.ProjectRoots
            .SelectMany(directory => Directory.EnumerateFiles(Path.Combine(SolutionLayout.Root, directory), "packages.lock.json", SearchOption.AllDirectories))
            .Select(path => Path.GetRelativePath(SolutionLayout.Root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)];

    /// <summary>Every package a lock file resolved. Project references have no resolved version and are skipped.</summary>
    public static IReadOnlyList<ResolvedPackage> ResolvedPackages(string lockFileJson)
    {
        using var document = JsonDocument.Parse(lockFileJson);

        return
        [
            .. document.RootElement.GetProperty("dependencies").EnumerateObject()
                .SelectMany(framework => framework.Value.EnumerateObject())
                .Where(package => package.Value.TryGetProperty("resolved", out _))
                .Select(package => new ResolvedPackage(package.Name, package.Value.GetProperty("resolved").GetString()!, Text(package.Value, "type") ?? string.Empty))
                .Distinct(),
        ];
    }

    public static IReadOnlyList<string> Violations(string lockFileName, string lockFileJson, IReadOnlyList<ForbiddenPackage> forbidden)
    {
        List<string> violations = [];

        foreach (var package in ResolvedPackages(lockFileJson))
        {
            foreach (var rule in forbidden.Where(rule => string.Equals(rule.Id, package.Id, StringComparison.OrdinalIgnoreCase)))
            {
                if (rule.MinVersion is null || ParseVersion(package.Version) >= rule.MinVersion)
                {
                    violations.Add($"{lockFileName}: {package.Id} {package.Version} is forbidden. {rule.Reason}");
                }
            }
        }

        return violations;
    }

    /// <summary>
    /// What each license exception must state. Every exception has a known kind. A Windows native runtime exception must be the one that
    /// ADR-0004 approves, and it must record its scope and its reason, so no other package or version can enter through the same door.
    /// </summary>
    public static IReadOnlyList<string> ExceptionDefinitionViolations(IReadOnlyList<LicenseException> exceptions)
    {
        List<string> violations = [];

        foreach (var exception in exceptions)
        {
            var name = $"{(exception.Id.Length == 0 ? "(no Id)" : exception.Id)} {exception.Version}".TrimEnd();

            switch (exception.Kind)
            {
                case ToolOnlyKind:
                    break;

                case WindowsNativeRuntimeKind:
                    if (!IsApprovedWindowsNativeRuntime(exception))
                    {
                        violations.Add($"{name}: only {ApprovedWindowsNativeRuntime.Id} {ApprovedWindowsNativeRuntime.Version} under {ApprovedWindowsNativeRuntime.License} may have a {WindowsNativeRuntimeKind} exception. Another package or version needs a new decision in ADR-0004.");
                    }

                    if (string.IsNullOrWhiteSpace(exception.Scope))
                    {
                        violations.Add($"{name}: a {WindowsNativeRuntimeKind} exception must record its scope.");
                    }

                    if (string.IsNullOrWhiteSpace(exception.Reason))
                    {
                        violations.Add($"{name}: a {WindowsNativeRuntimeKind} exception must record its reason.");
                    }

                    break;

                default:
                    violations.Add($"{name}: the kind is {(exception.Kind is null ? "missing" : $"'{exception.Kind}'")}, and every exception has the kind '{ToolOnlyKind}' or '{WindowsNativeRuntimeKind}'.");
                    break;
            }
        }

        return violations;
    }

    /// <summary>
    /// Where each license exception may appear. A tool-only package stays out of production projects (src). The approved Windows native runtime may
    /// appear in any project, but only at the exact approved version and only as a dependency of another package, never as a direct reference.
    /// </summary>
    public static IReadOnlyList<string> ExceptionUseViolations(string lockFileName, string lockFileJson, IReadOnlyList<LicenseException> exceptions)
    {
        var production = lockFileName.StartsWith("src/", StringComparison.Ordinal);
        List<string> violations = [];

        foreach (var package in ResolvedPackages(lockFileJson))
        {
            foreach (var exception in exceptions.Where(exception => string.Equals(exception.Id, package.Id, StringComparison.OrdinalIgnoreCase)))
            {
                if (IsApprovedWindowsNativeRuntime(exception))
                {
                    if (!string.Equals(package.Version, exception.Version, StringComparison.Ordinal))
                    {
                        violations.Add($"{lockFileName}: {package.Id} {package.Version} is not the version {exception.Version} that the {WindowsNativeRuntimeKind} exception covers. A new version needs a new decision in ADR-0004.");
                    }
                    else if (string.Equals(package.Type, "Direct", StringComparison.Ordinal))
                    {
                        violations.Add($"{lockFileName}: {package.Id} is referenced directly. The {WindowsNativeRuntimeKind} exception covers it only as a dependency of the driver that needs it.");
                    }
                }
                else if (production)
                {
                    violations.Add($"{lockFileName}: {package.Id} has a {exception.Kind ?? "kindless"} license exception and must be used by test projects only.");
                }
            }
        }

        return violations;
    }

    public static bool IsApprovedWindowsNativeRuntime(LicenseException exception) =>
        string.Equals(exception.Kind, WindowsNativeRuntimeKind, StringComparison.Ordinal)
        && string.Equals(exception.Id, ApprovedWindowsNativeRuntime.Id, StringComparison.OrdinalIgnoreCase)
        && string.Equals(exception.Version, ApprovedWindowsNativeRuntime.Version, StringComparison.Ordinal)
        && string.Equals(exception.License, ApprovedWindowsNativeRuntime.License, StringComparison.Ordinal);

    private static string? Text(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    // A pre-release such as 8.0.0-beta.1 is treated as 8.0.0, so a forbidden line cannot be entered through its pre-releases.
    private static Version ParseVersion(string version) => Version.Parse(version.Split('-', '+')[0]);
}
