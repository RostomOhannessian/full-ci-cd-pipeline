using Catalog.Api.Composition;
using Catalog.Api.Endpoints;
using Catalog.Api.Http;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace Catalog.Api;

/// <summary>The order of the middleware and the mapping of the endpoints of the API (plan sections 8.6, 8.9, and 9, ADR-0040).</summary>
internal static class CatalogPipeline
{
    /// <summary>
    /// Builds the pipeline. The order is the design: the trace and the forwarded headers come first, so everything after them sees the real caller and an error always has a trace identifier. The security headers and the
    /// error handlers wrap the rest, so every answer has the headers and the shape of the catalog. Routing comes before the port gate, which reads the endpoint, and authentication comes before the rate limiter, which counts
    /// a caller by its identifier. Authorization is last, so a request that the limiter refused never costs a policy check.
    /// </summary>
    /// <param name="app">The application.</param>
    public static WebApplication UseCatalogApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseMiddleware<RequestTraceMiddleware>();
        app.UseForwardedHeaders();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseExceptionHandler(new ExceptionHandlerOptions { ExceptionHandler = context => context.RequestServices.GetRequiredService<ApiErrorHandler>().HandleExceptionAsync(context) });
        app.UseStatusCodePages(context => ApiErrorHandler.WriteStatusAsync(context.HttpContext));
        app.UseRouting();
        app.UseMiddleware<PortGateMiddleware>();
        app.UseMiddleware<ApiVersionReportMiddleware>();
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseAuthorization();

        app.MapHealthEndpoints();
        app.MapCatalogEndpoints();

        return app;
    }

    // The three probes of plan section 8.9, on the management port only. Live checks nothing, startup checks that the credential is loaded, and ready checks the database, the outbox, and the cache. A degraded cache or
    // outbox is a 200 with the status in the body, and only an unhealthy check is a 503.
    private static void MapHealthEndpoints(this WebApplication app)
    {
        var health = app.MapGroup("/health").WithMetadata(ManagementEndpointMetadata.Instance);

        health.MapHealthChecks("/live", new HealthCheckOptions { Predicate = _ => false, ResponseWriter = HealthResponseWriter.WriteAsync });
        health.MapHealthChecks("/startup", new HealthCheckOptions { Predicate = check => check.Tags.Contains(HealthTags.Startup), ResponseWriter = HealthResponseWriter.WriteAsync });
        health.MapHealthChecks("/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(HealthTags.Ready), ResponseWriter = HealthResponseWriter.WriteAsync });
    }
}
