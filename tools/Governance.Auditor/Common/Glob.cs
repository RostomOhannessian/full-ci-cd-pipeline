using System.Text.RegularExpressions;

namespace Governance.Auditor.Common;

/// <summary>
/// Matches repository-relative paths against the glob patterns of the policy files. <c>*</c> matches within one path segment,
/// <c>**</c> matches across segments, and <c>?</c> matches one character. Paths use forward slashes and are case sensitive.
/// </summary>
internal static class Glob
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    public static bool IsMatch(string pattern, string path) => ToRegex(pattern).IsMatch(path);

    public static bool IsMatchAny(IEnumerable<string> patterns, string path) => patterns.Any(pattern => IsMatch(pattern, path));

    public static Regex ToRegex(string pattern)
    {
        var expression = new System.Text.StringBuilder("^");

        for (var index = 0; index < pattern.Length; index++)
        {
            var character = pattern[index];

            if (character == '*')
            {
                if (index + 1 < pattern.Length && pattern[index + 1] == '*')
                {
                    if (index + 2 < pattern.Length && pattern[index + 2] == '/')
                    {
                        expression.Append("(?:.*/)?");
                        index += 2;
                    }
                    else
                    {
                        expression.Append(".*");
                        index += 1;
                    }
                }
                else
                {
                    expression.Append("[^/]*");
                }
            }
            else if (character == '?')
            {
                expression.Append("[^/]");
            }
            else
            {
                expression.Append(Regex.Escape(character.ToString()));
            }
        }

        expression.Append('$');
        return new Regex(expression.ToString(), RegexOptions.CultureInvariant, MatchTimeout);
    }
}
