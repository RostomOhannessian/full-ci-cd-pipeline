using Governance.Auditor.Common;

namespace Governance.Auditor.Security;

/// <summary>
/// Looks for text that has the shape of a credential: a token, a key, or a private key block. It reports the rule, the file, and the line,
/// and never the value, because a finding reaches logs and job summaries. It complements gitleaks, which scans the Git history, and
/// push protection (REQ-SEC-001).
/// </summary>
internal sealed class SecretPatternRule : SyncSecurityRule
{
    private const int MaxFileBytes = 1024 * 1024;
    private const int BinaryProbeBytes = 8192;

    public override string Id => "secret-patterns";

    protected override IReadOnlyList<Finding> Evaluate(SecurityContext context)
    {
        var patterns = context.Policy.SecretPatterns.Select(pattern => (pattern.Id, pattern.Description, Regex: pattern.Compile())).ToList();
        List<Finding> findings = [];

        foreach (var path in context.Files.ListFiles().Where(path => !Glob.IsMatchAny(context.Policy.SecretScan.ExcludePaths, path)))
        {
            var full = context.Files.Resolve(path);

            if (new FileInfo(full).Length > MaxFileBytes || LooksBinary(full))
            {
                continue;
            }

            var lines = File.ReadAllLines(full);

            for (var index = 0; index < lines.Length; index++)
            {
                foreach (var (id, description, regex) in patterns)
                {
                    if (regex.IsMatch(lines[index]))
                    {
                        findings.Add(Finding.Error(Id, $"The line looks like {description} (pattern '{id}'). The value is not shown. Remove it, rotate the credential if it was real, and use a secret store.", path, index + 1));
                    }
                }
            }
        }

        return findings;
    }

    private static bool LooksBinary(string fullPath)
    {
        using var stream = File.OpenRead(fullPath);
        var buffer = new byte[BinaryProbeBytes];
        var read = stream.Read(buffer, 0, buffer.Length);
        return Array.IndexOf(buffer, (byte)0, 0, read) >= 0;
    }
}
