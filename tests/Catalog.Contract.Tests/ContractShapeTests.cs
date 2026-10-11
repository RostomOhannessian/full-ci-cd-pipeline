using Catalog.Contract.Tests.Support;
using Microsoft.OpenApi;
using Xunit;

namespace Catalog.Contract.Tests;

/// <summary>
/// REQ-CON-001 and REQ-API-001: the authored OpenAPI document is the source of truth, and it describes the resources, the operations, the filters, and the
/// scopes that plan sections 8.6 and 8.7 list.
/// </summary>
public sealed class ContractShapeTests
{
    [Fact]
    [Trait("Requirement", "REQ-CON-001")]
    public void The_contract_is_an_openapi_3_1_document_at_the_path_that_the_adr_gives()
    {
        Assert.Equal("contracts/openapi/catalog/v1/catalog.openapi.yaml", Authored.RelativePath);
        Assert.True(File.Exists(Authored.ContractPath));
        Assert.Contains("openapi: 3.1.0", Authored.ReadText("contracts", "openapi", "catalog", "v1", "catalog.openapi.yaml"), StringComparison.Ordinal);
        Assert.Equal("1.0.0", Authored.Document.Info?.Version);
    }

    [Fact]
    [Trait("Requirement", "REQ-CON-001")]
    public void The_contract_is_the_only_authored_openapi_document_of_the_repository()
    {
        string[] skipped = ["bin", "obj", "node_modules", ".dev", ".docfx", "_site", ".git", "TestResults"];

        var documents = Directory.EnumerateFiles(Authored.Root, "*.openapi.*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(Authored.Root, path).Replace('\\', '/'))
            .Where(path => !path.Split('/').Any(segment => skipped.Contains(segment, StringComparer.Ordinal)))
            .ToList();

        Assert.Equal([Authored.RelativePath], documents);
    }

    [Fact]
    [Trait("Requirement", "REQ-API-001")]
    public void The_contract_defines_the_ten_operations_of_the_plan_and_no_other()
    {
        string[] expected =
        [
            "GET /api/v1/categories listCategories",
            "POST /api/v1/categories createCategory",
            "GET /api/v1/categories/{categoryId} getCategory",
            "PUT /api/v1/categories/{categoryId} updateCategory",
            "POST /api/v1/categories/{categoryId}/discontinue discontinueCategory",
            "GET /api/v1/products listProducts",
            "POST /api/v1/products createProduct",
            "GET /api/v1/products/{productId} getProduct",
            "PUT /api/v1/products/{productId} updateProduct",
            "POST /api/v1/products/{productId}/discontinue discontinueProduct",
        ];

        var actual = ContractOperation.All.Select(operation => $"{operation.Method} {operation.Path} {operation.OperationId}").ToList();

        Assert.Equal(expected.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Requirement", "REQ-API-001")]
    public void The_product_list_takes_the_filters_the_sort_and_the_page_parameters_of_the_plan()
    {
        var list = ContractOperation.Get("listProducts");

        Assert.Equal(["categoryId", "status", "q", "sort", "pageSize", "cursor"], list.QueryParameters);

        var parameters = list.Operation.Parameters!.ToDictionary(parameter => parameter.Name!);
        Assert.Equal(["createdAt", "name"], parameters["sort"].Schema!.Enum!.Select(value => value!.GetValue<string>()));
        Assert.Equal("createdAt", parameters["sort"].Schema!.Default!.GetValue<string>());
        Assert.Equal(["active", "discontinued"], parameters["status"].Schema!.Enum!.Select(value => value!.GetValue<string>()));
        Assert.All(list.Operation.Parameters!.Where(parameter => parameter.In == ParameterLocation.Query), parameter => Assert.False(parameter.Required, $"{parameter.Name} narrows the list, so it is optional."));
    }

    [Theory]
    [InlineData("listProducts")]
    [InlineData("listCategories")]
    [Trait("Requirement", "REQ-API-001")]
    public void A_list_pages_with_a_size_of_at_most_100_and_an_opaque_cursor(string operationId)
    {
        var parameters = ContractOperation.Get(operationId).Operation.Parameters!.ToDictionary(parameter => parameter.Name!);

        var pageSize = parameters["pageSize"].Schema!;
        Assert.Equal("1", pageSize.Minimum);
        Assert.Equal("100", pageSize.Maximum);
        Assert.Equal(20, Json.Int(pageSize.Default));
        Assert.Equal(512, parameters["cursor"].Schema!.MaxLength);
        Assert.Equal("^[A-Za-z0-9_-]+$", parameters["cursor"].Schema!.Pattern);
    }

    [Theory]
    [InlineData("ProductPage")]
    [InlineData("CategoryPage")]
    [Trait("Requirement", "REQ-API-001")]
    public void A_page_has_items_and_a_next_cursor_that_is_null_on_the_last_page(string schemaName)
    {
        var page = Authored.Document.Components!.Schemas![schemaName];

        Assert.Equal(["items", "nextCursor"], page.Required!.Order(StringComparer.Ordinal));
        Assert.True(page.Properties!["nextCursor"].Type!.Value.HasFlag(JsonSchemaType.Null), "nextCursor must allow null on the last page.");
        Assert.True(page.Properties["nextCursor"].Type!.Value.HasFlag(JsonSchemaType.String));
    }

    [Fact]
    [Trait("Requirement", "REQ-API-001")]
    public void The_oauth_scheme_defines_exactly_the_three_scopes_and_every_operation_uses_a_defined_one()
    {
        var scheme = Authored.Document.Components!.SecuritySchemes!["oauth2"];
        string[] expected = ["catalog.admin", "catalog.read", "catalog.write"];

        Assert.Equal(SecuritySchemeType.OAuth2, scheme.Type);
        Assert.Equal(expected, scheme.Flows!.AuthorizationCode!.Scopes!.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(expected, scheme.Flows.ClientCredentials!.Scopes!.Keys.Order(StringComparer.Ordinal));

        foreach (var operation in ContractOperation.All)
        {
            var scope = Assert.Single(operation.Scopes);
            Assert.Contains(scope, expected);
        }
    }

    [Theory]
    [InlineData("listCategories", "catalog.read")]
    [InlineData("getCategory", "catalog.read")]
    [InlineData("listProducts", "catalog.read")]
    [InlineData("getProduct", "catalog.read")]
    [InlineData("createProduct", "catalog.write")]
    [InlineData("updateProduct", "catalog.write")]
    [InlineData("discontinueProduct", "catalog.admin")]
    [InlineData("createCategory", "catalog.admin")]
    [InlineData("updateCategory", "catalog.admin")]
    [InlineData("discontinueCategory", "catalog.admin")]
    [Trait("Requirement", "REQ-API-001")]
    public void Each_operation_asks_for_the_scope_of_plan_section_8_7(string operationId, string scope)
    {
        Assert.Equal([scope], ContractOperation.Get(operationId).Scopes);
    }

    [Fact]
    [Trait("Requirement", "REQ-API-001")]
    public void A_product_is_described_with_the_fields_of_the_domain_and_an_update_cannot_change_the_sku()
    {
        var schemas = Authored.Document.Components!.Schemas!;

        Assert.Equal(["categoryId", "createdAt", "description", "id", "name", "price", "sku", "status", "updatedAt"], schemas["Product"].Properties!.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(["categoryId", "description", "name", "price", "sku"], schemas["ProductCreate"].Properties!.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(["categoryId", "description", "name", "price"], schemas["ProductUpdate"].Properties!.Keys.Order(StringComparer.Ordinal));
        Assert.DoesNotContain("sku", schemas["ProductUpdate"].Properties!.Keys);
        Assert.DoesNotContain("id", schemas["ProductCreate"].Properties!.Keys);
        Assert.Equal(["createdAt", "id", "name", "status", "updatedAt"], schemas["Category"].Properties!.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(["name"], schemas["CategoryUpdate"].Properties!.Keys);
    }

    [Fact]
    [Trait("Requirement", "REQ-API-001")]
    public void A_category_and_a_product_move_from_active_to_discontinued_and_nowhere_else()
    {
        var status = Authored.Document.Components!.Schemas!["LifecycleStatus"];

        Assert.Equal(["active", "discontinued"], status.Enum!.Select(value => value!.GetValue<string>()));
        Assert.DoesNotContain(ContractOperation.All, operation => operation.Method == "DELETE");
    }

    [Fact]
    [Trait("Requirement", "REQ-API-001")]
    public void The_currencies_are_an_iso_4217_set_and_the_amount_is_a_decimal_string()
    {
        var schemas = Authored.Document.Components!.Schemas!;

        Assert.Equal(["USD", "EUR", "GBP", "JPY"], schemas["CurrencyCode"].Enum!.Select(value => value!.GetValue<string>()));
        Assert.True(schemas["Money"].Properties!["amount"].Type!.Value.HasFlag(JsonSchemaType.String), "A decimal amount is a string, so no client loses digits.");
        Assert.False(schemas["Money"].AdditionalPropertiesAllowed);
    }
}
