using Catalog.Api;
using Catalog.Api.Composition;

// The composition root of the catalog API (plan section 8.1, ADR-0040). All the wiring is in CatalogComposition, and the order of the middleware and the endpoints is in CatalogPipeline.
var builder = WebApplication.CreateBuilder(args);

builder.AddCatalogApi();

var app = builder.Build();

app.UseCatalogApi();

await app.RunAsync();

// CA1515: the integration tests start this composition root through WebApplicationFactory<Program>, which needs a public entry point type. The type holds nothing.
#pragma warning disable CA1515
/// <summary>The entry point of the API, which the integration tests name to start the host.</summary>
public partial class Program;
#pragma warning restore CA1515
