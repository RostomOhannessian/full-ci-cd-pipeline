using Catalog.Application.Results;
using Catalog.Application.Telemetry;

namespace Catalog.Api.Composition;

/// <summary>
/// The interim adapter of <see cref="IUseCaseTelemetry"/> (ADR-0040): it writes one structured log line for the outcome of each use case, with the name of the use case and the kind of refusal, and never a value of the
/// request. The observability work (WP1.11) replaces it with an adapter over an activity source, a meter, and the logger, and the registration is the one line that changes. A fault is logged by the handler of unhandled
/// exceptions with the trace identifier, so this adapter does not log it a second time.
/// </summary>
/// <param name="logger">The logger.</param>
internal sealed partial class LoggingUseCaseTelemetry(ILogger<LoggingUseCaseTelemetry> logger) : IUseCaseTelemetry
{
    private readonly ILogger<LoggingUseCaseTelemetry> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public IUseCaseObservation Begin(string useCase, UseCaseKind kind) => new Observation(this, useCase, kind);

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "The {Kind} {UseCase} succeeded.")]
    private partial void LogSucceeded(UseCaseKind kind, string useCase);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "The {Kind} {UseCase} was refused: {Reason}.")]
    private partial void LogRefused(UseCaseKind kind, string useCase, string reason);

    private sealed class Observation(LoggingUseCaseTelemetry owner, string useCase, UseCaseKind kind) : IUseCaseObservation
    {
        public void Succeeded() => owner.LogSucceeded(kind, useCase);

        public void Refused(ApplicationError reason) => owner.LogRefused(kind, useCase, reason.GetType().Name);

        public void Faulted(Exception exception)
        {
        }

        public void Dispose()
        {
        }
    }
}
