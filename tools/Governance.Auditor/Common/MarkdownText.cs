using System.Text;

namespace Governance.Auditor.Common;

/// <summary>
/// Helpers for Markdown output. Text from a pull request, a commit, or a test result is untrusted, so it is flattened to one line and
/// escaped before it enters a table or a job summary.
/// </summary>
internal static class MarkdownText
{
    /// <summary>Escapes text for a table cell: no line breaks, no pipes, no raw HTML, and no control characters.</summary>
    public static string Cell(string? text, int maxLength = 300)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);

        foreach (var character in text)
        {
            builder.Append(character switch
            {
                '|' => "\\|",
                '<' => "&lt;",
                '>' => "&gt;",
                '`' => "'",
                '\r' or '\n' or '\t' => " ",
                _ when char.IsControl(character) => string.Empty,
                _ => character.ToString(),
            });
        }

        var flattened = builder.ToString().Trim();
        return flattened.Length <= maxLength ? flattened : string.Concat(flattened.AsSpan(0, maxLength - 3), "...");
    }

    /// <summary>A code span for a short value such as a path or a rule ID. Backticks inside the value are replaced.</summary>
    public static string Code(string text) => $"`{Cell(text).Replace("\\|", "|", StringComparison.Ordinal)}`";

    public static string Table(IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string>> rows)
    {
        var builder = new StringBuilder();
        builder.Append("| ").AppendJoin(" | ", header).Append(" |\n");
        builder.Append("| ").AppendJoin(" | ", header.Select(_ => "---")).Append(" |\n");

        foreach (var row in rows)
        {
            builder.Append("| ").AppendJoin(" | ", row).Append(" |\n");
        }

        return builder.ToString();
    }

    /// <summary>Appends Markdown to a file, which is how GitHub Actions collects a job summary.</summary>
    public static void Append(string path, string markdown)
    {
        File.AppendAllText(path, markdown.ReplaceLineEndings("\n"), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
