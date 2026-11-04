using Asp.Versioning;
using Asp.Versioning.Builder;

namespace Catalog.Api.Endpoints;

/// <summary>
/// Maps the operations of the version 1 contract (plan section 8.6, REQ-API-001, ADR-0041). Each operation is one handler method that reads the request, runs the use case through its handler interface, and writes the
/// response, and the route group gives all of them the same version segment. The names of the operations are the operation identifiers of the contract.
/// </summary>
internal static class CatalogEndpoints
{
    /// <summary>The only version of the API today.</summary>
    public static readonly ApiVersion V1 = new(1, 0);

    /// <summary>Maps the categories and the products under <c>/api/v1</c>.</summary>
    /// <param name="app">The application.</param>
    public static WebApplication MapCatalogEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        ApiVersionSet versions = app.NewApiVersionSet().HasApiVersion(V1).ReportApiVersions().Build();
        var api = app.MapGroup("/api/v{version:apiVersion}").WithApiVersionSet(versions);

        api.MapCategoryEndpoints();
        api.MapProductEndpoints();

        return app;
    }
}
