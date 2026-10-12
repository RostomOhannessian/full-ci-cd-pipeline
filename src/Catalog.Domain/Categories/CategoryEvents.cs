using Catalog.Domain.Common;

namespace Catalog.Domain.Categories;

/// <summary>A category was created.</summary>
/// <param name="CategoryId">The category.</param>
/// <param name="Name">The name that it was created with.</param>
/// <param name="OccurredAt">When it happened, in UTC.</param>
public sealed record CategoryCreated(CategoryId CategoryId, CategoryName Name, DateTimeOffset OccurredAt) : IDomainEvent
{
    /// <inheritdoc />
    public string EventType => nameof(CategoryCreated);

    /// <inheritdoc />
    public int SchemaVersion => 1;
}

/// <summary>A category got a new name.</summary>
/// <param name="CategoryId">The category.</param>
/// <param name="OldName">The name that it had.</param>
/// <param name="NewName">The name that it has now.</param>
/// <param name="OccurredAt">When it happened, in UTC.</param>
public sealed record CategoryRenamed(CategoryId CategoryId, CategoryName OldName, CategoryName NewName, DateTimeOffset OccurredAt) : IDomainEvent
{
    /// <inheritdoc />
    public string EventType => nameof(CategoryRenamed);

    /// <inheritdoc />
    public int SchemaVersion => 1;
}

/// <summary>A category was discontinued.</summary>
/// <param name="CategoryId">The category.</param>
/// <param name="OccurredAt">When it happened, in UTC.</param>
public sealed record CategoryDiscontinued(CategoryId CategoryId, DateTimeOffset OccurredAt) : IDomainEvent
{
    /// <inheritdoc />
    public string EventType => nameof(CategoryDiscontinued);

    /// <inheritdoc />
    public int SchemaVersion => 1;
}
