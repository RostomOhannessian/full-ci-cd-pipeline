namespace Catalog.Api.Tests.Support;

/// <summary>The in-process issuer and the real API with the test endpoints, with the in-memory exporters of the telemetry attached after the redaction of the host, for the tests of the logs and of the redaction. It needs no database.</summary>
public sealed class TelemetryApiFixture : SecuredApiFixture
{
    internal TelemetryCapture Capture { get; } = new();

    internal override Action<IServiceCollection>? AttachTelemetry => Capture.AddTo;
}
