using Catalog.Application.Results;

namespace Catalog.Application.Persistence;

/// <summary>
/// One transaction around the work of a command. The persistence layer implements it over one <c>DbContext</c> (WP1.8), and the transaction decorator is the only
/// caller of <see cref="ExecuteAsync{T}"/>, so a handler never starts or commits a transaction.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Runs the work in a transaction. It runs the hooks of <see cref="BeforeCommit"/> after a success and before the commit, then commits. It rolls back when the work fails,
    /// when a hook throws, or when the commit fails. A commit that fails because another writer changed a row after it was loaded returns <see cref="PreconditionFailed"/>,
    /// and a violation of the unique index of the SKU returns <see cref="Conflict"/> with <see cref="ConflictReason.SkuAlreadyExists"/>, so no database exception reaches a handler.
    /// </summary>
    /// <typeparam name="T">The type of the value of the work.</typeparam>
    /// <param name="work">The work.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    Task<Result<T>> ExecuteAsync<T>(Func<CancellationToken, Task<Result<T>>> work, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the pending changes inside the open transaction without committing, so a value that the database makes, such as the version of a row, can be read. The save
    /// also turns the domain events that the aggregates recorded into outbox rows in the same transaction.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Registers an action that runs inside the transaction of the next <see cref="ExecuteAsync{T}"/> of this scope, after the work succeeded and before the commit. The action
    /// receives the value of the work. A decorator that wraps the transaction uses it to write a record in the same transaction as the effect.
    /// </summary>
    /// <param name="action">The action.</param>
    void BeforeCommit(Func<object, CancellationToken, Task> action);
}
