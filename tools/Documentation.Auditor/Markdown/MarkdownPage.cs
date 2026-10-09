using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Governance.Auditor.Common;
using Governance.Auditor.Documents;

namespace Documentation.Auditor.Markdown;

internal sealed record Heading(int Level, string Text, int Line);

/// <summary>A fenced code block. <paramref name="Words"/> is the info string split at spaces, and the first word is the language.</summary>
internal sealed record CodeFence(int Line, string Language, IReadOnlyList<string> Words, IReadOnlyList<string> Content);

/// <summary>A DocFX include: <c>[!include[...](file)]</c> for Markdown, or <c>[!code-lang[...](file#region)]</c> for a code snippet.</summary>
/// <param name="Line">The one-based line of the include.</param>
/// <param name="IsCode">True for a code snippet, false for a Markdown include.</param>
/// <param name="Target">The path as written, without the region or the query.</param>
/// <param name="Region">The named region to read, from <c>#region</c> or <c>?name=region</c>.</param>
/// <param name="Range">The line range to read, from <c>?range=</c>, as written.</param>
internal sealed record Include(int Line, bool IsCode, string Target, string? Region, string? Range);

/// <summary>
/// A Markdown page, read the way the documentation rules need it: the front matter, the headings, the fenced blocks, and the includes. Text
/// inside a fenced block is never read as a heading or an include, and neither is text inside an inline code span, so a page can show the
/// include syntax without using it.
/// </summary>
internal sealed partial class MarkdownPage
{
    [GeneratedRegex(@"^ {0,3}(?<fence>`{3,}|~{3,})(?<info>.*)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FenceOpenPattern();

    [GeneratedRegex(@"^ {0,3}(?<fence>`{3,}|~{3,})\s*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FenceClosePattern();

    [GeneratedRegex(@"^ {0,3}(?<marks>#{1,6})[ \t]+(?<text>.+?)(?:[ \t]+#+)?[ \t]*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"`[^`\n]*`", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex InlineCodePattern();

    [GeneratedRegex(@"\[!(?<kind>code(?:-[A-Za-z0-9+#_.-]*)?|include)\s*\[[^\]]*\]\((?<target>[^)\s]*)(?:\s+""[^""]*"")?\)\]", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex IncludePattern();

    private MarkdownPage(string path, IReadOnlyList<string> lines)
    {
        Path = path;
        Lines = lines;
    }

    /// <summary>The repository-relative path with forward slashes.</summary>
    public string Path { get; }

    public IReadOnlyList<string> Lines { get; }

    /// <summary>The front matter as a tree, or null when the page has none or it cannot be read. <see cref="FrontMatterError"/> says why.</summary>
    public JsonObject? FrontMatter { get; private set; }

    public bool HasFrontMatter { get; private set; }

    public string? FrontMatterError { get; private set; }

    public IReadOnlyList<Heading> Headings { get; private set; } = [];

    public IReadOnlyList<CodeFence> Fences { get; private set; } = [];

    public IReadOnlyList<Include> Includes { get; private set; } = [];

    public static MarkdownPage Parse(string path, string text)
    {
        var page = new MarkdownPage(path, text.ReplaceLineEndings("\n").Split('\n'));
        var bodyStart = page.ReadFrontMatter();
        page.ReadBody(bodyStart);
        return page;
    }

    /// <summary>The lines under a level-2 heading, without blank lines and HTML comments, or null when the page has no such heading.</summary>
    public IReadOnlyList<string>? Section(string title)
    {
        var heading = Headings.FirstOrDefault(candidate => candidate.Level == 2 && string.Equals(candidate.Text, title, StringComparison.Ordinal));

        if (heading is null)
        {
            return null;
        }

        var end = Headings.FirstOrDefault(candidate => candidate.Line > heading.Line && candidate.Level <= 2)?.Line ?? Lines.Count + 1;
        var content = new List<string>();
        var inComment = false;

        for (var index = heading.Line; index < end - 1 && index < Lines.Count; index++)
        {
            var line = Lines[index].Trim();

            if (inComment)
            {
                inComment = !line.Contains("-->", StringComparison.Ordinal);
                continue;
            }

            if (line.StartsWith("<!--", StringComparison.Ordinal))
            {
                inComment = !line.Contains("-->", StringComparison.Ordinal);
                continue;
            }

            if (line.Length > 0)
            {
                content.Add(line);
            }
        }

        return content;
    }

    private int ReadFrontMatter()
    {
        if (Lines.Count == 0 || !string.Equals(Lines[0].TrimEnd(), "---", StringComparison.Ordinal))
        {
            return 0;
        }

        HasFrontMatter = true;
        var end = -1;

        for (var index = 1; index < Lines.Count; index++)
        {
            if (string.Equals(Lines[index].TrimEnd(), "---", StringComparison.Ordinal))
            {
                end = index;
                break;
            }
        }

        if (end < 0)
        {
            FrontMatterError = "The front matter starts with '---' and is never closed.";
            return Lines.Count;
        }

        try
        {
            var node = YamlDocument.Parse(string.Join('\n', Lines.Skip(1).Take(end - 1)), Path);

            if (node is JsonObject mapping)
            {
                FrontMatter = mapping;
            }
            else if (node is null)
            {
                FrontMatter = [];
            }
            else
            {
                FrontMatterError = "The front matter must be a mapping of keys to values.";
            }
        }
        catch (GovernanceException exception)
        {
            FrontMatterError = exception.Message;
        }

        return end + 1;
    }

    private void ReadBody(int start)
    {
        List<Heading> headings = [];
        List<CodeFence> fences = [];
        List<Include> includes = [];

        string? openFence = null;
        var openLine = 0;
        var openInfo = string.Empty;
        List<string> content = [];

        for (var index = start; index < Lines.Count; index++)
        {
            var line = Lines[index];

            if (openFence is not null)
            {
                var close = FenceClosePattern().Match(line);

                if (close.Success && close.Groups["fence"].Value[0] == openFence[0] && close.Groups["fence"].Length >= openFence.Length)
                {
                    fences.Add(CreateFence(openLine, openInfo, content));
                    openFence = null;
                }
                else
                {
                    content.Add(line);
                }

                continue;
            }

            var open = FenceOpenPattern().Match(line);

            if (open.Success && !(open.Groups["fence"].Value[0] == '`' && open.Groups["info"].Value.Contains('`', StringComparison.Ordinal)))
            {
                openFence = open.Groups["fence"].Value;
                openLine = index + 1;
                openInfo = open.Groups["info"].Value.Trim();
                content = [];
                continue;
            }

            if (HeadingPattern().Match(line) is { Success: true } heading)
            {
                headings.Add(new Heading(heading.Groups["marks"].Length, heading.Groups["text"].Value.Trim(), index + 1));
            }

            var withoutCode = InlineCodePattern().Replace(line, string.Empty);

            foreach (Match include in IncludePattern().Matches(withoutCode))
            {
                includes.Add(ReadInclude(index + 1, include));
            }
        }

        // A fence that is never closed runs to the end of the page, as CommonMark says.
        if (openFence is not null)
        {
            fences.Add(CreateFence(openLine, openInfo, content));
        }

        Headings = headings;
        Fences = fences;
        Includes = includes;
    }

    private static CodeFence CreateFence(int line, string info, List<string> content)
    {
        var words = info.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new CodeFence(line, words.Length == 0 ? string.Empty : words[0].ToLowerInvariant(), words, content);
    }

    private static Include ReadInclude(int line, Match match)
    {
        var target = match.Groups["target"].Value;
        var isCode = !string.Equals(match.Groups["kind"].Value, "include", StringComparison.OrdinalIgnoreCase);
        string? region = null;
        string? range = null;

        var query = target.IndexOf('?', StringComparison.Ordinal);

        if (query >= 0)
        {
            foreach (var option in target[(query + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = option.Split('=', 2);

                if (parts.Length == 2 && string.Equals(parts[0], "name", StringComparison.OrdinalIgnoreCase))
                {
                    region = parts[1];
                }
                else if (parts.Length == 2 && string.Equals(parts[0], "range", StringComparison.OrdinalIgnoreCase))
                {
                    range = parts[1];
                }
            }

            target = target[..query];
        }

        var hash = target.IndexOf('#', StringComparison.Ordinal);

        if (hash >= 0)
        {
            region = target[(hash + 1)..];
            target = target[..hash];
        }

        return new Include(line, isCode, target, region, range);
    }
}
