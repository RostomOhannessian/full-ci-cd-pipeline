using Catalog.Domain.Common;

namespace Catalog.Domain.Categories;

/// <summary>
/// A group of products. A category is active until it is discontinued, and a discontinued category stays in the catalog and keeps its products,
/// but no product can be assigned to it afterwards (plan section 8.3). Renaming is allowed in either state.
/// </summary>
public sealed class Category : AggregateRoot<CategoryId>
{
    private Category(CategoryId id, CategoryName name, DateTimeOffset now)
        : base(id)
    {
        Name = name;
        Status = LifecycleStatus.Active;
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Gets the name.</summary>
    public CategoryName Name { get; private set; }

    /// <summary>Gets the lifecycle status.</summary>
    public LifecycleStatus Status { get; private set; }

    /// <summary>Gets a value that says whether a product can be assigned to the category.</summary>
    public bool IsActive => Status == LifecycleStatus.Active;

    /// <summary>Gets when the category was created, in UTC.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Gets when the category last changed, in UTC.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Creates an active category and records <see cref="CategoryCreated"/>.</summary>
    /// <param name="id">The identifier, which the caller made with its identifier generator.</param>
    /// <param name="name">The name.</param>
    /// <param name="clock">The source of the current time.</param>
    public static Category Create(CategoryId id, CategoryName name, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(clock);

        var now = clock.GetUtcNow();
        var category = new Category(id, name, now);
        category.Raise(new CategoryCreated(id, name, now));

        return category;
    }

    /// <summary>Gives the category a new name and records <see cref="CategoryRenamed"/>. The same name changes nothing and records nothing.</summary>
    /// <param name="name">The new name.</param>
    /// <param name="clock">The source of the current time.</param>
    public void Rename(CategoryName name, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(clock);

        if (name == Name)
        {
            return;
        }

        var now = clock.GetUtcNow();
        Raise(new CategoryRenamed(Id, Name, name, now));
        Name = name;
        UpdatedAt = now;
    }

    /// <summary>
    /// Discontinues the category and records <see cref="CategoryDiscontinued"/>. Discontinuing a category that is already discontinued changes nothing and
    /// records nothing, so the action is idempotent (plan section 8.3).
    /// </summary>
    /// <param name="clock">The source of the current time.</param>
    public void Discontinue(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (!IsActive)
        {
            return;
        }

        var now = clock.GetUtcNow();
        Status = LifecycleStatus.Discontinued;
        UpdatedAt = now;
        Raise(new CategoryDiscontinued(Id, now));
    }
}
