namespace Catalog.Application.Idempotency;

/// <summary>Identifies one request to the idempotency store: the key, the caller that sent it, and the fingerprint of what the caller asked for.</summary>
/// <param name="Key">The key of the caller.</param>
/// <param name="CallerId">The identifier of the caller (<see cref="Security.ICurrentPrincipal.CallerId"/>). A key belongs to one caller, so two callers can use the same text.</param>
/// <param name="Fingerprint">A hash of the command, so a key that comes back with a different request is told apart from a replay.</param>
public sealed record IdempotencyRequest(IdempotencyKey Key, string CallerId, string Fingerprint);

/// <summary>What the store found when a request began.</summary>
public enum IdempotencyOutcome
{
    /// <summary>The key is new. The store reserved it, and the request must run.</summary>
    Reserved,

    /// <summary>The same request ran before and finished. <see cref="IdempotencyDecision.Response"/> holds its first result.</summary>
    Replay,

    /// <summary>The same request is still running, so the caller must retry later.</summary>
    InProgress,

    /// <summary>The key belongs to a different request.</summary>
    Reused,
}

/// <summary>The answer of <see cref="IIdempotencyStore.BeginAsync"/>.</summary>
/// <param name="Outcome">What the store found.</param>
/// <param name="Response">The serialized first result when the outcome is <see cref="IdempotencyOutcome.Replay"/>, and null otherwise.</param>
public sealed record IdempotencyDecision(IdempotencyOutcome Outcome, string? Response = null);

/// <summary>
/// The store of idempotency records (plan section 8.4). A record holds the key, the caller, the fingerprint, the status, and the serialized first result, and it
/// expires after 24 hours. The store is implemented in WP1.8.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>Finds the record of a request, or reserves the key when it is new. Two requests that arrive together get one <see cref="IdempotencyOutcome.Reserved"/>.</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    Task<IdempotencyDecision> BeginAsync(IdempotencyRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Records the first result of a reserved request. The idempotency decorator calls it from a hook of <see cref="Persistence.IUnitOfWork.BeforeCommit"/>, so the
    /// store writes the record in the transaction of the command, and the record exists exactly when the effect does.
    /// </summary>
    /// <param name="request">The request that began.</param>
    /// <param name="response">The serialized result of the command.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    Task CompleteAsync(IdempotencyRequest request, string response, CancellationToken cancellationToken);

    /// <summary>Releases the reservation of a request that failed, so the caller can send it again with the same key.</summary>
    /// <param name="request">The request that began.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    Task ReleaseAsync(IdempotencyRequest request, CancellationToken cancellationToken);
}
