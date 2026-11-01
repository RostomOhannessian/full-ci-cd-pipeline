namespace Catalog.Application.Security;

/// <summary>
/// The caller of the current use case, as the API saw it after it validated the access token. The adapter in the API implements it over the claims of the
/// token (WP1.10), and a test passes a fake.
/// </summary>
public interface ICurrentPrincipal
{
    /// <summary>Gets a value that says whether the request carried a valid access token.</summary>
    bool IsAuthenticated { get; }

    /// <summary>Gets the identifier of the caller: <c>sub</c> for a person, and <c>azp</c> for a client that uses client credentials. It is empty when the caller is not authenticated.</summary>
    string CallerId { get; }

    /// <summary>Gets the scopes of the token, such as <c>catalog.read</c>.</summary>
    IReadOnlySet<string> Scopes { get; }

    /// <summary>Gets the realm roles of the token, such as <c>catalog-editor</c>.</summary>
    IReadOnlySet<string> Roles { get; }
}
