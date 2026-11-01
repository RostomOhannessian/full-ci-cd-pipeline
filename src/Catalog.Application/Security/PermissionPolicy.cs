using Catalog.Application.Results;

namespace Catalog.Application.Security;

/// <summary>What one <see cref="AccessLevel"/> needs from a token: the scope, and any one of the roles.</summary>
/// <param name="Level">The level.</param>
/// <param name="Scope">The scope that the token must carry.</param>
/// <param name="Roles">The roles of which the token must carry at least one. A role above the lowest one that grants the level also grants it.</param>
public sealed record PermissionRequirement(AccessLevel Level, string Scope, IReadOnlyList<string> Roles);

/// <summary>
/// The one table that says what each access level needs (plan section 8.7, ADR-0026, ADR-0032). The authorization decorator reads it, and the API binds the same table
/// to its endpoint policies (WP1.10), so the scope and the role are checked in the same way on both sides. A test compares the scopes with the version 1 contract.
/// </summary>
public static class PermissionPolicy
{
    /// <summary>The scope that grants <see cref="AccessLevel.Read"/>.</summary>
    public const string ReadScope = "catalog.read";

    /// <summary>The scope that grants <see cref="AccessLevel.Write"/>.</summary>
    public const string WriteScope = "catalog.write";

    /// <summary>The scope that grants <see cref="AccessLevel.Admin"/>.</summary>
    public const string AdminScope = "catalog.admin";

    /// <summary>The lowest role, which can read.</summary>
    public const string ReaderRole = "catalog-reader";

    /// <summary>The role that can also create and change products.</summary>
    public const string EditorRole = "catalog-editor";

    /// <summary>The role that can also discontinue products and change categories.</summary>
    public const string AdminRole = "catalog-admin";

    /// <summary>Gets the requirement of every access level, in the order of <see cref="AccessLevel"/>.</summary>
    public static IReadOnlyList<PermissionRequirement> Requirements { get; } =
    [
        new(AccessLevel.Read, ReadScope, [ReaderRole, EditorRole, AdminRole]),
        new(AccessLevel.Write, WriteScope, [EditorRole, AdminRole]),
        new(AccessLevel.Admin, AdminScope, [AdminRole]),
    ];

    /// <summary>Returns the requirement of an access level.</summary>
    /// <param name="level">The level.</param>
    /// <exception cref="ArgumentOutOfRangeException">The level is not in the table.</exception>
    public static PermissionRequirement For(AccessLevel level) =>
        Requirements.FirstOrDefault(requirement => requirement.Level == level)
        ?? throw new ArgumentOutOfRangeException(nameof(level), level, "The access level has no requirement.");

    /// <summary>
    /// Checks that a caller holds both the scope and one of the roles of an access level. The scope is checked first, so a caller without the scope is told
    /// which scope to ask for, and a caller with the scope and without the role gets a different reason.
    /// </summary>
    /// <param name="level">The level that the operation needs.</param>
    /// <param name="principal">The caller.</param>
    /// <returns>A <see cref="Forbidden"/> error, or null when the caller is allowed.</returns>
    public static Forbidden? Check(AccessLevel level, ICurrentPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var requirement = For(level);

        if (!principal.Scopes.Contains(requirement.Scope))
        {
            return new Forbidden(level, ForbiddenReason.MissingScope);
        }

        return requirement.Roles.Any(principal.Roles.Contains) ? null : new Forbidden(level, ForbiddenReason.MissingRole);
    }
}
