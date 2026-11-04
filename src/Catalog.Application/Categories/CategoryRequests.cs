using Catalog.Application.Idempotency;
using Catalog.Application.Messaging;
using Catalog.Application.Paging;
using Catalog.Application.Persistence;
using Catalog.Application.Security;
using Catalog.Domain.Categories;
using Catalog.Domain.Common;

namespace Catalog.Application.Categories;

/// <summary>A category as a caller sees it. The values are plain, so the API maps them to the contract and the idempotency store keeps them as JSON.</summary>
/// <param name="Id">The identifier.</param>
/// <param name="Name">The name.</param>
/// <param name="Status">The lifecycle status.</param>
/// <param name="CreatedAt">When the category was created.</param>
/// <param name="UpdatedAt">When the category last changed.</param>
/// <param name="ETag">The version of the category as the token of an <see cref="EntityTag"/>, which the API writes as a strong <c>ETag</c>.</param>
public sealed record CategoryView(Guid Id, string Name, LifecycleStatus Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string ETag)
{
    /// <summary>Describes a category that the repository loaded or saved.</summary>
    /// <param name="category">The category.</param>
    /// <param name="tag">The version of the category.</param>
    public static CategoryView From(Category category, EntityTag tag)
    {
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(tag);

        return new CategoryView(category.Id.Value, category.Name.Value, category.Status, category.CreatedAt, category.UpdatedAt, tag.Value);
    }
}

/// <summary>Creates a category (<c>createCategory</c>). It needs the admin level.</summary>
/// <param name="Name">The name of the category.</param>
/// <param name="IdempotencyKey">The key of the request, or null when the caller sent none.</param>
public sealed record CreateCategory(string? Name, IdempotencyKey? IdempotencyKey = null) : IProtectedRequest, IIdempotentCommand
{
    /// <inheritdoc />
    public static AccessLevel RequiredAccess => AccessLevel.Admin;
}

/// <summary>Renames a category (<c>updateCategory</c>). It needs the admin level and the current version.</summary>
/// <param name="CategoryId">The identifier of the category.</param>
/// <param name="Name">The new name.</param>
/// <param name="IfMatch">The version that the caller read, or null when it sent none.</param>
public sealed record RenameCategory(Guid CategoryId, string? Name, EntityTag? IfMatch) : IProtectedRequest
{
    /// <inheritdoc />
    public static AccessLevel RequiredAccess => AccessLevel.Admin;
}

/// <summary>Discontinues a category (<c>discontinueCategory</c>). It needs the admin level and the current version, and it changes nothing for a category that is already discontinued.</summary>
/// <param name="CategoryId">The identifier of the category.</param>
/// <param name="IfMatch">The version that the caller read, or null when it sent none.</param>
public sealed record DiscontinueCategory(Guid CategoryId, EntityTag? IfMatch) : IProtectedRequest
{
    /// <inheritdoc />
    public static AccessLevel RequiredAccess => AccessLevel.Admin;
}

/// <summary>Reads one category (<c>getCategory</c>). It needs the read level.</summary>
/// <param name="CategoryId">The identifier of the category.</param>
public sealed record GetCategory(Guid CategoryId) : IProtectedRequest
{
    /// <inheritdoc />
    public static AccessLevel RequiredAccess => AccessLevel.Read;
}

/// <summary>Reads a page of categories ordered by name (<c>listCategories</c>). It needs the read level.</summary>
/// <param name="PageSize">The number of items that the caller wants.</param>
/// <param name="Cursor">The cursor of the page, or null for the first one.</param>
/// <param name="Status">Only the categories with this status, or null for every status.</param>
public sealed record ListCategories(int PageSize = PageRequest.DefaultSize, string? Cursor = null, LifecycleStatus? Status = null) : IProtectedRequest
{
    /// <summary>The name of the only sort order of categories, which their cursors name.</summary>
    public const string SortName = "name";

    /// <inheritdoc />
    public static AccessLevel RequiredAccess => AccessLevel.Read;
}

/// <summary>
/// Reads categories for the query side. The persistence layer implements it as a read-only projection (WP1.8), so a query handler never loads an aggregate.
/// </summary>
public interface ICategoryReadStore
{
    /// <summary>Reads one category, or returns null when no category has the identifier.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    Task<CategoryView?> GetAsync(CategoryId id, CancellationToken cancellationToken);

    /// <summary>Reads up to <paramref name="limit"/> categories with a status, ordered by name and then by identifier, after a position.</summary>
    /// <param name="status">The status to keep, or null for every status.</param>
    /// <param name="after">The position after which to start, or null for the first page.</param>
    /// <param name="limit">The largest number of categories to return.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    Task<IReadOnlyList<CategoryView>> ListAsync(LifecycleStatus? status, KeysetPosition? after, int limit, CancellationToken cancellationToken);
}
