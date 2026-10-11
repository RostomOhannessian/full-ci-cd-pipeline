using System.Text.RegularExpressions;

namespace Catalog.Contract.Tests.Support;

/// <summary>A table of a Markdown page, read as rows of cells with the backticks removed.</summary>
internal static class MarkdownTable
{
    /// <summary>Reads the table that follows a heading, and stops at the first line that is not a table row.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> After(string markdown, string heading)
    {
        var lines = markdown.Split('\n');
        var start = Array.FindIndex(lines, line => line.Trim() == heading);
        if (start < 0)
        {
            throw new InvalidOperationException($"The page has no heading '{heading}'.");
        }

        var rows = lines
            .Skip(start + 1)
            .SkipWhile(line => !line.TrimStart().StartsWith('|'))
            .TakeWhile(line => line.TrimStart().StartsWith('|'))
            .Select(Cells)
            .Where(cells => !cells.All(cell => Regex.IsMatch(cell, "^:?-{3,}:?$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))))
            .ToList();

        // The first row holds the column names.
        return rows.Count > 0 ? rows.Skip(1).ToList() : rows;
    }

    private static IReadOnlyList<string> Cells(string line) =>
        [.. line.Trim().Trim('|').Split('|').Select(cell => cell.Trim().Trim('`'))];
}
