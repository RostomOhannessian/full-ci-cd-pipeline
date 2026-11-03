using Catalog.Application.Persistence;
using Catalog.Application.Results;
using Catalog.Infrastructure.Persistence.Mapping;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Infrastructure.Persistence;

/// <summary>
/// Implements <see cref="IUnitOfWork"/> over one <see cref="CatalogDbContext"/> transaction (plan section 8.4, ADR-0033). The transaction decorator is the only caller of <see cref="ExecuteAsync{T}"/>. A successful
/// run saves the changes, which the outbox interceptor turns into outbox rows in the same transaction, runs the hooks, and commits. A refusal that a caller can cause becomes a value: a row that another writer changed
/// is <see cref="PreconditionFailed"/>, and a SKU that another product owns is <see cref="Conflict"/>. Any other failure rolls back and surfaces as an exception.
/// </summary>
/// <param name="context">The write context of the scope, which the repositories share.</param>
public sealed class EfUnitOfWork(CatalogDbContext context) : IUnitOfWork
{
    // SQL Server reports a violation of a unique index as 2601 and of a unique constraint as 2627. The primary key is a constraint, so a duplicate identifier is 2627 too, and the index name tells the two apart.
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    private readonly CatalogDbContext _context = context ?? throw new ArgumentNullException(nameof(context));
    private readonly List<Func<object, CancellationToken, Task>> _hooks = [];
    private bool _running;

    /// <inheritdoc />
    public async Task<Result<T>> ExecuteAsync<T>(Func<CancellationToken, Task<Result<T>>> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        if (_running)
        {
            throw new InvalidOperationException("A unit of work does not nest. The transaction decorator is the only caller of ExecuteAsync.");
        }

        _running = true;

        try
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                var result = await work(cancellationToken);

                if (!result.IsSuccess)
                {
                    Discard();

                    return result;
                }

                await _context.SaveChangesAsync(cancellationToken);

                foreach (var hook in _hooks.ToList())
                {
                    await hook(result.Value!, cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);

                return result;
            }
            catch (DbUpdateConcurrencyException)
            {
                Discard();

                return Result.Failure<T>(new PreconditionFailed());
            }
            catch (DbUpdateException exception) when (IsSkuViolation(exception))
            {
                Discard();

                return Result.Failure<T>(new Conflict(ConflictReason.SkuAlreadyExists));
            }
            catch
            {
                Discard();
                throw;
            }
        }
        finally
        {
            _hooks.Clear();
            _running = false;
        }
    }

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken) => _context.SaveChangesAsync(cancellationToken);

    /// <inheritdoc />
    public void BeforeCommit(Func<object, CancellationToken, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        _hooks.Add(action);
    }

    // A failed or refused run leaves tracked changes that never reached the database, so the tracker is cleared and the scope can read again. Leaving the block of the transaction rolls it back.
    private void Discard() => _context.ChangeTracker.Clear();

    private static bool IsSkuViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: UniqueIndexViolation or UniqueConstraintViolation } sql
        && sql.Message.Contains(ProductConfiguration.SkuIndexName, StringComparison.Ordinal);
}
