using Governance.Auditor.Common;

namespace Governance.Auditor.Security;

/// <summary>The commits of a pull request: everything reachable from <see cref="Head"/> and not from <see cref="Base"/>.</summary>
internal sealed record GitRange(string Base, string Head);

/// <summary>What a rule can see. The policy is data, and the repository is read through <see cref="RepositoryFiles"/>.</summary>
internal sealed class SecurityContext
{
    private IReadOnlyList<WorkflowFile>? _workflows;

    public SecurityContext(RepositoryFiles files, SecurityPolicy policy, IProcessRunner processes, GitRange? range)
    {
        Files = files;
        Policy = policy;
        Processes = processes;
        Range = range;
    }

    public RepositoryFiles Files { get; }

    public SecurityPolicy Policy { get; }

    public IProcessRunner Processes { get; }

    public GitRange? Range { get; }

    /// <summary>The workflow files, parsed once. A file that is not valid YAML is reported by the <c>workflow-parse</c> rule.</summary>
    public IReadOnlyList<WorkflowFile> Workflows => _workflows ??= WorkflowFile.LoadAll(Files);
}

/// <summary>One check of the security auditor. A rule has a stable ID so a policy, a test, or a skill can name it.</summary>
internal interface ISecurityRule
{
    string Id { get; }

    Task<IReadOnlyList<Finding>> EvaluateAsync(SecurityContext context, CancellationToken cancellationToken);
}

/// <summary>A rule that needs no I/O beyond reading files.</summary>
internal abstract class SyncSecurityRule : ISecurityRule
{
    public abstract string Id { get; }

    public Task<IReadOnlyList<Finding>> EvaluateAsync(SecurityContext context, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Finding>>(Evaluate(context));

    protected abstract IReadOnlyList<Finding> Evaluate(SecurityContext context);
}

internal static class SecurityAuditor
{
    public static IReadOnlyList<ISecurityRule> AllRules { get; } =
    [
        new SecretPatternRule(),
        new WorkflowParseRule(),
        new WorkflowPermissionsRule(),
        new ActionPinningRule(),
        new WorkflowSecretsRule(),
        new CheckoutCredentialsRule(),
        new PullRequestTargetRule(),
        new WorkflowLimitsRule(),
        new LicensePolicyRule(),
        new CommitIdentityRule(),
    ];

    /// <summary>Runs the rules, or only the named ones. An unknown rule name is an error, so a typo cannot skip a check silently.</summary>
    public static async Task<IReadOnlyList<Finding>> RunAsync(SecurityContext context, IReadOnlyCollection<string> only, CancellationToken cancellationToken)
    {
        var unknown = only.Where(name => AllRules.All(rule => !string.Equals(rule.Id, name, StringComparison.Ordinal))).ToList();

        if (unknown.Count > 0)
        {
            throw new GovernanceException($"Unknown rule '{string.Join("', '", unknown)}'. The rules are: {string.Join(", ", AllRules.Select(rule => rule.Id))}.");
        }

        List<Finding> findings = [];

        foreach (var rule in AllRules.Where(rule => only.Count == 0 || only.Contains(rule.Id, StringComparer.Ordinal)))
        {
            findings.AddRange(await rule.EvaluateAsync(context, cancellationToken));
        }

        return findings;
    }
}
