using Catalog.Application.Caching;
using Catalog.Application.Events;
using Catalog.Application.Idempotency;
using Catalog.Application.Messaging;
using Catalog.Application.Persistence;
using Catalog.Application.Security;
using Catalog.Application.Telemetry;
using FluentValidation;

namespace Catalog.CrossCutting.Decorators;

/// <summary>
/// The one place that fixes the order of the decorators of a command, outermost first: telemetry, validation, authorization, idempotency, transaction, then the handler (plan section 8.2,
/// ADR-0032). The composition root calls <see cref="Compose{TCommand, TResult}"/> once for each command that a feature registers, so the chain is plain code and no container decides the order.
/// </summary>
public static class CommandPipeline
{
    /// <summary>Wraps a handler in the decorators of a command.</summary>
    /// <typeparam name="TCommand">The type of the command.</typeparam>
    /// <typeparam name="TResult">The type of the value that a success returns.</typeparam>
    /// <param name="handler">The handler of the use case.</param>
    /// <param name="telemetry">The telemetry port.</param>
    /// <param name="validators">The validators of the command.</param>
    /// <param name="principal">The caller.</param>
    /// <param name="idempotencyStore">The idempotency store.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    /// <param name="outbox">The outbox dispatcher.</param>
    /// <returns>The outermost decorator.</returns>
    public static ICommandHandler<TCommand, TResult> Compose<TCommand, TResult>(
        ICommandHandler<TCommand, TResult> handler,
        IUseCaseTelemetry telemetry,
        IEnumerable<IValidator<TCommand>> validators,
        ICurrentPrincipal principal,
        IIdempotencyStore idempotencyStore,
        IUnitOfWork unitOfWork,
        IOutboxDispatcher outbox)
        where TCommand : IProtectedRequest
    {
        var transaction = new TransactionCommandDecorator<TCommand, TResult>(handler, unitOfWork, outbox);
        var idempotency = new IdempotencyCommandDecorator<TCommand, TResult>(transaction, idempotencyStore, unitOfWork, principal);
        var authorization = new AuthorizationCommandDecorator<TCommand, TResult>(idempotency, principal);
        var validation = new ValidationCommandDecorator<TCommand, TResult>(authorization, validators);

        return new TelemetryCommandDecorator<TCommand, TResult>(validation, telemetry);
    }
}

/// <summary>
/// The one place that fixes the order of the decorators of a query, outermost first: telemetry, validation, authorization, the cache for a marked query, then the handler (plan section 8.2,
/// ADR-0032). A query has no transaction, because it only reads.
/// </summary>
public static class QueryPipeline
{
    /// <summary>Wraps a handler in the decorators of a query.</summary>
    /// <typeparam name="TQuery">The type of the query.</typeparam>
    /// <typeparam name="TResult">The type of the value that a success returns.</typeparam>
    /// <param name="handler">The handler of the use case.</param>
    /// <param name="telemetry">The telemetry port.</param>
    /// <param name="validators">The validators of the query.</param>
    /// <param name="principal">The caller.</param>
    /// <param name="cache">The cache.</param>
    /// <returns>The outermost decorator.</returns>
    public static IQueryHandler<TQuery, TResult> Compose<TQuery, TResult>(
        IQueryHandler<TQuery, TResult> handler,
        IUseCaseTelemetry telemetry,
        IEnumerable<IValidator<TQuery>> validators,
        ICurrentPrincipal principal,
        ICatalogCache cache)
        where TQuery : IProtectedRequest
    {
        var caching = new QueryCacheDecorator<TQuery, TResult>(handler, cache);
        var authorization = new AuthorizationQueryDecorator<TQuery, TResult>(caching, principal);
        var validation = new ValidationQueryDecorator<TQuery, TResult>(authorization, validators);

        return new TelemetryQueryDecorator<TQuery, TResult>(validation, telemetry);
    }
}
