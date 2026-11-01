using Catalog.Application.Results;

namespace Catalog.Application.Telemetry;

/// <summary>Says whether a use case changes the catalog or reads it.</summary>
public enum UseCaseKind
{
    /// <summary>A command changes the catalog.</summary>
    Command,

    /// <summary>A query reads the catalog.</summary>
    Query,
}

/// <summary>
/// The telemetry of use cases. The observability work (WP1.11) implements it over an <c>ActivitySource</c>, a meter, and the logger, so the decorator in this layer
/// stays free of those libraries and a test passes a fake.
/// </summary>
public interface IUseCaseTelemetry
{
    /// <summary>Starts to observe a use case. Disposing the observation ends it.</summary>
    /// <param name="useCase">The name of the use case, such as <c>CreateProduct</c>. It is a name of the code and never a value of the request.</param>
    /// <param name="kind">Whether it is a command or a query.</param>
    IUseCaseObservation Begin(string useCase, UseCaseKind kind);
}

/// <summary>One run of a use case. Exactly one of the three outcome methods is called before the observation is disposed.</summary>
public interface IUseCaseObservation : IDisposable
{
    /// <summary>The use case succeeded.</summary>
    void Succeeded();

    /// <summary>The use case refused to do its work, which is an expected outcome and not a fault.</summary>
    /// <param name="reason">The reason.</param>
    void Refused(ApplicationError reason);

    /// <summary>The use case threw an exception, which is a fault. A cancellation arrives here too, and the adapter decides how to classify it.</summary>
    /// <param name="exception">The exception.</param>
    void Faulted(Exception exception);
}
