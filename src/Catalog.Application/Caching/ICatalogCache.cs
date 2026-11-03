using Catalog.Application.Results;

namespace Catalog.Application.Caching;

/// <summary>
/// The name and the tags of one cache entry. The adapter adds the environment and the version of the cache contract to the name (plan section 8.5, WP1.9), so the name here
/// holds only what identifies the data, such as <c>product:{id}</c>. A tag groups entries, so a write can remove every entry that it could have changed.
/// </summary>
/// <param name="Name">The name of the entry.</param>
/// <param name="Tags">The tags of the entry, such as <c>product:{id}</c> or <c>products:list</c>.</param>
public sealed record CacheKey(string Name, IReadOnlyList<string> Tags);

/// <summary>A query whose successful result may be kept in the cache. Only a query that implements this interface passes through the caching decorator (REQ-APP-003).</summary>
public interface ICachedQuery
{
    /// <summary>Gets the key of the result of this query. Two queries that must return the same result have the same name.</summary>
    CacheKey CacheKey { get; }
}

/// <summary>
/// The cache of query results. FusionCache implements it over a memory cache, a distributed cache, and a backplane (ADR-0016, ADR-0038). Cache outages are the adapter's to absorb on a read:
/// when the cache is down, a call runs the loader, so a use case never fails because of the cache. A removal is the exception, because it is the one operation whose result a caller must trust.
/// </summary>
public interface ICatalogCache
{
    /// <summary>
    /// Returns the cached result for the key, or runs the loader and keeps what it returns. Only a success is kept, because an error such as <c>not-found</c> must not
    /// outlive the change that fixes it. Calls for the same key on one node share one load.
    /// </summary>
    /// <typeparam name="T">The type of the value of the result.</typeparam>
    /// <param name="key">The key.</param>
    /// <param name="load">The loader, which runs when no entry exists.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    Task<Result<T>> GetOrLoadAsync<T>(CacheKey key, Func<CancellationToken, Task<Result<T>>> load, CancellationToken cancellationToken);

    /// <summary>
    /// Removes every entry that has the tag, on this node and, through the shared cache and the backplane, on the others. The call completes when the removal reached the shared cache and the backplane.
    /// </summary>
    /// <param name="tag">The tag.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    /// <exception cref="CacheInvalidationIncompleteException">
    /// The entries of this node are gone, and the removal could not be confirmed on the shared cache or the backplane. A caller that must guarantee the removal lets the exception propagate and tries again later. A caller
    /// that cannot wait, such as the best-effort dispatch after a commit, catches it, because the outbox delivers the same removal again.
    /// </exception>
    Task RemoveByTagAsync(string tag, CancellationToken cancellationToken);
}
