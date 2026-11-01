using Catalog.Application.Messaging;
using Catalog.Application.Results;
using Catalog.Application.Security;

namespace Catalog.CrossCutting.Decorators;

/// <summary>
/// Refuses a command whose caller lacks the scope or the role of its access level, before the next handler touches any state. The endpoint policy has checked the token already, and
/// this decorator checks the scope again together with the role, because Keycloak does not withhold a scope (R21, ADR-0032).
/// </summary>
/// <typeparam name="TCommand">The type of the command.</typeparam>
/// <typeparam name="TResult">The type of the value that a success returns.</typeparam>
/// <param name="inner">The next handler.</param>
/// <param name="principal">The caller.</param>
public sealed class AuthorizationCommandDecorator<TCommand, TResult>(ICommandHandler<TCommand, TResult> inner, ICurrentPrincipal principal) : ICommandHandler<TCommand, TResult>
    where TCommand : IProtectedRequest
{
    private readonly ICommandHandler<TCommand, TResult> _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly ICurrentPrincipal _principal = principal ?? throw new ArgumentNullException(nameof(principal));

    /// <inheritdoc />
    public Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken cancellationToken) =>
        PermissionPolicy.Check(TCommand.RequiredAccess, _principal) is { } forbidden
            ? Task.FromResult(Result.Failure<TResult>(forbidden))
            : _inner.HandleAsync(command, cancellationToken);
}

/// <summary>Refuses a query whose caller lacks the scope or the role of its access level, before the next handler reads anything (R21, ADR-0032).</summary>
/// <typeparam name="TQuery">The type of the query.</typeparam>
/// <typeparam name="TResult">The type of the value that a success returns.</typeparam>
/// <param name="inner">The next handler.</param>
/// <param name="principal">The caller.</param>
public sealed class AuthorizationQueryDecorator<TQuery, TResult>(IQueryHandler<TQuery, TResult> inner, ICurrentPrincipal principal) : IQueryHandler<TQuery, TResult>
    where TQuery : IProtectedRequest
{
    private readonly IQueryHandler<TQuery, TResult> _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly ICurrentPrincipal _principal = principal ?? throw new ArgumentNullException(nameof(principal));

    /// <inheritdoc />
    public Task<Result<TResult>> HandleAsync(TQuery query, CancellationToken cancellationToken) =>
        PermissionPolicy.Check(TQuery.RequiredAccess, _principal) is { } forbidden
            ? Task.FromResult(Result.Failure<TResult>(forbidden))
            : _inner.HandleAsync(query, cancellationToken);
}
