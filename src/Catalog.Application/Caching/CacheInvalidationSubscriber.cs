using Catalog.Application.Events;

namespace Catalog.Application.Caching;

/// <summary>
/// The handler of the outbox that removes the cache entries which a domain event made stale (plan sections 8.4 and 8.5, ADR-0012, ADR-0039). The writing node gives it each committed message once right after the
/// commit, on a best-effort basis, and the outbox processor gives it the message again with a receipt, so a removal that failed or was lost on the first path still happens. It is idempotent: a second removal of a tag
/// only makes the entries that were loaded in between stale again, which is the double delete that closes the race of a reader that loaded the old row before the commit. It tries every tag even when one is not
/// confirmed, so this node and the shared cache lose all the entries, and then it throws <see cref="CacheInvalidationIncompleteException"/> so the outbox tries the whole message again later.
/// </summary>
/// <param name="cache">The cache.</param>
public sealed class CacheInvalidationSubscriber(ICatalogCache cache) : IOutboxMessageHandler
{
    /// <summary>The stable name of the handler. It is the key of the receipt, so renaming the class never changes it.</summary>
    public const string HandlerName = "catalog-cache-invalidation";

    private readonly ICatalogCache _cache = cache ?? throw new ArgumentNullException(nameof(cache));

    /// <inheritdoc />
    public string Name => HandlerName;

    /// <inheritdoc />
    public async Task HandleAsync(OutboxMessageEnvelope message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var tags = CacheInvalidationPolicy.TagsFor(message);
        List<CacheInvalidationIncompleteException>? incomplete = null;

        foreach (var tag in tags)
        {
            try
            {
                await _cache.RemoveByTagAsync(tag, cancellationToken).ConfigureAwait(false);
            }
            catch (CacheInvalidationIncompleteException exception)
            {
                (incomplete ??= []).Add(exception);
            }
        }

        if (incomplete is not null)
        {
            throw new CacheInvalidationIncompleteException($"The removal of {incomplete.Count} of {tags.Count} tags for message {message.MessageId} is not confirmed.", new AggregateException(incomplete));
        }
    }
}
