using System.Text.RegularExpressions;
using Governance.Auditor.Common;

namespace Governance.Auditor.Security;

/// <summary>
/// Every author, committer, and co-author email in the pull request range is a noreply address (ADR-0006). A personal email in a commit
/// is published for good once the history is public. The finding names the commit and the role, and shows only the domain, so the
/// check does not repeat the address in a log.
/// </summary>
internal sealed partial class CommitIdentityRule : ISecurityRule
{
    private const char FieldSeparator = '\u001f';
    private const char RecordSeparator = '\u001e';

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._/@^~+-]*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SafeReferencePattern();

    [GeneratedRegex(@"^\s*Co-authored-by:.*<(?<email>[^<>]*)>\s*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CoAuthorPattern();

    public string Id => "commit-identity";

    public async Task<IReadOnlyList<Finding>> EvaluateAsync(SecurityContext context, CancellationToken cancellationToken)
    {
        if (context.Range is null)
        {
            return [Finding.Warning(Id, "No --base was given, so no commit was checked. In a pull request, pass the base and head commits.")];
        }

        foreach (var reference in new[] { context.Range.Base, context.Range.Head })
        {
            // A reference that starts with a dash could be read as a Git option, so only plain reference characters are accepted.
            if (!SafeReferencePattern().IsMatch(reference))
            {
                throw new GovernanceException($"'{reference}' is not a valid Git reference for the commit range.");
            }
        }

        var allowed = context.Policy.CommitIdentity.AllowedEmailPatterns.Select(pattern => SafeRegex.Create(pattern, SecurityPolicy.Path)).ToList();
        var format = $"%H{FieldSeparator}%ae{FieldSeparator}%ce{FieldSeparator}%b{RecordSeparator}";
        var request = new ProcessRequest("git", ["log", $"--format={format}", $"{context.Range.Base}..{context.Range.Head}"], context.Files.Root);
        var result = await context.Processes.RunAsync(request, cancellationToken);

        if (!result.Succeeded)
        {
            throw new GovernanceException($"Git could not list the commits in {context.Range.Base}..{context.Range.Head}. Fetch the full history first. {result.StandardError.Trim()}");
        }

        List<Finding> findings = [];
        var commits = result.StandardOutput.Split(RecordSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var commit in commits)
        {
            var fields = commit.Split(FieldSeparator);

            if (fields.Length < 4)
            {
                continue;
            }

            var sha = fields[0][..Math.Min(7, fields[0].Length)];
            var identities = new List<(string Role, string Email)> { ("author", fields[1]), ("committer", fields[2]) };
            identities.AddRange(CoAuthorPattern().Matches(fields[3]).Select(match => ("co-author", match.Groups["email"].Value.Trim())));

            foreach (var (role, email) in identities.Where(identity => !allowed.Any(pattern => pattern.IsMatch(identity.Email))))
            {
                findings.Add(Finding.Error(Id, $"Commit {sha}: the {role} email is not a GitHub noreply address (domain: {DomainOf(email)}). Rewrite the commit with the repository noreply identity (ADR-0006).", null, null));
            }
        }

        if (commits.Length == 0)
        {
            findings.Add(Finding.Note(Id, $"No commits were found in {context.Range.Base}..{context.Range.Head}."));
        }

        return findings;
    }

    private static string DomainOf(string email)
    {
        var at = email.LastIndexOf('@');
        return at < 0 || at == email.Length - 1 ? "none" : email[(at + 1)..];
    }
}
