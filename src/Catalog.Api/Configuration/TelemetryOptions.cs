using Microsoft.Extensions.Options;

namespace Catalog.Api.Configuration;

/// <summary>Where the API sends its telemetry. <c>Otlp</c> sends traces, metrics, and logs over OTLP, and <c>None</c> sends nothing, which is what a test of the API wants.</summary>
public enum TelemetryExporter
{
    /// <summary>Send over OTLP to the endpoint of the standard <c>OTEL_EXPORTER_OTLP_*</c> variables, which is <c>localhost:4317</c> over gRPC when none is set.</summary>
    Otlp,

    /// <summary>Send nothing. The instrumentation still runs, so a trace identifier exists and a test can attach an exporter of its own.</summary>
    None,
}

/// <summary>
/// The telemetry settings of the API (plan section 8.11, ADR-0043). The endpoint, the protocol, the headers, and the resource attributes come from the standard OpenTelemetry variables (<c>OTEL_EXPORTER_OTLP_ENDPOINT</c>,
/// <c>OTEL_EXPORTER_OTLP_PROTOCOL</c>, <c>OTEL_EXPORTER_OTLP_HEADERS</c>, <c>OTEL_RESOURCE_ATTRIBUTES</c>), which the SDK reads itself, so a deployment sets them the way it sets them for any other service. This type holds the
/// few choices that the SDK has no variable for. The headers can carry a credential, so nothing here ever prints them. The type prints nothing but its name.
/// </summary>
public sealed class TelemetryOptions
{
    /// <summary>The name of the configuration section that the composition root binds to this type.</summary>
    public const string SectionName = "Catalog:Telemetry";

    /// <summary>Gets or sets where the telemetry goes. Default <see cref="TelemetryExporter.Otlp"/>.</summary>
    public TelemetryExporter Exporter { get; set; } = TelemetryExporter.Otlp;

    /// <summary>Gets or sets the name of the service in every signal, unless <c>OTEL_SERVICE_NAME</c> sets one. Default <c>catalog-api</c>.</summary>
    public string ServiceName { get; set; } = "catalog-api";

    /// <summary>Gets or sets the share of the traces that start in this service and are recorded, between 0 and 1. A trace that another service started keeps the decision of that service. Default 1, which records every trace.</summary>
    public double TraceSamplingRatio { get; set; } = 1;

    /// <inheritdoc />
    public override string ToString() => nameof(TelemetryOptions);
}

/// <summary>
/// Validates <see cref="TelemetryOptions"/> and the OTLP variables that the exporter will read, when the host starts. A problem names a setting or a variable and never repeats a value, because the headers can hold a
/// credential and an endpoint can hold one in its address.
/// </summary>
/// <param name="configuration">The configuration of the host, which holds the environment variables.</param>
internal sealed class TelemetryOptionsValidator(IConfiguration configuration) : IValidateOptions<TelemetryOptions>
{
    private static readonly string[] EndpointVariables =
    [
        "OTEL_EXPORTER_OTLP_ENDPOINT",
        "OTEL_EXPORTER_OTLP_TRACES_ENDPOINT",
        "OTEL_EXPORTER_OTLP_METRICS_ENDPOINT",
        "OTEL_EXPORTER_OTLP_LOGS_ENDPOINT",
    ];

    private static readonly string[] ProtocolVariables =
    [
        "OTEL_EXPORTER_OTLP_PROTOCOL",
        "OTEL_EXPORTER_OTLP_TRACES_PROTOCOL",
        "OTEL_EXPORTER_OTLP_METRICS_PROTOCOL",
        "OTEL_EXPORTER_OTLP_LOGS_PROTOCOL",
    ];

    private readonly IConfiguration _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TelemetryOptions options)
    {
        List<string> problems = [];

        if (!Enum.IsDefined(options.Exporter))
        {
            problems.Add("Exporter must be Otlp or None.");
        }

        if (string.IsNullOrWhiteSpace(options.ServiceName) || options.ServiceName.Length > 100 || options.ServiceName.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')))
        {
            problems.Add("ServiceName must have 1 to 100 characters: letters, digits, dots, hyphens, and underscores.");
        }

        if (double.IsNaN(options.TraceSamplingRatio) || options.TraceSamplingRatio is < 0 or > 1)
        {
            problems.Add("TraceSamplingRatio must be between 0 and 1.");
        }

        if (options.Exporter == TelemetryExporter.Otlp)
        {
            foreach (var variable in EndpointVariables)
            {
                var value = _configuration[variable];

                if (!string.IsNullOrWhiteSpace(value) && !(Uri.TryCreate(value, UriKind.Absolute, out var endpoint) && endpoint.Scheme is "http" or "https"))
                {
                    problems.Add($"{variable} must be an absolute http or https address.");
                }
            }

            foreach (var variable in ProtocolVariables)
            {
                var value = _configuration[variable];

                if (!string.IsNullOrWhiteSpace(value) && value is not ("grpc" or "http/protobuf"))
                {
                    problems.Add($"{variable} must be grpc or http/protobuf.");
                }
            }
        }

        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}
