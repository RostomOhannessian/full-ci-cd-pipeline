using System.Text.Json;
using Governance.Auditor.Common;

namespace Governance.Auditor.Security;

/// <summary>
/// Keeps the dependency license policy honest (ADR-0004, ADR-0020). It reads the same policy files as the license gate and the
/// architecture tests: the allow-list must not admit a license that ADR-0004 rejects, every license exception has a known kind, a tool-only
/// exception must stay out of production projects, a Windows native runtime exception covers only the package and version that ADR-0004
/// approves, and no resolved package in any lock file may be on the forbidden list.
/// </summary>
internal sealed class LicensePolicyRule : SyncSecurityRule
{
    private const string PoliciesDirectory = "governance/policies";
    private const string AllowListPath = PoliciesDirectory + "/license-policy.json";
    private const string OverridesPath = PoliciesDirectory + "/license-overrides.json";
    private const string ForbiddenPath = PoliciesDirectory + "/forbidden-packages.json";

    // The property that nuget-license reads for the package ID in an override entry.
    private const string OverrideIdProperty = "Id";

    // The kinds of license exception. The kind is a property of each override entry that nuget-license ignores.
    private const string ToolOnlyKind = "tool-only";
    private const string WindowsNativeKind = "windows-native-runtime";

    public override string Id => "license-policy";

    protected override IReadOnlyList<Finding> Evaluate(SecurityContext context)
    {
        List<Finding> findings = [];
        var lockFiles = context.Files.ListFiles().Where(path => path.EndsWith("/packages.lock.json", StringComparison.Ordinal)).ToList();

        CheckAllowList(context, findings);
        CheckOverrides(context, lockFiles, findings);
        CheckForbidden(context, lockFiles, findings);

        return findings;
    }

