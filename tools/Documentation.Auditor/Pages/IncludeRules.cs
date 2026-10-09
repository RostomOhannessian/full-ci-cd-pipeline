using System.Globalization;
using System.Text.RegularExpressions;
using Documentation.Auditor.Markdown;
using Documentation.Auditor.Policy;
using Governance.Auditor.Common;

namespace Documentation.Auditor.Pages;

/// <summary>
/// Include verification (plan section 11.3). A tutorial shows commands and code by including them from files that CI builds or runs, so a
/// snippet cannot drift from the code. This rule proves that every include points at a real file inside the repository, that a code include
/// reads from a place CI covers, and that the region or the line range it names exists.
/// </summary>
internal static partial class IncludeRules
{
    [GeneratedRegex(@"^(?<from>\d*)(?:(?<dash>-)(?<to>\d*))?$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RangePartPattern();

    public static void Check(MarkdownPage page, IncludePolicy policy, RepositoryFiles files, List<Finding> findings)
    {
        foreach (var include in page.Includes)
        {
            var relative = Resolve(page.Path, include.Target);

            if (relative is null)
            {
                findings.Add(Finding.Error("include-outside-repository", $"The include '{include.Target}' leaves the repository, so it cannot be tested or built.", page.Path, include.Line));
                continue;
            }

            if (!files.FileExists(relative))
            {
                findings.Add(Finding.Error("include-target-missing", $"The include '{include.Target}' resolves to {relative}, which does not exist.", page.Path, include.Line));
                continue;
            }

            if (include.IsCode && !policy.AllowedRoots.Any(root => IsUnder(relative, root)))
            {
                findings.Add(Finding.Error(
                    "include-untested-source",
                    $"The code include reads {relative}, which is not under a path that CI builds or runs ({string.Join(", ", policy.AllowedRoots)}). Include from a tested file, or mark a hand-written block illustrative.",
                    page.Path,
                    include.Line));
                continue;
            }

            CheckRegionAndRange(page, include, relative, files, findings);
        }
    }

    /// <summary>Returns the repository-relative path of an include, or null when it leaves the repository. A path that starts with <c>~/</c> is relative to the root.</summary>
    internal static string? Resolve(string pagePath, string target)
    {
        var directory = System.IO.Path.GetDirectoryName(pagePath)?.Replace('\\', '/') ?? string.Empty;
        var combined = target.StartsWith("~/", StringComparison.Ordinal) ? target[2..] : $"{directory}/{target}";

        List<string> segments = [];

        foreach (var segment in combined.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count == 0)
                {
                    return null;
                }

                segments.RemoveAt(segments.Count - 1);
            }
            else
            {
                segments.Add(segment);
            }
        }

        return string.Join('/', segments);
    }

    private static bool IsUnder(string path, string root) =>
        root.EndsWith('/') ? path.StartsWith(root, StringComparison.Ordinal) : string.Equals(path, root, StringComparison.Ordinal);

    private static void CheckRegionAndRange(MarkdownPage page, Include include, string relative, RepositoryFiles files, List<Finding> findings)
    {
        if (include.Region is null && include.Range is null)
        {
            return;
        }

        var lines = files.ReadAllText(relative).ReplaceLineEndings("\n").Split('\n');

        if (include.Region is { } region)
        {
            // DocFX reads "#region name" and a named tag such as "<name>" inside any comment style.
            var pattern = $@"(?:#\s*region\s+{Regex.Escape(region)}\b|<{Regex.Escape(region)}>)";

            if (!lines.Any(line => Regex.IsMatch(line, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))))
            {
                findings.Add(Finding.Error("include-region-missing", $"{relative} has no region named '{region}'. Mark it with '#region {region}' or '<{region}>' in a comment.", page.Path, include.Line));
            }
        }

        if (include.Range is { } range && !RangeFits(range, lines.Length))
        {
            findings.Add(Finding.Error("include-range", $"The line range '{range}' does not fit {relative}, which has {lines.Length} lines.", page.Path, include.Line));
        }
    }

    /// <summary>A range is a comma-separated list of <c>5</c>, <c>3-9</c>, <c>3-</c>, or <c>-9</c>, and every number must be a line of the file.</summary>
    private static bool RangeFits(string range, int lineCount)
    {
        foreach (var part in range.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (RangePartPattern().Match(part) is not { Success: true } match || part.Length == 0)
            {
                return false;
            }

            var from = ToNumber(match.Groups["from"].Value);
            var to = ToNumber(match.Groups["to"].Value);

            if ((from is not null && (from < 1 || from > lineCount)) || (to is not null && (to < 1 || to > lineCount)) || (from is not null && to is not null && from > to))
            {
                return false;
            }
        }

        return true;
    }

    private static int? ToNumber(string text) =>
        text.Length == 0 ? null : int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : int.MaxValue;
}
