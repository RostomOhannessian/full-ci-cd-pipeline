using Catalog.Api.Configuration;
using Catalog.Api.Telemetry;
using Catalog.CrossCutting.Caching;
using Catalog.CrossCutting.Telemetry;
using Catalog.Infrastructure.Caching;
using Catalog.Infrastructure.Credentials;
using Catalog.Infrastructure.Idempotency;
using Catalog.Infrastructure.Outbox;
using Microsoft.Extensions.Options;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Catalog.Api.Composition;

/// <summary>
/// The telemetry of the API (plan section 8.11, REQ-OBS-001, REQ-OBS-002, REQ-OBS-003, ADR-0043): the OpenTelemetry SDK with its instrumentation, the sources and meters that the code of the catalog owns, the redaction
/// of every span and log record, and the OTLP exporters. This is the one place that names the OpenTelemetry packages, and it lives in the composition namespace because it names the adapters whose sources and meters it
/// subscribes to. The exporter is chosen by <see cref="TelemetryOptions"/>, and the endpoint comes from the standard OTLP variables, which the SDK reads itself.
/// </summary>
internal static class TelemetryComposition
{
    // The meters of ASP.NET Core 10 for the authentication and the authorization are not part of the default set of the instrumentation, and the plan names their counters (section 8.11).
    private static readonly string[] AspNetCoreSecurityMeters = ["Microsoft.AspNetCore.Authentication", "Microsoft.AspNetCore.Authorization"];

    /// <summary>Registers the options, the redaction, and the SDK.</summary>
    /// <param name="builder">The builder of the host.</param>
    public static WebApplicationBuilder AddCatalogTelemetry(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;
        services.AddSingleton<IValidateOptions<TelemetryOptions>, TelemetryOptionsValidator>();
        services.AddOptions<TelemetryOptions>().BindConfiguration(TelemetryOptions.SectionName).ValidateOnStart();

        services.AddRedaction(redaction =>
        {
            redaction.SetRedactor<MarkerRedactor>(CatalogTaxonomy.Secret, CatalogTaxonomy.Personal);
            redaction.SetFallbackRedactor<MarkerRedactor>();
        });
        services.AddSingleton<TelemetryRedactor>();
        services.AddSingleton<RedactingActivityProcessor>();
        services.AddSingleton<RedactingLogProcessor>();

        // The choices that decide how the pipeline is built are read once, here. The validator checks the same section when the host starts, so a wrong value stops the host.
        var settings = builder.Configuration.GetSection(TelemetryOptions.SectionName).Get<TelemetryOptions>() ?? new TelemetryOptions();
        var otlp = settings.Exporter == TelemetryExporter.Otlp;

        var telemetry = services.AddOpenTelemetry();
        telemetry.ConfigureResource(resource => resource.AddService(settings.ServiceName));

        telemetry.WithTracing(tracing =>
        {
            tracing.SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(Math.Clamp(settings.TraceSamplingRatio, 0, 1))));

            // The redaction comes first, so every exporter that is added after it sees the redacted spans (REQ-SEC-006).
            tracing.AddProcessor(provider => provider.GetRequiredService<RedactingActivityProcessor>());

            // A probe every few seconds would be most of the traces, so the health endpoints are left out. The instrumentation never records the text of an exception, which can hold a value of a request.
            tracing.AddAspNetCoreInstrumentation(options =>
            {
                options.Filter = context => !context.Request.Path.StartsWithSegments("/health", StringComparison.Ordinal);
                options.RecordException = false;
            });
            tracing.AddHttpClientInstrumentation(options => options.RecordException = false);
            tracing.AddSqlClientInstrumentation(options => options.RecordException = false);
            tracing.AddEntityFrameworkCoreInstrumentation();
            tracing.AddRedisInstrumentation();
            tracing.ConfigureRedisInstrumentation((provider, instrumentation) =>
                provider.GetRequiredService<ValkeyConnectionObserver>().Attach(connection => _ = instrumentation.AddConnection(connection)));

            tracing.AddSource(UseCaseTelemetry.Name, OutboxMetrics.MeterName, CredentialMetrics.MeterName);
            tracing.AddSource(CacheTelemetryNames.ActivitySources.ToArray());

            if (otlp)
            {
                tracing.AddOtlpExporter();
            }
        });

        telemetry.WithMetrics(metrics =>
        {
            metrics.AddAspNetCoreInstrumentation();
            metrics.AddHttpClientInstrumentation();
            metrics.AddSqlClientInstrumentation();
            metrics.AddRuntimeInstrumentation();
            metrics.AddMeter(AspNetCoreSecurityMeters);
            metrics.AddMeter(UseCaseTelemetry.Name, OutboxMetrics.MeterName, CredentialMetrics.MeterName, IdempotencyMetrics.MeterName, CacheMetrics.MeterName);
            metrics.AddMeter(CacheTelemetryNames.Meters.ToArray());

            if (otlp)
            {
                metrics.AddOtlpExporter();
            }
        });

        // The formatted message is left out, because the attributes hold every value of the template and the redaction checks each one. The scopes are left out because a scope can hold anything.
        telemetry.WithLogging(
            logging =>
            {
                logging.AddProcessor(provider => provider.GetRequiredService<RedactingLogProcessor>());

                if (otlp)
                {
                    logging.AddOtlpExporter();
                }
            },
            options =>
            {
                options.IncludeFormattedMessage = false;
                options.IncludeScopes = false;
                options.ParseStateValues = true;
            });

        return builder;
    }
}
