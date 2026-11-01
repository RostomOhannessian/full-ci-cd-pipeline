namespace Catalog.Application.Security;

/// <summary>
/// How much of the catalog an operation needs. Each level is a scope of the access token and the roles that grant it, so the scope alone never
/// decides: Keycloak 26.8.0 grants a requested scope to a user who lacks the role that the scope maps to (ADR-0026, risk R21).
/// </summary>
public enum AccessLevel
{
    /// <summary>Read categories and products. All <c>GET</c> operations need it.</summary>
    Read,

    /// <summary>Create and change products.</summary>
    Write,

    /// <summary>Discontinue products, and create, rename, and discontinue categories.</summary>
    Admin,
}
