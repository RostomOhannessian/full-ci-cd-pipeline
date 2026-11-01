using Catalog.Application.Categories;
using Catalog.Application.Messaging;
using Catalog.Application.Products;
using Catalog.Application.Results;
using Catalog.Application.Security;
using Catalog.Contract.Tests.Support;
using Xunit;

namespace Catalog.Contract.Tests;

/// <summary>
/// REQ-APP-004, REQ-IDN-002, and ADR-0028: the mapping table of the application and the permission table say the same thing as the error catalog and the contract.
/// The application never reads the documents, so a change to a status, a title, or a scope in one place fails here until the other follows.
/// </summary>
public sealed class ApplicationAgreementTests
{
    [Fact]
    [Trait("Requirement", "REQ-APP-004")]
    public void Every_row_of_the_mapping_table_is_an_entry_of_the_catalog_with_the_same_status_and_title()
    {
        foreach (var type in ProblemMapping.All)
        {
            Assert.True(ErrorCatalogTests.Entries.TryGetValue(type.Slug, out var entry), $"'{type.Slug}' is not an entry of the error catalog.");
            Assert.Equal(entry.Status, type.Status);
            Assert.Equal(entry.Title, type.Title);
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-004")]
    public void The_entries_that_the_table_leaves_out_are_the_ones_that_only_the_api_raises()
    {
        string[] raisedByTheApi =
        [
            "malformed-request", "idempotency-key-required", "unauthorized", "payload-too-large", "unsupported-media-type", "rate-limited", "internal-error", "service-unavailable",
        ];

        var left = ErrorCatalogTests.Entries.Keys.Except(ProblemMapping.All.Select(type => type.Slug)).Order(StringComparer.Ordinal);

        Assert.Equal(raisedByTheApi.Order(StringComparer.Ordinal), left);
    }

    [Fact]
    [Trait("Requirement", "REQ-IDN-002")]
    public void The_permission_table_names_the_scopes_of_the_contract_and_the_roles_of_the_realm()
    {
        var contractScopes = Authored.Document.Components!.SecuritySchemes!.Values
            .SelectMany(scheme => scheme.Flows?.AuthorizationCode?.Scopes?.Keys ?? [])
            .Distinct()
            .Order(StringComparer.Ordinal);

        Assert.Equal(contractScopes, PermissionPolicy.Requirements.Select(requirement => requirement.Scope).Order(StringComparer.Ordinal));

        var realm = Authored.ReadText("identity", "keycloak", "realms", "catalog-realm.json");
        foreach (var role in PermissionPolicy.Requirements.SelectMany(requirement => requirement.Roles).Distinct())
        {
            Assert.Contains($"\"name\": \"{role}\"", realm, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-IDN-002")]
    public void Every_use_case_asks_for_the_scope_that_the_contract_names_for_its_operation()
    {
        Dictionary<string, string> asked = new()
        {
            ["listCategories"] = ScopeOf<ListCategories>(),
            ["createCategory"] = ScopeOf<CreateCategory>(),
            ["getCategory"] = ScopeOf<GetCategory>(),
            ["updateCategory"] = ScopeOf<RenameCategory>(),
            ["discontinueCategory"] = ScopeOf<DiscontinueCategory>(),
            ["listProducts"] = ScopeOf<ListProducts>(),
            ["createProduct"] = ScopeOf<CreateProduct>(),
            ["getProduct"] = ScopeOf<GetProduct>(),
            ["updateProduct"] = ScopeOf<UpdateProduct>(),
            ["discontinueProduct"] = ScopeOf<DiscontinueProduct>(),
        };

        Assert.Equal(ContractOperation.All.Select(operation => operation.OperationId).Order(StringComparer.Ordinal), asked.Keys.Order(StringComparer.Ordinal));

        foreach (var operation in ContractOperation.All)
        {
            Assert.Equal(Assert.Single(operation.Scopes), asked[operation.OperationId]);
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-IDN-002")]
    public void The_scopes_follow_the_permission_matrix_of_the_plan()
    {
        Assert.Equal("catalog.read", ScopeOf<GetProduct>());
        Assert.Equal("catalog.read", ScopeOf<ListCategories>());
        Assert.Equal("catalog.write", ScopeOf<CreateProduct>());
        Assert.Equal("catalog.write", ScopeOf<UpdateProduct>());
        Assert.Equal("catalog.admin", ScopeOf<DiscontinueProduct>());
        Assert.Equal("catalog.admin", ScopeOf<CreateCategory>());
        Assert.Equal("catalog.admin", ScopeOf<RenameCategory>());
        Assert.Equal("catalog.admin", ScopeOf<DiscontinueCategory>());
    }

    private static string ScopeOf<TRequest>()
        where TRequest : IProtectedRequest => PermissionPolicy.For(TRequest.RequiredAccess).Scope;

    [Fact]
    [Trait("Requirement", "REQ-IDN-002")]
    public void Every_operation_of_the_contract_asks_for_a_scope_that_the_permission_table_knows()
    {
        var known = PermissionPolicy.Requirements.Select(requirement => requirement.Scope).ToHashSet();

        foreach (var operation in ContractOperation.All)
        {
            var scope = Assert.Single(operation.Scopes);
            Assert.True(known.Contains(scope), $"{operation.OperationId} asks for '{scope}', which the permission table does not know.");
        }
    }
}
