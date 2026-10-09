using System.Text.RegularExpressions;
using Documentation.Auditor.Markdown;
using Documentation.Auditor.Policy;
using Governance.Auditor.Common;

namespace Documentation.Auditor.Pages;

/// <summary>The rules for fenced blocks: a hand-written command is marked illustrative, and a diagram has a title and a description.</summary>
internal static partial class BlockRules
{
    [GeneratedRegex(@"^[ \t]*accTitle[ \t]*:[ \t]*\S", RegexOptions.CultureInvariant | RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AccessibleTitlePattern();

    [GeneratedRegex(@"^[ \t]*accDescr[ \t]*(?::[ \t]*\S|\{)", RegexOptions.CultureInvariant | RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AccessibleDescriptionPattern();

    [GeneratedRegex(@"^(?:\$|PS>|>)\s+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PromptPattern();

    /// <summary>
    /// A command that a reader will copy must come from a file that CI runs, through a DocFX include, or be marked as illustrative on the
    /// fence: <c>```bash illustrative</c>. A block in a shell language is always a command. A block with no language, or with <c>text</c>,
    /// is a command when its first line starts with a command name from the policy, so output and file contents stay unmarked.
    /// </summary>
    public static void CheckCommandBlocks(MarkdownPage page, CommandBlockPolicy policy, List<Finding> findings)
    {
        if (!Glob.IsMatchAny(policy.Paths, page.Path))
        {
            return;
        }

        foreach (var fence in page.Fences)
        {
            var marked = fence.Words.Skip(1).Any(word => string.Equals(word, policy.Marker, StringComparison.OrdinalIgnoreCase));
            var isCommand = policy.Languages.Contains(fence.Language, StringComparer.OrdinalIgnoreCase)
                || (policy.PlainLanguages.Contains(fence.Language, StringComparer.OrdinalIgnoreCase) && StartsWithCommand(fence, policy));

            if (isCommand && !marked)
            {
                findings.Add(Finding.Error(
                    "command-block-untested",
                    $"A hand-written command block must come from a tested file through an include, such as [!code-bash[...](~/scripts/example.sh#region)], or be marked '{policy.Marker}' after the language, as in ```{(fence.Language.Length == 0 ? "bash" : fence.Language)} {policy.Marker}.",
                    page.Path,
                    fence.Line));
            }
        }
    }

    /// <summary>Mermaid accepts a diagram with no title or description, so the check is here. Rendering is checked by mermaid-cli.</summary>
    public static void CheckDiagrams(MarkdownPage page, List<Finding> findings)
    {
        foreach (var fence in page.Fences.Where(fence => string.Equals(fence.Language, "mermaid", StringComparison.Ordinal)))
        {
            var body = string.Join('\n', fence.Content);

            if (!AccessibleTitlePattern().IsMatch(body))
            {
                findings.Add(Finding.Error("diagram-accessibility", "The diagram has no accessible title. Add a line 'accTitle: ...' under the diagram type.", page.Path, fence.Line));
            }

            if (!AccessibleDescriptionPattern().IsMatch(body))
            {
                findings.Add(Finding.Error("diagram-accessibility", "The diagram has no accessible description. Add a line 'accDescr: ...' under the diagram type.", page.Path, fence.Line));
            }
        }
    }

    private static bool StartsWithCommand(CodeFence fence, CommandBlockPolicy policy)
    {
        var first = fence.Content
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length > 0 && !line.StartsWith('#') && !line.StartsWith("//", StringComparison.Ordinal));

        if (first is null)
        {
            return false;
        }

        first = PromptPattern().Replace(first, string.Empty);
        var token = first.Split([' ', '\t'], 2)[0];

        return policy.Commands.Any(command => command.EndsWith('/')
            ? first.StartsWith(command, StringComparison.Ordinal)
            : string.Equals(token, command, StringComparison.OrdinalIgnoreCase));
    }
}
