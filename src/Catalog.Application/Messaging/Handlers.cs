using Catalog.Application.Results;

namespace Catalog.Application.Messaging;

/// <summary>
/// Handles one command, which changes the state of the catalog. A command has one handler, and an endpoint injects this interface for its own command, so there
/// is no mediator and no dispatch by type lookup (plan section 8.2, ADR-0032). Decorators implement the same interface and wrap the handler.
/// </summary>
/// <typeparam name="TCommand">The type of the command.</typeparam>
/// <typeparam name="TResult">The type of the value that a success returns.</typeparam>
public interface ICommandHandler<in TCommand, TResult>
{
    /// <summary>Handles the command.</summary>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// Handles one query, which reads the catalog and changes nothing. A query has one handler, and an endpoint injects this interface for its own query
/// (plan section 8.2, ADR-0032). Decorators implement the same interface and wrap the handler.
/// </summary>
/// <typeparam name="TQuery">The type of the query.</typeparam>
/// <typeparam name="TResult">The type of the value that a success returns.</typeparam>
public interface IQueryHandler<in TQuery, TResult>
{
    /// <summary>Handles the query.</summary>
    /// <param name="query">The query.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    Task<Result<TResult>> HandleAsync(TQuery query, CancellationToken cancellationToken);
}
