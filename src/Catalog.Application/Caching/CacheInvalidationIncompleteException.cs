namespace Catalog.Application.Caching;

/// <summary>
/// Says that <see cref="ICatalogCache.RemoveByTagAsync"/> removed the entries of this node, and could not confirm the removal on the shared cache or on the backplane, so another node can keep serving
/// the entries until its own copy expires (plan section 8.5, ADR-0038). The outbox handler of the cache lets it propagate, so the processor delivers the message again with a longer backoff, and the best-effort
/// dispatch after a commit catches it, because the outbox still holds the message. The message names the tag and never a value of an entry.
/// </summary>
public sealed class CacheInvalidationIncompleteException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="CacheInvalidationIncompleteException"/> class.</summary>
    public CacheInvalidationIncompleteException()
        : this("The cache could not confirm the removal on the shared cache or on the backplane.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="CacheInvalidationIncompleteException"/> class.</summary>
    /// <param name="message">The message. It must not hold the value of an entry.</param>
    public CacheInvalidationIncompleteException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="CacheInvalidationIncompleteException"/> class.</summary>
    /// <param name="message">The message. It must not hold the value of an entry.</param>
    /// <param name="innerException">The fault of the cache that made the removal incomplete.</param>
    public CacheInvalidationIncompleteException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
