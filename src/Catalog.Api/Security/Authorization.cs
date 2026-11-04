using Catalog.Api.Http;
using Catalog.Application.Messaging;
using Catalog.Application.Results;
using Catalog.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Catalog.Api.Security;

/// <summary>What an endpoint needs from a caller: one access level, which is a scope and one of its roles (plan section 8.7, ADR-0026, ADR-0032).</summary>
/// <param name="Level">The access level.</param>
internal sealed record AccessRequirement(AccessLevel Level) : IAuthorizationRequirement;

/// <summary>
/// Checks an <see cref="AccessRequirement"/> with <see cref="PermissionPolicy"/>, the same table that the authorization decorator of the pipeline reads, so the endpoint and the use case cannot disagree about what a scope
/// and a role allow. A token needs both, because Keycloak 26.8.0 grants a requested scope to a user who lacks the mapped role (risk R21). The reason of a refusal travels with the failure, so the answer can name what is missing.
/// </summary>
internal sealed class AccessRequirementHandler : AuthorizationHandler<AccessRequirement>
{
    /// <inheritdoc />
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AccessRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        var forbidden = PermissionPolicy.Check(requirement.Level, new ClaimsCurrentPrincipal(context.User));

        if (forbidden is null)
        {
            context.Succeed(requirement);
        }
        else
        {
            context.Fail(new AuthorizationFailureReason(this, forbidden.Reason.ToString()));
        }

        return Task.CompletedTask;
    }
}

/// <summary>The names of the endpoint policies, one for each access level, and their registration.</summary>
internal static class CatalogPolicies
{
    /// <summary>Returns the name of the policy of an access level, which is the scope of the level.</summary>
    /// <param name="level">The access level.</param>
    public static string NameOf(AccessLevel level) => PermissionPolicy.For(level).Scope;

    /// <summary>Adds one policy for each access level of <see cref="PermissionPolicy"/>. A caller must be authenticated and must satisfy the requirement.</summary>
    /// <param name="options">The authorization options.</param>
    public static void AddTo(AuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        foreach (var requirement in PermissionPolicy.Requirements)
        {
            options.AddPolicy(NameOf(requirement.Level), policy => policy.RequireAuthenticatedUser().AddRequirements(new AccessRequirement(requirement.Level)));
        }
    }
}

/// <summary>Binds an endpoint to the access level of its use case, so the endpoint and the pipeline read the level from the same request type.</summary>
internal static class AccessEndpointExtensions
{
    /// <summary>Requires the access level that a request type declares.</summary>
    /// <typeparam name="TBuilder">The type of the builder.</typeparam>
    /// <typeparam name="TRequest">The command or query that the endpoint runs.</typeparam>
    /// <param name="builder">The endpoint.</param>
    public static TBuilder RequireAccessOf<TBuilder, TRequest>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
        where TRequest : IProtectedRequest =>
        builder.RequireAuthorization(CatalogPolicies.NameOf(TRequest.RequiredAccess));
}

/// <summary>
/// Writes the answer to a request that the endpoint policy refused (ADR-0028, ADR-0040). A caller without a token is challenged by the bearer handler, which answers 401 with its own challenge, and a caller whose token
/// lacks the scope or the role gets a 403 <c>forbidden</c> whose detail names what the operation requires.
/// </summary>
internal sealed class CatalogAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    /// <inheritdoc />
    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(authorizeResult);

        if (authorizeResult is { Forbidden: true, AuthorizationFailure: { } failure })
        {
            await ForbiddenOf(policy, failure).ExecuteAsync(context);

            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }

    private static ProblemResult ForbiddenOf(AuthorizationPolicy policy, AuthorizationFailure failure)
    {
        var level = policy.Requirements.OfType<AccessRequirement>().Select(requirement => (AccessLevel?)requirement.Level).FirstOrDefault();
        var reason = failure.FailureReasons.Select(failed => failed.Message).FirstOrDefault();

        if (level is { } required && Enum.TryParse<ForbiddenReason>(reason, out var forbiddenReason))
        {
            return Problems.For(new Forbidden(required, forbiddenReason));
        }

        return Problems.Of(ProblemMapping.Forbidden, "The token does not allow this operation.");
    }
}
