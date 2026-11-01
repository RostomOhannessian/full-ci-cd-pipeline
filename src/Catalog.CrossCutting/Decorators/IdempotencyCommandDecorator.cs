using System.Security.Cryptography;
using System.Text.Json;
using Catalog.Application.Idempotency;
using Catalog.Application.Messaging;
using Catalog.Application.Persistence;
using Catalog.Application.Results;
using Catalog.Application.Security;

namespace Catalog.CrossCutting.Decorators;

/// <summary>
/// Makes a command that carries an idempotency key run at most once for that key (plan section 8.4, ADR-0028, ADR-0032). The decorator reserves the key, replays the first result for the same
/// request, refuses a different request that uses the key, and refuses a request while the first one runs. A command without a key passes through.
/// The record of the first result is written in the transaction of the command: the decorator registers a hook with the unit of work, and the transaction decorator runs the hook before the commit.
/// The value of a command must round-trip through <c>System.Text.Json</c>, because the store keeps it as text.
/// </summary>
/// <typeparam name="TCommand">The type of the command.</typeparam>
/// <typeparam name="TResult">The type of the value that a success returns.</typeparam>
/// <param name="inner">The next handler, which is the transaction decorator.</param>
/// <param name="store">The idempotency store.</param>
/// <param name="unitOfWork">The unit of work that the transaction decorator runs.</param>
/// <param name="principal">The caller, whose identifier scopes the key.</param>
public sealed class IdempotencyCommandDecorator<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner,
    IIdempotencyStore store,
    IUnitOfWork unitOfWork,
    ICurrentPrincipal principal) : ICommandHandler<TCommand, TResult>
{
    private readonly ICommandHandler<TCommand, TResult> _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly IIdempotencyStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly IUnitOfWork _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    private readonly ICurrentPrincipal _principal = principal ?? throw new ArgumentNullException(nameof(principal));

    /// <inheritdoc />
    public async Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken cancellationToken)
    {
        if (command is not IIdempotentCommand { IdempotencyKey: { } key })
        {
            return await _inner.HandleAsync(command, cancellationToken);
        }

        var request = new IdempotencyRequest(key, _principal.CallerId, Fingerprint(command));
        var decision = await _store.BeginAsync(request, cancellationToken);

        return decision.Outcome switch
        {
            IdempotencyOutcome.Replay => Replay(decision),
            IdempotencyOutcome.InProgress => Result.Failure<TResult>(new Conflict(ConflictReason.IdempotencyRequestInProgress)),
            IdempotencyOutcome.Reused => Result.Failure<TResult>(new IdempotencyKeyReused()),
            IdempotencyOutcome.Reserved => await RunReservedAsync(command, request, cancellationToken),
            _ => throw new InvalidOperationException($"The idempotency store returned the unknown outcome {decision.Outcome}."),
        };
    }

    private async Task<Result<TResult>> RunReservedAsync(TCommand command, IdempotencyRequest request, CancellationToken cancellationToken)
    {
        _unitOfWork.BeforeCommit((value, token) => _store.CompleteAsync(request, JsonSerializer.Serialize((TResult)value), token));

        try
        {
            var result = await _inner.HandleAsync(command, cancellationToken);

            if (!result.IsSuccess)
            {
                await _store.ReleaseAsync(request, cancellationToken);
            }

            return result;
        }
        catch
        {
            // The caller must be able to retry after a fault, so the reservation is released even when the request was cancelled.
            await _store.ReleaseAsync(request, CancellationToken.None);
            throw;
        }
    }

    private static Result<TResult> Replay(IdempotencyDecision decision)
    {
        if (decision.Response is null)
        {
            throw new InvalidOperationException("The idempotency store returned a replay that holds no result.");
        }

        return Result.Success(JsonSerializer.Deserialize<TResult>(decision.Response)
            ?? throw new InvalidOperationException("The idempotency store returned a replay whose result is JSON null."));
    }

    // The same request gives the same text: the name of the command and its properties, in the order that the type declares them.
    private static string Fingerprint(TCommand command) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(command)));
}
