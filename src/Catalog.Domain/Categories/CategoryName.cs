using Catalog.Domain.Common;

namespace Catalog.Domain.Categories;

/// <summary>
/// The name of a category: one to 100 characters of one line of text, with the white space around it trimmed. Characters are Unicode characters, as
/// in the contract's <c>maxLength</c>. Trimming twice changes nothing, so a name that a client sends back is the same name.
/// </summary>
public sealed record CategoryName
{
    /// <summary>The most characters of a name.</summary>
    public const int MaxLength = 100;

    private CategoryName(string value) => Value = value;

    /// <summary>Gets the trimmed name.</summary>
    public string Value { get; }

    /// <summary>Normalizes a name, or reports which rule it breaks.</summary>
    /// <param name="raw">The name as a caller sent it.</param>
    public static DomainResult<CategoryName> Create(string? raw)
    {
        var name = TextRules.Name(raw, "name", "name", MaxLength);

        return name.IsSuccess ? DomainResult.Success<CategoryName>(new CategoryName(name.Value)) : DomainResult.Failure<CategoryName>(name.Error);
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
