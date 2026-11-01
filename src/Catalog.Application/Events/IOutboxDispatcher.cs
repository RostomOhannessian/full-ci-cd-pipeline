namespace Catalog.Application.Events;

/// <summary>
/// Wakes the outbox dispatcher of this node after a commit, so a message is delivered within milliseconds and not at the next poll. It is best-effort: the outbox
/// processor delivers every message at least once even when the call never happens (plan section 8.4, ADR-0012). The persistence layer implements it (WP1.8).
/// </summary>
public interface IOutboxDispatcher
{
    /// <summary>Delivers the messages that the last commit of this scope wrote. A failure here must not fail the use case, because the outbox still holds the messages.</summary>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    Task DispatchCommittedAsync(CancellationToken cancellationToken);
}
