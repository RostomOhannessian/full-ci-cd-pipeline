using Scalar.AspNetCore;

namespace Catalog.Api.Documentation;

/// <summary>
/// The document of the contract and its viewer, which exist in the Development environment only (plan section 8.6, REQ-CON-002, ADR-0042). Nothing is registered in any other environment, so a production host answers
/// 404 for both paths and ships no viewer to a caller. The document is what the conformance check compares with the authored contract, so it is generated from the endpoints of the running host.
/// </summary>
internal static class DocumentationEndpoints
{
    /// <summary>The path of the generated document.</summary>
    public const string DocumentPath = "/openapi/v1.json";

    /// <summary>The path of the viewer.</summary>
    public const string ViewerPath = "/scalar";

    // The viewer runs scripts and styles of its own, so it needs a policy that the API's strict default does not allow. It applies to the viewer only, and the default stays on every other response.
    private const string ViewerPolicy = "default-src 'self'; script-src 'self' 'unsafe-inline' 'unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self' data:; connect-src 'self'; frame-ancestors 'none'";

    /// <summary>Maps the document and the viewer when the host runs in the Development environment.</summary>
    /// <param name="app">The application.</param>
    public static WebApplication MapDevelopmentDocumentation(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (!app.Environment.IsDevelopment())
        {
            return app;
        }

        app.Use((context, next) =>
        {
            if (context.Request.Path.StartsWithSegments(ViewerPath, StringComparison.Ordinal))
            {
                context.Response.Headers.ContentSecurityPolicy = ViewerPolicy;
            }

            return next(context);
        });

        app.MapGet(DocumentPath, async (EndpointDataSource endpoints) => Results.Text(await ServedContract.BuildAsync(endpoints.Endpoints), "application/json"));
        app.MapScalarApiReference(ViewerPath, options => options.WithTitle("Product Catalog API").WithOpenApiRoutePattern(DocumentPath));

        return app;
    }
}
