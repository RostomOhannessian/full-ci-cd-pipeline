using Catalog.Application.Security;

namespace Catalog.Application.Tests.Support;

/// <summary>A caller for tests, with the scopes and the roles that a test names.</summary>
internal sealed class TestPrincipal : ICurrentPrincipal
{
    public bool IsAuthenticated { get; init; } = true;

    public string CallerId { get; init; } = "caller-1";

    public IReadOnlySet<string> Scopes { get; init; } = new HashSet<string>();

    public IReadOnlySet<string> Roles { get; init; } = new HashSet<string>();

    /// <summary>Returns a caller that holds the scopes and the roles.</summary>
    public static TestPrincipal With(IEnumerable<string> scopes, IEnumerable<string> roles) =>
        new() { Scopes = new HashSet<string>(scopes), Roles = new HashSet<string>(roles) };
}
