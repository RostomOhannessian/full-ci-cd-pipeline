using Catalog.Domain.Categories;
using Catalog.Domain.Products;

namespace Catalog.Application.Identity;

/// <summary>
/// Creates the identifiers of new aggregates. Both are UUIDs of version 7, so they sort by creation time. A test passes a generator that counts, so the identifiers are
/// the same on every run (plan sections 8.3 and 10.3).
/// </summary>
public interface IIdGenerator
{
    /// <summary>Creates the identifier of a new product.</summary>
    ProductId NewProductId();

    /// <summary>Creates the identifier of a new category.</summary>
    CategoryId NewCategoryId();
}

/// <summary>The default <see cref="IIdGenerator"/>. It calls <see cref="Guid.CreateVersion7(DateTimeOffset)"/> with the time of the clock, so a fake clock gives identifiers that sort in the order of the fake time.</summary>
/// <param name="clock">The clock.</param>
public sealed class GuidV7IdGenerator(TimeProvider clock) : IIdGenerator
{
    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <inheritdoc />
    public ProductId NewProductId() => ProductId.Create(Guid.CreateVersion7(_clock.GetUtcNow())).Value;

    /// <inheritdoc />
    public CategoryId NewCategoryId() => CategoryId.Create(Guid.CreateVersion7(_clock.GetUtcNow())).Value;
}
