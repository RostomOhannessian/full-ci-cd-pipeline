using System.Text.Json;
using Governance.Auditor.Common;

namespace Governance.Auditor.Security;

/// <summary>
/// Keeps the dependency license policy honest (ADR-0004, ADR-0020). It reads the same policy files as the license gate and the
/// architecture tests: the allow-list must not admit a license that ADR-0004 rejects, a tool-only exception must stay out of production
/// projects, and no resolved package in any lock file may be on the forbidden list.
/// </summary>
internal sealed class LicensePolicyRule : SyncSecurityRule
{
    private const string PoliciesDirectory = "governance/policies";
    private const string AllowListPath = PoliciesDirectory + "/license-policy.json";
    private const string OverridesPath = PoliciesDirectory + "/license-overrides.json";
    private const string ForbiddenPath = PoliciesDirectory + "/forbidden-packages.json";

    // The property that nuget-license reads for the package ID in an override entry.
    private const string OverrideIdProperty = "Id";

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

        using var document = JsonDocument.Parse(context.Files.ReadAllText(OverridesPath));
        var exceptions = document.RootElement.EnumerateArray()
            .Select(entry => entry.TryGetProperty(OverrideIdProperty, out var packageId) ? packageId.GetString() : null)
            .Where(packageId => packageId is not null)
            .Select(packageId => packageId!)
            .ToList();

        foreach (var lockFile in lockFiles.Where(path => path.StartsWith("src/", StringComparison.Ordinal)))
        {
            foreach (var package in ResolvedPackages(context.Files.ReadAllText(lockFile)).Where(package => exceptions.Contains(package.PackageId, StringComparer.OrdinalIgnoreCase)))
            {
                findings.Add(Finding.Error(Id, $"{package.PackageId} has a tool-only license exception but is used by a production project. The exception covers test-time tools only (ADR-0004).", lockFile));
            }
        }
    }

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

                if (Version.TryParse(resolved.Split('-', '+')[0], out var version))
                {
                    packages.Add(new ResolvedPackage(package.Name, resolved, version));
                }
            }
        }

        return packages.Distinct();
    }

    private sealed record ResolvedPackage(string PackageId, string Resolved, Version Version);
}
