using System.Globalization;
using Catalog.Domain.Common;

namespace Catalog.Domain.Categories;

/// <summary>
/// The identifier of a category. It is a UUID version 7, so identifiers sort by creation time, and a value of this type is always one: the
/// constructor is private, and <see cref="Create"/> refuses anything else. The application creates new identifiers through its
/// <c>IIdGenerator</c> port (WP1.7), which uses <see cref="Guid.CreateVersion7()"/> by default, and tests pass identifiers that they built.
/// </summary>
public sealed record CategoryId
{
    private CategoryId(Guid value) => Value = value;

    /// <summary>Gets the UUID.</summary>
    public Guid Value { get; }

    /// <summary>Wraps a UUID, or reports that it is not a version 7 UUID.</summary>
    /// <param name="value">The UUID.</param>
    public static DomainResult<CategoryId> Create(Guid value) =>
        Uuid7.IsValid(value)
            ? DomainResult.Success<CategoryId>(new CategoryId(value))
            : DomainResult.Failure<CategoryId>(DomainError.Invalid("categoryId", "The category identifier must be a UUID version 7."));

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}
