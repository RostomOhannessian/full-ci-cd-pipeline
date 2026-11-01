using AwesomeAssertions;
using Catalog.Application.Categories;
using Catalog.Application.Products;
using Catalog.Application.Tests.Support;
using Catalog.CrossCutting.Validation;
using Catalog.Domain.Common;
using Catalog.Domain.Tests.Support;
using FluentValidation;
using FluentValidation.Results;
using Xunit;

namespace Catalog.Application.Tests;

/// <summary>REQ-APP-005: the validators run the value objects of the domain model and report every invalid field at once, named as the contract names it, and a valid request passes.</summary>
public sealed class ValidatorTests
{
    private static readonly Guid Id = TestIds.Product(1).Value;
    private static readonly Guid CategoryId = TestIds.Category(1).Value;

    private static async Task<string[]> FieldsAsync<T>(IValidator<T> validator, T request)
    {
        ValidationResult result = await validator.ValidateAsync(request);

        return [.. result.Errors.Select(failure => failure.PropertyName)];
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-005")]
    public async Task A_valid_product_passes_every_validator()
    {
        (await FieldsAsync(new CreateProductValidator(), new CreateProduct("KB-100", "Keyboard", null, 89.99m, "USD", CategoryId))).Should().BeEmpty();
        (await FieldsAsync(new UpdateProductValidator(), new UpdateProduct(Id, "Keyboard", "text", 0m, "JPY", CategoryId, null))).Should().BeEmpty();
        (await FieldsAsync(new DiscontinueProductValidator(), new DiscontinueProduct(Id, null))).Should().BeEmpty();
        (await FieldsAsync(new GetProductValidator(), new GetProduct(Id))).Should().BeEmpty();
        (await FieldsAsync(new ListProductsValidator(), new ListProducts(CategoryId, LifecycleStatus.Active, "key", "name", 100))).Should().BeEmpty();
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-005")]
    public async Task A_product_that_breaks_every_rule_reports_each_field_once_in_the_order_of_the_request()
    {
        var create = await FieldsAsync(new CreateProductValidator(), new CreateProduct("!", string.Empty, new string('d', 2001), 1m, "XXX", Make.NotUuid7));
        var update = await FieldsAsync(new UpdateProductValidator(), new UpdateProduct(Make.NotUuid7, " ", null, -1m, "USD", Make.NotUuid7, null));

        create.Should().Equal("sku", "name", "description", "price.currency", "categoryId");
        update.Should().Equal("productId", "name", "price.amount", "categoryId");
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-005")]
    public async Task A_price_with_too_many_decimals_for_its_currency_is_reported_on_the_amount()
    {
        (await FieldsAsync(new CreateProductValidator(), new CreateProduct("KB-100", "Keyboard", null, 10.5m, "JPY", CategoryId))).Should().Equal("price.amount");
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-005")]
    public async Task An_identifier_that_is_not_a_uuid_version_7_is_reported_on_its_field()
    {
        (await FieldsAsync(new DiscontinueProductValidator(), new DiscontinueProduct(Make.NotUuid7, null))).Should().Equal("productId");
        (await FieldsAsync(new GetProductValidator(), new GetProduct(Guid.Empty))).Should().Equal("productId");
        (await FieldsAsync(new DiscontinueCategoryValidator(), new DiscontinueCategory(Make.NotUuid7, null))).Should().Equal("categoryId");
        (await FieldsAsync(new GetCategoryValidator(), new GetCategory(Make.NotUuid7))).Should().Equal("categoryId");
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-005")]
    public async Task The_category_validators_check_the_name_and_the_identifier_together()
    {
        (await FieldsAsync(new CreateCategoryValidator(), new CreateCategory("Keyboards"))).Should().BeEmpty();
        (await FieldsAsync(new CreateCategoryValidator(), new CreateCategory(null))).Should().Equal("name");
        (await FieldsAsync(new RenameCategoryValidator(), new RenameCategory(CategoryId, "Boards", null))).Should().BeEmpty();
        (await FieldsAsync(new RenameCategoryValidator(), new RenameCategory(Make.NotUuid7, new string('n', 101), null))).Should().Equal("categoryId", "name");
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-005")]
    public async Task The_list_validators_report_the_page_size_and_the_cursor_and_nothing_for_a_valid_page()
    {
        (await FieldsAsync(new ListCategoriesValidator(), new ListCategories(20, null))).Should().BeEmpty();
        (await FieldsAsync(new ListCategoriesValidator(), new ListCategories(101, "nope"))).Should().Equal("pageSize", "cursor");
        (await FieldsAsync(new ListProductsValidator(), new ListProducts(PageSize: 0, Sort: "x", Cursor: "nope"))).Should().Equal("sort", "pageSize", "cursor");
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-005")]
    public void The_shared_step_needs_a_context_and_a_result()
    {
        var withoutContext = () => DomainRules.Report<CreateCategory, string>(null!, DomainResult.Success("x"));
        var withoutResult = () => DomainRules.Report<CreateCategory, string>(new ValidationContext<CreateCategory>(new CreateCategory("x")), null!);

        withoutContext.Should().Throw<ArgumentNullException>();
        withoutResult.Should().Throw<ArgumentNullException>();
    }
}
