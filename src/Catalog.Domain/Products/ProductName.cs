using Catalog.Domain.Common;

namespace Catalog.Domain.Products;

/// <summary>
/// The name of a product: one to 200 characters of one line of text, with the white space around it trimmed. Characters are Unicode characters, as
/// in the contract's <c>maxLength</c>. Trimming twice changes nothing, so a name that a client sends back is the same name.
/// </summary>
public sealed record ProductName
{
    /// <summary>The most characters of a name.</summary>
    public const int MaxLength = 200;

    private ProductName(string value) => Value = value;

    /// <summary>Gets the trimmed name.</summary>
    public string Value { get; }

    /// <summary>Normalizes a name, or reports which rule it breaks.</summary>
    /// <param name="raw">The name as a caller sent it.</param>
    public static DomainResult<ProductName> Create(string? raw)
    {
        var name = TextRules.Name(raw, "name", "name", MaxLength);

        return name.IsSuccess ? DomainResult.Success<ProductName>(new ProductName(name.Value)) : DomainResult.Failure<ProductName>(name.Error);
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
