using AwesomeAssertions;
using Catalog.Application.Results;
using Catalog.Application.Security;
using Catalog.Application.Tests.Support;
using Xunit;

namespace Catalog.Application.Tests;

/// <summary>
/// REQ-APP-005, REQ-IDN-002, and risk R21: an access level needs its scope and one of its roles. The scope alone never decides, because Keycloak 26.8.0 grants a
/// requested scope to a user who lacks the role that the scope maps to.
/// </summary>
public sealed class PermissionPolicyTests
{
    private static readonly string[] Scopes = [PermissionPolicy.ReadScope, PermissionPolicy.WriteScope, PermissionPolicy.AdminScope];
    private static readonly string[] Roles = [PermissionPolicy.ReaderRole, PermissionPolicy.EditorRole, PermissionPolicy.AdminRole];

    [Fact]
    [Trait("Requirement", "REQ-IDN-002")]
    public void Each_access_level_names_one_scope_and_the_roles_that_grant_it()
    {
        PermissionPolicy.Requirements.Select(requirement => (requirement.Level, requirement.Scope, string.Join(',', requirement.Roles))).Should().Equal(
            (AccessLevel.Read, "catalog.read", "catalog-reader,catalog-editor,catalog-admin"),
            (AccessLevel.Write, "catalog.write", "catalog-editor,catalog-admin"),
            (AccessLevel.Admin, "catalog.admin", "catalog-admin"));
        Enum.GetValues<AccessLevel>().Should().BeEquivalentTo(PermissionPolicy.Requirements.Select(requirement => requirement.Level));
    }

    [Theory]
    [InlineData(AccessLevel.Read)]
    [InlineData(AccessLevel.Write)]
    [InlineData(AccessLevel.Admin)]
    [Trait("Requirement", "REQ-APP-005")]
    [Trait("Requirement", "REQ-IDN-002")]
    public void A_caller_is_allowed_exactly_when_it_holds_the_scope_and_one_of_the_roles(AccessLevel level)
    {
        var requirement = PermissionPolicy.For(level);

        // Every combination of scopes and roles: 2^3 scopes times 2^3 roles.
        for (var scopeBits = 0; scopeBits < 8; scopeBits++)
        {
            for (var roleBits = 0; roleBits < 8; roleBits++)
            {
                var scopes = Scopes.Where((_, index) => (scopeBits & (1 << index)) != 0).ToList();
                var roles = Roles.Where((_, index) => (roleBits & (1 << index)) != 0).ToList();
                var expectedAllowed = scopes.Contains(requirement.Scope) && roles.Intersect(requirement.Roles).Any();

                var forbidden = PermissionPolicy.Check(level, TestPrincipal.With(scopes, roles));

                (forbidden is null).Should().Be(expectedAllowed, $"scopes [{string.Join(' ', scopes)}] and roles [{string.Join(' ', roles)}] for {level}");
            }
        }
    }

    [Theory]
    [InlineData(AccessLevel.Write)]
    [InlineData(AccessLevel.Admin)]
    [Trait("Requirement", "REQ-APP-005")]
    [Trait("Requirement", "REQ-IDN-002")]
    public void A_reader_that_Keycloak_gave_every_scope_is_still_refused_for_the_role(AccessLevel level)
    {
        var reader = TestPrincipal.With(Scopes, [PermissionPolicy.ReaderRole]);

        var forbidden = PermissionPolicy.Check(level, reader);

        forbidden.Should().Be(new Forbidden(level, ForbiddenReason.MissingRole));
    }

    [Fact]
    [Trait("Requirement", "REQ-IDN-002")]
    public void A_caller_without_the_scope_is_told_the_scope_even_when_it_also_lacks_the_role()
    {
        var nobody = TestPrincipal.With([], []);

        PermissionPolicy.Check(AccessLevel.Write, nobody).Should().Be(new Forbidden(AccessLevel.Write, ForbiddenReason.MissingScope));
    }

    [Fact]
    [Trait("Requirement", "REQ-IDN-002")]
    public void The_roles_form_a_ladder_from_reader_to_editor_to_admin()
    {
        var editor = TestPrincipal.With(Scopes, [PermissionPolicy.EditorRole]);
        var admin = TestPrincipal.With(Scopes, [PermissionPolicy.AdminRole]);

        PermissionPolicy.Check(AccessLevel.Read, editor).Should().BeNull();
        PermissionPolicy.Check(AccessLevel.Write, editor).Should().BeNull();
        PermissionPolicy.Check(AccessLevel.Admin, editor).Should().NotBeNull();
        PermissionPolicy.Check(AccessLevel.Admin, admin).Should().BeNull();
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-005")]
    public void A_missing_argument_or_an_unknown_level_is_a_programming_error()
    {
        var withoutPrincipal = () => PermissionPolicy.Check(AccessLevel.Read, null!);
        var unknownLevel = () => PermissionPolicy.For((AccessLevel)99);

        withoutPrincipal.Should().Throw<ArgumentNullException>();
        unknownLevel.Should().Throw<ArgumentOutOfRangeException>();
    }
}
