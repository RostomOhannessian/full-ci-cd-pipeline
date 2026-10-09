using System.Text.RegularExpressions;
using Governance.Auditor.Common;
using Governance.Auditor.Documents;

namespace Governance.Auditor.Security;

/// <summary>The typed view of <c>governance/policies/security-policy.yaml</c>. The rules read their settings from here, never from code.</summary>
internal sealed record SecurityPolicy
{
    public const string Path = "governance/policies/security-policy.yaml";

    public required int SchemaVersion { get; init; }

    public string? Description { get; init; }

    public required IReadOnlyList<SecretPattern> SecretPatterns { get; init; }

    public required SecretScanPolicy SecretScan { get; init; }

    public required WorkflowPolicy Workflows { get; init; }

    public required LicensePolicy Licenses { get; init; }

    public required CommitIdentityPolicy CommitIdentity { get; init; }

    public static SecurityPolicy Load(RepositoryFiles files)
    {
        var node = YamlDocument.Parse(files.ReadAllText(Path), Path)
            ?? throw new GovernanceException($"{Path}: the policy is empty.");
        var policy = DocumentJson.Deserialize<SecurityPolicy>(node, Path);

        if (policy.SchemaVersion != 1)
        {
            throw new GovernanceException($"{Path}: schema-version {policy.SchemaVersion} is not supported. This tool reads version 1.");
        }

        // Compile every pattern now, so a broken policy fails loudly before any file is scanned.
        foreach (var pattern in policy.SecretPatterns)
        {
            _ = pattern.Compile();
        }

        foreach (var pattern in policy.CommitIdentity.AllowedEmailPatterns)
        {
            _ = SafeRegex.Create(pattern, Path);
        }

        return policy;
    }
}

internal sealed record SecretPattern
{
    public required string Id { get; init; }

    public required string Description { get; init; }

    public required string Pattern { get; init; }

    public Regex Compile() => SafeRegex.Create(Pattern, $"{SecurityPolicy.Path} pattern '{Id}'");
}

internal sealed record SecretScanPolicy
{
    public IReadOnlyList<string> ExcludePaths { get; init; } = [];
}

internal sealed record WorkflowPolicy
{
    public IReadOnlyList<string> AllowedSecrets { get; init; } = [];

    public IReadOnlyList<WritePermissionException> AllowedWritePermissions { get; init; } = [];

    public IReadOnlyList<PullRequestTargetException> AllowedPullRequestTarget { get; init; } = [];
}

internal sealed record WritePermissionException
{
    public required string Workflow { get; init; }

    public required string Job { get; init; }

    public required string Permission { get; init; }

    public required string Reason { get; init; }
}

internal sealed record PullRequestTargetException
{
    public required string Workflow { get; init; }

    public required string Reason { get; init; }
}

internal sealed record LicensePolicy
{
    public IReadOnlyList<string> DeniedLicensePrefixes { get; init; } = [];

    public IReadOnlyList<string> AllowedLicenseReferences { get; init; } = [];
}

internal sealed record CommitIdentityPolicy
{
    public required IReadOnlyList<string> AllowedEmailPatterns { get; init; }
}

internal static class SafeRegex
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    public static Regex Create(string pattern, string source)
    {
        try
        {
            return new Regex(pattern, RegexOptions.CultureInvariant, Timeout);
        }
        catch (ArgumentException exception)
        {
            throw new GovernanceException($"{source}: the regular expression is not valid. {exception.Message}", exception);
        }
    }
}
