using Catalog.Application.Messaging;
using Catalog.Application.Results;
using Catalog.Application.Telemetry;

namespace Catalog.CrossCutting.Decorators;

/// <summary>The outermost decorator of a command. It observes the whole run, so a refusal, a replay, and a fault are all traced, counted, and timed (ADR-0032).</summary>
/// <typeparam name="TCommand">The type of the command.</typeparam>
/// <typeparam name="TResult">The type of the value that a success returns.</typeparam>
/// <param name="inner">The next handler.</param>
/// <param name="telemetry">The telemetry port.</param>
public sealed class TelemetryCommandDecorator<TCommand, TResult>(ICommandHandler<TCommand, TResult> inner, IUseCaseTelemetry telemetry) : ICommandHandler<TCommand, TResult>
{
    private readonly ICommandHandler<TCommand, TResult> _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly IUseCaseTelemetry _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));

    /// <inheritdoc />
    public Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken cancellationToken) =>
        Observation.RunAsync(_telemetry, typeof(TCommand).Name, UseCaseKind.Command, token => _inner.HandleAsync(command, token), cancellationToken);
}

/// <summary>The outermost decorator of a query. It observes the whole run, so a refusal, a cache hit, and a fault are all traced, counted, and timed (ADR-0032).</summary>
/// <typeparam name="TQuery">The type of the query.</typeparam>
/// <typeparam name="TResult">The type of the value that a success returns.</typeparam>
/// <param name="inner">The next handler.</param>
/// <param name="telemetry">The telemetry port.</param>
public sealed class TelemetryQueryDecorator<TQuery, TResult>(IQueryHandler<TQuery, TResult> inner, IUseCaseTelemetry telemetry) : IQueryHandler<TQuery, TResult>
{
    private readonly IQueryHandler<TQuery, TResult> _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly IUseCaseTelemetry _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));

    /// <inheritdoc />
    public Task<Result<TResult>> HandleAsync(TQuery query, CancellationToken cancellationToken) =>
        Observation.RunAsync(_telemetry, typeof(TQuery).Name, UseCaseKind.Query, token => _inner.HandleAsync(query, token), cancellationToken);
}

internal static class Observation
{
    // Exactly one outcome is reported for each run. An exception, including a cancellation, is reported as a fault and thrown again, and the adapter decides how to classify it.
    public static async Task<Result<T>> RunAsync<T>(
        IUseCaseTelemetry telemetry,
        string useCase,
        UseCaseKind kind,
        Func<CancellationToken, Task<Result<T>>> run,
        CancellationToken cancellationToken)
    {
        using var observation = telemetry.Begin(useCase, kind);

        try
        {
            var result = await run(cancellationToken);

            if (result.IsSuccess)
            {
                observation.Succeeded();
            }
            else
            {
                observation.Refused(result.Error);
            }

            return result;
        }
        catch (Exception exception)
        {
            observation.Faulted(exception);
            throw;
        }
    }
}