    private void CheckAllowList(SecurityContext context, List<Finding> findings)
    {
        if (!context.Files.FileExists(AllowListPath))
        {
            findings.Add(Finding.Error(Id, "The license allow-list does not exist, so the license gate has nothing to enforce.", AllowListPath));
            return;
        }

        using var document = JsonDocument.Parse(context.Files.ReadAllText(AllowListPath));
        var licenses = document.RootElement.EnumerateArray().Select(entry => entry.GetString() ?? string.Empty).ToList();

        foreach (var license in licenses)
        {
            if (context.Policy.Licenses.DeniedLicensePrefixes.Any(prefix => license.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                findings.Add(Finding.Error(Id, $"The allow-list admits '{license}', which ADR-0004 does not allow for linked dependencies. A tool-only exception belongs in {OverridesPath}, with an ADR.", AllowListPath));
            }
            else if (license.StartsWith("LicenseRef-", StringComparison.OrdinalIgnoreCase)
                && !context.Policy.Licenses.AllowedLicenseReferences.Contains(license, StringComparer.Ordinal))
            {
                findings.Add(Finding.Error(Id, $"The allow-list admits '{license}', which is not a license that {SecurityPolicy.Path} allows by reference.", AllowListPath));
            }
        }
    }

    private void CheckOverrides(SecurityContext context, IReadOnlyList<string> lockFiles, List<Finding> findings)
    {
        if (!context.Files.FileExists(OverridesPath))
        {
            return;
        }

        var overrides = ReadOverrides(context.Files.ReadAllText(OverridesPath));

        foreach (var entry in overrides)
        {
            CheckOverrideDefinition(context, entry, findings);
        }

        foreach (var lockFile in lockFiles)
        {
            var production = lockFile.StartsWith("src/", StringComparison.Ordinal);

            foreach (var package in ResolvedPackages(context.Files.ReadAllText(lockFile)))
            {
                foreach (var entry in overrides.Where(entry => string.Equals(entry.Id, package.PackageId, StringComparison.OrdinalIgnoreCase)))
                {
                    CheckExceptionUse(context, entry, package, lockFile, production, findings);
                }
            }
        }
    }

    private void CheckOverrideDefinition(SecurityContext context, LicenseOverride entry, List<Finding> findings)
    {
        switch (entry.Kind)
        {
            case ToolOnlyKind:
                break;

            case WindowsNativeKind:
                if (!IsApprovedWindowsNativeRuntime(context, entry))
                {
                    findings.Add(Finding.Error(Id, $"The override for {entry.Id} {entry.Version} claims the kind '{WindowsNativeKind}', and {SecurityPolicy.Path} does not approve that package, version, and license. A new native runtime exception needs a decision in ADR-0004.", OverridesPath));
                }

                if (string.IsNullOrWhiteSpace(entry.Scope))
                {
                    findings.Add(Finding.Error(Id, $"The override for {entry.Id} has the kind '{WindowsNativeKind}' and no scope. It must say which assets the exception covers.", OverridesPath));
                }

                if (string.IsNullOrWhiteSpace(entry.Reason))
                {
                    findings.Add(Finding.Error(Id, $"The override for {entry.Id} has the kind '{WindowsNativeKind}' and no reason. It must say why no shipped artifact contains the package.", OverridesPath));
                }

                break;

            default:
                findings.Add(Finding.Error(Id, $"The override for {entry.Id} has {(entry.Kind is null ? "no kind" : $"the kind '{entry.Kind}'")}. Every override is '{ToolOnlyKind}' or '{WindowsNativeKind}'.", OverridesPath));
                break;
        }
    }

    // A tool-only exception stays out of production projects. A Windows native runtime exception may appear in any project, but only at the exact
    // version that was approved, and only as a dependency of the driver that needs it, because its Windows assets reach no shipped artifact (ADR-0004).
    private void CheckExceptionUse(SecurityContext context, LicenseOverride entry, ResolvedPackage package, string lockFile, bool production, List<Finding> findings)
    {
        if (IsApprovedWindowsNativeRuntime(context, entry))
        {
            if (!string.Equals(package.Resolved, entry.Version, StringComparison.Ordinal))
            {
                findings.Add(Finding.Error(Id, $"{package.PackageId} {package.Resolved} is not the version {entry.Version} that the Windows native runtime exception covers. A new version needs a new decision in ADR-0004.", lockFile));
            }
            else if (string.Equals(package.Type, "Direct", StringComparison.Ordinal))
            {
                findings.Add(Finding.Error(Id, $"{package.PackageId} is referenced directly. The Windows native runtime exception covers it only as a transitive dependency of the driver that needs it (ADR-0004).", lockFile));
            }

            return;
        }

        if (production)
        {
            var kind = string.Equals(entry.Kind, ToolOnlyKind, StringComparison.Ordinal) ? "tool-only license exception" : "license exception that is not an approved Windows native runtime";
            findings.Add(Finding.Error(Id, $"{package.PackageId} has a {kind} but is used by a production project. The exception covers test-time tools only (ADR-0004).", lockFile));
        }
    }

    private static bool IsApprovedWindowsNativeRuntime(SecurityContext context, LicenseOverride entry) =>
        string.Equals(entry.Kind, WindowsNativeKind, StringComparison.Ordinal)
        && context.Policy.Licenses.AllowedWindowsNativeRuntimes.Any(approved =>
            string.Equals(approved.Id, entry.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(approved.Version, entry.Version, StringComparison.Ordinal)
            && string.Equals(approved.License, entry.License, StringComparison.Ordinal));

    private static List<LicenseOverride> ReadOverrides(string json)
    {
        using var document = JsonDocument.Parse(json);

        return
        [
            .. document.RootElement.EnumerateArray()
                .Where(entry => Text(entry, OverrideIdProperty) is not null)
                .Select(entry => new LicenseOverride(
                    Text(entry, OverrideIdProperty)!,
                    Text(entry, "Version"),
                    Text(entry, "License"),
                    Text(entry, "Kind"),
                    Text(entry, "Scope"),
                    Text(entry, "Reason"))),
        ];
    }

    private static string? Text(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private void CheckForbidden(SecurityContext context, IReadOnlyList<string> lockFiles, List<Finding> findings)
    {
        if (!context.Files.FileExists(ForbiddenPath))
        {
            findings.Add(Finding.Error(Id, "The forbidden-package list does not exist.", ForbiddenPath));
            return;
        }

        using var document = JsonDocument.Parse(context.Files.ReadAllText(ForbiddenPath));
        var forbidden = document.RootElement.GetProperty("forbidden").EnumerateArray()
            .Select(entry => (
                PackageId: entry.GetProperty("id").GetString()!,
                Minimum: entry.TryGetProperty("minVersion", out var minimum) ? Version.Parse(minimum.GetString()!) : null,
                Reason: entry.GetProperty("reason").GetString()!))
            .ToList();

        foreach (var lockFile in lockFiles)
        {
            foreach (var package in ResolvedPackages(context.Files.ReadAllText(lockFile)))
            {
                foreach (var rule in forbidden.Where(rule => string.Equals(rule.PackageId, package.PackageId, StringComparison.OrdinalIgnoreCase)))
                {
                    if (rule.Minimum is null || package.Version >= rule.Minimum)
                    {
                        findings.Add(Finding.Error(Id, $"{package.PackageId} {package.Resolved} is forbidden. {rule.Reason}", lockFile));
                    }
                }
            }
        }
    }

    // A pre-release such as 8.0.0-beta.1 counts as 8.0.0, so a forbidden line cannot be entered through its pre-releases.
    private static IEnumerable<ResolvedPackage> ResolvedPackages(string lockFileJson)
    {
        using var document = JsonDocument.Parse(lockFileJson);

        List<ResolvedPackage> packages = [];

        foreach (var framework in document.RootElement.GetProperty("dependencies").EnumerateObject())
        {
            foreach (var package in framework.Value.EnumerateObject().Where(package => package.Value.TryGetProperty("resolved", out _)))
            {
                var resolved = package.Value.GetProperty("resolved").GetString()!;
                var type = Text(package.Value, "type") ?? string.Empty;

                if (Version.TryParse(resolved.Split('-', '+')[0], out var version))
                {
                    packages.Add(new ResolvedPackage(package.Name, resolved, version, type));
                }
            }
        }

        return packages.Distinct();
    }

    private sealed record ResolvedPackage(string PackageId, string Resolved, Version Version, string Type);

    private sealed record LicenseOverride(string Id, string? Version, string? License, string? Kind, string? Scope, string? Reason);
}
