using System.Text.RegularExpressions;
using Governance.Auditor.Common;

namespace Governance.Auditor.Trace;

/// <summary>A test that names a requirement it proves.</summary>
/// <param name="Requirement">The text that the test wrote, which may not be a valid ID.</param>
/// <param name="Path">The test file, relative to the repository root.</param>
/// <param name="Line">The line that names the requirement.</param>
internal sealed record TestReference(string Requirement, string Path, int Line);

/// <summary>
/// Finds the tests that declare which requirement they prove (plan section 10.2). A .NET test carries a
/// <c>[Trait("Requirement", "REQ-...")]</c> attribute. A test in any other language declares its IDs in a comment line such as
/// <c># requirements: REQ-QUA-004, REQ-QUA-005</c>, which fits Pester, Chainsaw, Kyverno, Terraform, and k6 files alike.
/// </summary>
internal static partial class ProofScanner
{
    private const int MaxFileBytes = 1024 * 1024;

    // .NET tests live under tests/. The other kinds of test may live beside the code they test.
    private static readonly string[] DotNetRoots = ["tests/"];
    private static readonly string[] MetadataRoots = ["tests/", "tools/", "scripts/", "policies/", "infra/"];
    private static readonly string[] MetadataExtensions = [".ps1", ".yaml", ".yml", ".hcl", ".tf", ".js", ".ts", ".sh"];

    [GeneratedRegex("""Trait\s*\(\s*"Requirement"\s*,\s*"(?<id>[^"]*)"\s*\)""", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TraitPattern();

    [GeneratedRegex(@"^\s*(?:#|//)\s*requirements?\s*:\s*(?<ids>.+?)\s*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MetadataPattern();

    [GeneratedRegex(@"[A-Za-z0-9_-]+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex WordPattern();

    public static IReadOnlyList<TestReference> Scan(RepositoryFiles files)
    {
        List<TestReference> references = [];

        foreach (var path in files.ListFiles())
        {
            var isDotNet = path.EndsWith(".cs", StringComparison.Ordinal) && DotNetRoots.Any(root => path.StartsWith(root, StringComparison.Ordinal));
            var isMetadata = MetadataExtensions.Any(extension => path.EndsWith(extension, StringComparison.Ordinal))
                && MetadataRoots.Any(root => path.StartsWith(root, StringComparison.Ordinal));

            if ((!isDotNet && !isMetadata) || new FileInfo(files.Resolve(path)).Length > MaxFileBytes)
            {
                continue;
            }

            var lines = files.ReadAllText(path).Split('\n');

            for (var index = 0; index < lines.Length; index++)
            {
                if (isDotNet)
                {
                    references.AddRange(TraitPattern().Matches(lines[index]).Select(match => new TestReference(match.Groups["id"].Value, path, index + 1)));
                }
                else if (MetadataPattern().Match(lines[index]) is { Success: true } metadata)
                {
                    references.AddRange(WordPattern().Matches(metadata.Groups["ids"].Value)
                        .Select(word => word.Value)
                        .Where(word => word.StartsWith("REQ-", StringComparison.Ordinal))
                        .Select(word => new TestReference(word, path, index + 1)));
                }
            }
        }

        return references;
    }
}
