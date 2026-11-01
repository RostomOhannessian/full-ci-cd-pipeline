using Catalog.Application.Messaging;
using Catalog.Application.Results;
using FluentValidation;

namespace Catalog.CrossCutting.Decorators;

/// <summary>
/// Runs every FluentValidation validator of a command before the next handler and refuses the command with all the messages at once. A command without a validator passes
/// through. The messages name the fields as the contract does (see <see cref="RequestValidation.JsonPath"/>).
/// </summary>
/// <typeparam name="TCommand">The type of the command.</typeparam>
/// <typeparam name="TResult">The type of the value that a success returns.</typeparam>
/// <param name="inner">The next handler.</param>
/// <param name="validators">The validators of the command.</param>
public sealed class ValidationCommandDecorator<TCommand, TResult>(ICommandHandler<TCommand, TResult> inner, IEnumerable<IValidator<TCommand>> validators) : ICommandHandler<TCommand, TResult>
{
    private readonly ICommandHandler<TCommand, TResult> _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly IEnumerable<IValidator<TCommand>> _validators = validators ?? throw new ArgumentNullException(nameof(validators));

    /// <inheritdoc />
    public async Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken cancellationToken)
    {
        var failed = await RequestValidation.ValidateAsync(_validators, command, cancellationToken);

        return failed is null ? await _inner.HandleAsync(command, cancellationToken) : Result.Failure<TResult>(failed);
    }
}

/// <summary>Runs every FluentValidation validator of a query before the next handler and refuses the query with all the messages at once. A query without a validator passes through.</summary>
/// <typeparam name="TQuery">The type of the query.</typeparam>
/// <typeparam name="TResult">The type of the value that a success returns.</typeparam>
/// <param name="inner">The next handler.</param>
/// <param name="validators">The validators of the query.</param>
public sealed class ValidationQueryDecorator<TQuery, TResult>(IQueryHandler<TQuery, TResult> inner, IEnumerable<IValidator<TQuery>> validators) : IQueryHandler<TQuery, TResult>
{
    private readonly IQueryHandler<TQuery, TResult> _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly IEnumerable<IValidator<TQuery>> _validators = validators ?? throw new ArgumentNullException(nameof(validators));

    /// <inheritdoc />
    public async Task<Result<TResult>> HandleAsync(TQuery query, CancellationToken cancellationToken)
    {
        var failed = await RequestValidation.ValidateAsync(_validators, query, cancellationToken);

        return failed is null ? await _inner.HandleAsync(query, cancellationToken) : Result.Failure<TResult>(failed);
    }
}

/// <summary>The shared work of the two validation decorators.</summary>
public static class RequestValidation
{
    /// <summary>Runs the validators in order and collects the failures of all of them, or returns null when the request is valid.</summary>
    /// <typeparam name="T">The type of the request.</typeparam>
    /// <param name="validators">The validators.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    public static async Task<ValidationFailed?> ValidateAsync<T>(IEnumerable<IValidator<T>> validators, T request, CancellationToken cancellationToken)
    {
        List<FieldError> errors = [];

        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(request, cancellationToken);
            errors.AddRange(result.Errors.Select(failure => new FieldError(JsonPath(failure.PropertyName), failure.ErrorMessage)));
        }

        return errors.Count == 0 ? null : new ValidationFailed(errors);
    }

    /// <summary>
    /// Turns the name of a property of a request into the JSON path of the contract by lowering the first letter of each segment, so <c>Price.Amount</c> becomes <c>price.amount</c> and
    /// <c>Items[0].Name</c> becomes <c>items[0].name</c>. An empty name, which a rule on the whole request has, stays empty.
    /// </summary>
    /// <param name="propertyName">The name that FluentValidation reports.</param>
    public static string JsonPath(string propertyName)
    {
        ArgumentNullException.ThrowIfNull(propertyName);

        return string.Join('.', propertyName.Split('.').Select(segment => segment.Length == 0 ? segment : char.ToLowerInvariant(segment[0]) + segment[1..]));
    }
}
