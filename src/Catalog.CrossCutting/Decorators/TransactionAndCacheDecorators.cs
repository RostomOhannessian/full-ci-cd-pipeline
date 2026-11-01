using Catalog.Application.Caching;
using Catalog.Application.Events;
using Catalog.Application.Messaging;
using Catalog.Application.Persistence;
using Catalog.Application.Results;

namespace Catalog.CrossCutting.Decorators;

/// <summary>
/// Runs the handler of a command in one transaction through the unit of work, and wakes the outbox dispatcher after a successful commit (plan section 8.4, ADR-0032). It is the
/// innermost decorator, so the handler and the hooks of the decorators outside it share one transaction. The wake-up is best-effort: the outbox processor delivers the messages even
/// when the call fails, so a failure here never fails the command.
/// </summary>
/// <typeparam name="TCommand">The type of the command.</typeparam>
/// <typeparam name="TResult">The type of the value that a success returns.</typeparam>
/// <param name="inner">The handler.</param>
/// <param name="unitOfWork">The unit of work.</param>
/// <param name="outbox">The outbox dispatcher.</param>
public sealed class TransactionCommandDecorator<TCommand, TResult>(ICommandHandler<TCommand, TResult> inner, IUnitOfWork unitOfWork, IOutboxDispatcher outbox) : ICommandHandler<TCommand, TResult>
{
    private readonly ICommandHandler<TCommand, TResult> _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly IUnitOfWork _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    private readonly IOutboxDispatcher _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));

    /// <inheritdoc />
    public async Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken cancellationToken)
    {
        var result = await _unitOfWork.ExecuteAsync(token => _inner.HandleAsync(command, token), cancellationToken);

        if (result.IsSuccess)
        {
            await DispatchAsync(cancellationToken);
        }

        return result;
    }

    private async Task DispatchAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _outbox.DispatchCommittedAsync(cancellationToken);
        }
#pragma warning disable CA1031 // The commit succeeded and the outbox holds the messages, so the use case must not fail because the best-effort wake-up did.
        catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
        {
            // The outbox processor delivers the messages, and the implementation of the port reports its own failure.
        }
    }
}

/// <summary>
/// Serves a query that implements <see cref="ICachedQuery"/> from the cache and loads it through the next handler on a miss. Only a success is kept. A query that does not implement the interface
/// passes through, so a decorator that sits in every chain costs nothing for an uncached query.
/// </summary>
/// <typeparam name="TQuery">The type of the query.</typeparam>
/// <typeparam name="TResult">The type of the value that a success returns.</typeparam>
/// <param name="inner">The handler.</param>
/// <param name="cache">The cache.</param>
public sealed class QueryCacheDecorator<TQuery, TResult>(IQueryHandler<TQuery, TResult> inner, ICatalogCache cache) : IQueryHandler<TQuery, TResult>
{
    private readonly IQueryHandler<TQuery, TResult> _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly ICatalogCache _cache = cache ?? throw new ArgumentNullException(nameof(cache));

    /// <inheritdoc />
    public Task<Result<TResult>> HandleAsync(TQuery query, CancellationToken cancellationToken) =>
        query is ICachedQuery cached
            ? _cache.GetOrLoadAsync(cached.CacheKey, token => _inner.HandleAsync(query, token), cancellationToken)
            : _inner.HandleAsync(query, cancellationToken);
}
