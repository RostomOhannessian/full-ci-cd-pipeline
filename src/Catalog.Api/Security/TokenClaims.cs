using System.Security.Claims;
using System.Text.Json;
using Catalog.Application.Security;

namespace Catalog.Api.Security;

/// <summary>
/// Reads the claims that the API trusts from a validated access token (plan section 8.7, ADR-0040). The token handler does not rename claims, so the names are the ones that Keycloak writes: <c>scope</c> is one
/// space-delimited string, the realm roles are in the object <c>realm_access</c>, and the caller is <c>sub</c>, or <c>azp</c> when a token has no subject. A claim that is missing or not in the expected shape gives an
/// empty set, so a malformed token never grants anything.
/// </summary>
internal static class TokenClaims
{
    /// <summary>The claim that holds the space-delimited scopes.</summary>
    public const string Scope = "scope";

    /// <summary>The claim that holds the object with the realm roles.</summary>
    public const string RealmAccess = "realm_access";

    /// <summary>Returns the scopes of the token.</summary>
    /// <param name="user">The validated principal.</param>
    public static IReadOnlySet<string> Scopes(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return user.FindAll(Scope)
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Returns the realm roles of the token.</summary>
    /// <param name="user">The validated principal.</param>
    public static IReadOnlySet<string> Roles(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);

        HashSet<string> roles = new(StringComparer.Ordinal);

        foreach (var claim in user.FindAll(RealmAccess))
        {
            try
            {
                using var document = JsonDocument.Parse(claim.Value);

                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("roles", out var list)
                    && list.ValueKind == JsonValueKind.Array)
                {
                    foreach (var role in list.EnumerateArray().Where(role => role.ValueKind == JsonValueKind.String))
                    {
                        roles.Add(role.GetString()!);
                    }
                }
            }
            catch (JsonException)
            {
                // A claim that is not JSON grants no role.
            }
        }

        return roles;
    }

    /// <summary>Returns the identifier of the caller: <c>sub</c>, or <c>azp</c> when the token has no subject.</summary>
    /// <param name="user">The validated principal.</param>
    public static string CallerId(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return user.FindFirst("sub")?.Value ?? user.FindFirst("azp")?.Value ?? string.Empty;
    }
}

/// <summary>The caller of a request, as <see cref="ICurrentPrincipal"/> describes it, read from a validated principal.</summary>
/// <param name="user">The principal that the authentication middleware built from the token, or an empty one.</param>
internal sealed class ClaimsCurrentPrincipal(ClaimsPrincipal user) : ICurrentPrincipal
{
    private readonly ClaimsPrincipal _user = user ?? throw new ArgumentNullException(nameof(user));
    private IReadOnlySet<string>? _scopes;
    private IReadOnlySet<string>? _roles;

    /// <inheritdoc />
    public bool IsAuthenticated => _user.Identity?.IsAuthenticated == true;

    /// <inheritdoc />
    public string CallerId => IsAuthenticated ? TokenClaims.CallerId(_user) : string.Empty;

    /// <inheritdoc />
    public IReadOnlySet<string> Scopes => _scopes ??= IsAuthenticated ? TokenClaims.Scopes(_user) : new HashSet<string>();

    /// <inheritdoc />
    public IReadOnlySet<string> Roles => _roles ??= IsAuthenticated ? TokenClaims.Roles(_user) : new HashSet<string>();
}

/// <summary>The caller of the current request. A scoped service reads it from the <c>HttpContext</c>, and the use cases of the pipeline see the same caller that the endpoint policy saw.</summary>
/// <param name="accessor">The accessor of the current request.</param>
internal sealed class HttpContextCurrentPrincipal(IHttpContextAccessor accessor) : ICurrentPrincipal
{
    private readonly IHttpContextAccessor _accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
    private ClaimsCurrentPrincipal? _principal;

    /// <inheritdoc />
    public bool IsAuthenticated => Principal.IsAuthenticated;

    /// <inheritdoc />
    public string CallerId => Principal.CallerId;

    /// <inheritdoc />
    public IReadOnlySet<string> Scopes => Principal.Scopes;

    /// <inheritdoc />
    public IReadOnlySet<string> Roles => Principal.Roles;

    private ClaimsCurrentPrincipal Principal => _principal ??= new ClaimsCurrentPrincipal(_accessor.HttpContext?.User ?? new ClaimsPrincipal());
}
