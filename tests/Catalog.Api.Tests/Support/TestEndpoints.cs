using System.Data.Common;
using Catalog.Api.Http;
using Catalog.Api.Security;
using Catalog.Application.Results;
using Catalog.Application.Security;

namespace Catalog.Api.Tests.Support;

/// <summary>A database exception that a test throws, because the driver builds its own only from a real failure.</summary>
/// <param name="message">The message, which a test fills with text that must never reach a caller.</param>
internal sealed class FakeDbException(string message) : DbException(message);

/// <summary>
/// The endpoints that the tests add to the real pipeline, for behavior that the endpoints of the catalog do not show or must never show: one endpoint for each access level, an endpoint that reports who the caller
/// is, one that reports what the forwarded headers did, and endpoints that fail. They are mapped after <c>UseCatalogApi</c>, so they pass through the same middleware as every route of the API.
/// </summary>
internal static class TestEndpoints
{
    public const string ThrownSecret = "internal detail that a caller must never see";

    public const string DriverSecret = "Login failed for user sa with password Pw-Secret-database";

    public static void Map(WebApplication app)
    {
        var routes = app.MapGroup("/test");

        routes.MapGet("/read", () => Results.Ok(new { access = "read" })).RequireAuthorization(CatalogPolicies.NameOf(AccessLevel.Read));
        routes.MapPost("/write", () => Results.Ok(new { access = "write" })).RequireAuthorization(CatalogPolicies.NameOf(AccessLevel.Write));
        routes.MapPost("/admin", () => Results.Ok(new { access = "admin" })).RequireAuthorization(CatalogPolicies.NameOf(AccessLevel.Admin));

        routes.MapGet("/whoami", (ICurrentPrincipal principal) => Results.Ok(new { principal.IsAuthenticated, principal.CallerId, scopes = principal.Scopes.Order().ToArray(), roles = principal.Roles.Order().ToArray() }))
            .RequireAuthorization(CatalogPolicies.NameOf(AccessLevel.Read));

        routes.MapGet("/remote", (HttpContext context) => Results.Ok(new
        {
            address = context.Connection.RemoteIpAddress?.ToString(),
            scheme = context.Request.Scheme,
            host = context.Request.Host.Value,
        }));

        routes.MapGet("/validation", () => Problems.Validation(new FieldError("price.amount", "The amount must have at most 2 decimal places for USD."), new FieldError("name", "The name is required."), new FieldError("name", "The name must have between 1 and 200 characters.")));
        routes.MapGet("/boom", Task<IResult> () => throw new InvalidOperationException(ThrownSecret));
        routes.MapGet("/database-down", Task<IResult> () => throw new InvalidOperationException("The save failed.", new FakeDbException(DriverSecret)));
    }
}
