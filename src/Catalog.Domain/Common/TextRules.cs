using System.Buffers;
using System.Text;

namespace Catalog.Domain.Common;

/// <summary>What is wrong with a piece of text, if anything.</summary>
internal enum TextProblem
{
    None,
    TooLong,
    ControlCharacter,
    InvalidUnicode,
}

/// <summary>
/// The checks that the text value objects share. Length is counted in Unicode characters, which are code points, because that is what
/// <c>minLength</c> and <c>maxLength</c> count in JSON Schema, and the contract states its bounds that way. A .NET string counts UTF-16 code units,
/// so an emoji is two of them and one character here, and the two counts differ for exactly the text that a naive check would get wrong.
/// </summary>
internal static class TextRules
{
    /// <summary>Counts the characters of a text and reports the first problem that it has.</summary>
    /// <param name="text">The text to inspect.</param>
    /// <param name="maxLength">The largest number of characters that the text may have.</param>
    /// <param name="allowLineBreaks">Whether a tab, a line feed, and a carriage return are allowed. Every other control character is refused.</param>
    /// <param name="length">The number of characters, which is not counted past a problem.</param>
    public static TextProblem Inspect(string text, int maxLength, bool allowLineBreaks, out int length)
    {
        length = 0;
        var remaining = text.AsSpan();

        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out var rune, out var consumed) != OperationStatus.Done)
            {
                return TextProblem.InvalidUnicode;
            }

            if (Rune.IsControl(rune) && !(allowLineBreaks && IsLineBreakOrTab(rune)))
            {
                return TextProblem.ControlCharacter;
            }

            length++;
            if (length > maxLength)
            {
                return TextProblem.TooLong;
            }

            remaining = remaining[consumed..];
        }

        return TextProblem.None;
    }

    private static bool IsLineBreakOrTab(Rune rune) => rune.Value is '\t' or '\n' or '\r';

    /// <summary>
    /// Trims a name and checks it: one to <paramref name="maxLength"/> characters, valid Unicode, and no control characters, so a name is one line of
    /// text. The messages are the ones of the contract's examples.
    /// </summary>
    /// <param name="raw">The name as a caller sent it.</param>
    /// <param name="field">The path of the value, for the error.</param>
    /// <param name="label">What to call the value in the message, such as <c>name</c>.</param>
    /// <param name="maxLength">The largest number of characters.</param>
    public static DomainResult<string> Name(string? raw, string field, string label, int maxLength)
    {
        var trimmed = raw?.Trim() ?? string.Empty;
        var problem = trimmed.Length == 0 ? TextProblem.TooLong : Inspect(trimmed, maxLength, allowLineBreaks: false, out _);

        return problem switch
        {
            TextProblem.None => DomainResult.Success<string>(trimmed),
            TextProblem.ControlCharacter => DomainResult.Failure<string>(DomainError.Invalid(field, $"The {label} must not contain control characters.")),
            TextProblem.InvalidUnicode => DomainResult.Failure<string>(DomainError.Invalid(field, $"The {label} must be valid Unicode text.")),
            _ => DomainResult.Failure<string>(DomainError.Invalid(field, $"The {label} must have between 1 and {maxLength} characters.")),
        };
    }
}
