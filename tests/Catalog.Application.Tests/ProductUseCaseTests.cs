using AwesomeAssertions;
using Catalog.Application.Products;
using Catalog.Application.Results;
using Catalog.Application.Tests.Support;
using Catalog.Domain.Common;
using Catalog.Domain.Products;
using Catalog.Domain.Tests.Support;
using Xunit;

namespace Catalog.Application.Tests;

/// <summary>
/// REQ-APP-001, REQ-APP-004, REQ-API-003, REQ-API-004, and REQ-DOM-003: the use cases of products run through the production chain of decorators, over an in-memory catalog that has versions and an outbox of
/// events. The error of each refusal is the typed error that ADR-0028 maps to one entry of the error catalog.
/// </summary>
public sealed class ProductUseCaseTests
{
    private static CreateProduct Create(Guid categoryId, string sku = "kb-100", string name = "Keyboard", decimal amount = 89.99m, string currency = "USD", string? key = "key-1") =>
        new(sku, name, "A mechanical keyboard.", amount, currency, categoryId, key is null ? null : Make.Key(key));

    [Fact]
    [Trait("Requirement", "REQ-APP-001")]
    public async Task Creating_a_product_returns_its_view_with_a_new_identifier_a_normalized_sku_and_a_version()
    {
        var rig = new UseCaseRig();
        var category = rig.SeedCategory();

        var result = await rig.Command<CreateProduct, ProductView>().HandleAsync(Create(category.Id.Value), CancellationToken.None);

        var view = result.Value;
        view.Id.Should().Be(TestIds.Product(1001).Value);
        view.Sku.Should().Be("KB-100");
        view.Name.Should().Be("Keyboard");
        view.Amount.Should().Be(89.99m);
        view.Currency.Should().Be("USD");
        view.CategoryId.Should().Be(category.Id.Value);
        view.Status.Should().Be(LifecycleStatus.Active);
        view.CreatedAt.Should().Be(UseCaseRig.Epoch);
        view.ETag.Should().Be(InMemoryCatalog.TagForTests(1).Value);
        rig.Catalog.PublishedEvents.OfType<ProductCreated>().Should().ContainSingle();
        rig.UnitOfWork.Saves.Should().Be(1);
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-004")]
    public async Task A_second_product_with_the_same_sku_is_a_conflict_and_nothing_is_added()
    {
        var rig = new UseCaseRig();
        var category = rig.SeedCategory();
        rig.SeedProduct(category, "KB-100");

        var result = await rig.Command<CreateProduct, ProductView>().HandleAsync(Create(category.Id.Value, sku: "kb-100"), CancellationToken.None);

        result.Error.Should().Be(new Conflict(ConflictReason.SkuAlreadyExists));
        rig.Catalog.AllProducts.Should().ContainSingle();
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-004")]
    public async Task A_category_that_does_not_exist_is_a_validation_error_on_the_category()
    {
        var rig = new UseCaseRig();

        var result = await rig.Command<CreateProduct, ProductView>().HandleAsync(Create(TestIds.Category(5).Value), CancellationToken.None);

        result.Error.Should().BeOfType<ValidationFailed>().Which.Errors.Should().Equal(new FieldError("categoryId", "The category does not exist."));
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-004")]
    public async Task A_discontinued_category_refuses_a_new_product_as_a_validation_error_on_the_category()
    {
        var rig = new UseCaseRig();
        var category = rig.SeedCategory(discontinued: true);

        var result = await rig.Command<CreateProduct, ProductView>().HandleAsync(Create(category.Id.Value), CancellationToken.None);

        result.Error.Should().BeOfType<ValidationFailed>().Which.Errors.Should().ContainSingle().Which.Field.Should().Be("categoryId");
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-005")]
    public async Task Every_invalid_field_comes_back_at_once_before_anything_is_looked_up()
    {
        var rig = new UseCaseRig();
        var category = rig.SeedCategory();

        var result = await rig.Command<CreateProduct, ProductView>().HandleAsync(new CreateProduct("x", " ", null, -1m, "XXX", category.Id.Value), CancellationToken.None);

        result.Error.Should().BeOfType<ValidationFailed>().Which.Errors.Select(error => error.Field).Should().Equal("sku", "name", "price.currency");
        rig.Catalog.Products.Finds.Should().Be(0);
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-005")]
    public async Task A_negative_amount_names_the_amount_of_the_price()
    {
        var rig = new UseCaseRig();
        var category = rig.SeedCategory();

        var result = await rig.Command<CreateProduct, ProductView>().HandleAsync(Create(category.Id.Value, amount: -1m), CancellationToken.None);

        result.Error.Should().BeOfType<ValidationFailed>().Which.Errors.Select(error => error.Field).Should().Equal("price.amount");
    }

    [Theory]
    [InlineData(" ", "USD", 1, "name")]
    [InlineData("Keyboard", "XXX", 1, "price.currency")]
    [InlineData("Keyboard", "USD", -1, "price.amount")]
    [Trait("Requirement", "REQ-APP-004")]
    public async Task The_handler_refuses_an_invalid_value_on_its_own_when_no_validator_runs(string name, string currency, int amount, string field)
    {
        var rig = new UseCaseRig(withValidators: false);
        var category = rig.SeedCategory();

        var result = await rig.Command<CreateProduct, ProductView>().HandleAsync(Create(category.Id.Value, name: name, currency: currency, amount: amount), CancellationToken.None);

        result.Error.Should().BeOfType<ValidationFailed>().Which.Errors.Select(error => error.Field).Should().Equal(field);
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-004")]
    public async Task The_handler_refuses_an_invalid_sku_and_an_invalid_description_on_its_own()
    {
        var rig = new UseCaseRig(withValidators: false);
        var category = rig.SeedCategory();

        var sku = await rig.Command<CreateProduct, ProductView>().HandleAsync(Create(category.Id.Value, sku: "!!"), CancellationToken.None);
        var description = await rig.Command<CreateProduct, ProductView>().HandleAsync(Create(category.Id.Value) with { Description = new string('d', 2001) }, CancellationToken.None);
        var invalidCategory = await rig.Command<CreateProduct, ProductView>().HandleAsync(Create(Make.NotUuid7), CancellationToken.None);

        sku.Error.Should().BeOfType<ValidationFailed>().Which.Errors[0].Field.Should().Be("sku");
        description.Error.Should().BeOfType<ValidationFailed>().Which.Errors[0].Field.Should().Be("description");
        invalidCategory.Error.Should().BeOfType<ValidationFailed>().Which.Errors[0].Field.Should().Be("categoryId");
    }

    [Fact]
    [Trait("Requirement", "REQ-API-004")]
    public async Task The_same_request_with_the_same_key_creates_one_product_and_replays_the_first_view()
    {
        var rig = new UseCaseRig();
        var category = rig.SeedCategory();
        var handler = rig.Command<CreateProduct, ProductView>();

        var first = await handler.HandleAsync(Create(category.Id.Value), CancellationToken.None);
        var replay = await handler.HandleAsync(Create(category.Id.Value), CancellationToken.None);

        replay.Value.Should().Be(first.Value);
        rig.Catalog.AllProducts.Should().ContainSingle();
        (await handler.HandleAsync(Create(category.Id.Value, name: "Another"), CancellationToken.None)).Error.Should().Be(new IdempotencyKeyReused());
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-001")]
    public async Task Updating_a_product_changes_its_values_and_its_version()
    {
        var rig = new UseCaseRig();
        var category = rig.SeedCategory();
        var other = rig.SeedCategory("Mice");
        var product = rig.SeedProduct(category);
        var before = rig.TagOf(product);

        var result = await rig.Command<UpdateProduct, ProductView>().HandleAsync(
            new UpdateProduct(product.Id.Value, "Keyboard 2", "New.", 99m, "EUR", other.Id.Value, before),
            CancellationToken.None);

        var view = result.Value;
        (view.Name, view.Amount, view.Currency, view.CategoryId).Should().Be(("Keyboard 2", 99m, "EUR", other.Id.Value));
        view.ETag.Should().NotBe(before.Value).And.Be(rig.TagOf(product).Value);
        rig.Catalog.PublishedEvents.Should().NotBeEmpty();
    }

    [Fact]
    [Trait("Requirement", "REQ-API-003")]
    public async Task An_update_without_a_version_is_refused_before_any_lookup()
    {
        var rig = new UseCaseRig();
        var category = rig.SeedCategory();
        var product = rig.SeedProduct(category);

        var result = await rig.Command<UpdateProduct, ProductView>().HandleAsync(
            new UpdateProduct(product.Id.Value, "Keyboard 2", null, 99m, "USD", category.Id.Value, null),
            CancellationToken.None);

        result.Error.Should().Be(new PreconditionRequired());
        rig.Catalog.Products.Finds.Should().Be(0);
    }

    [Fact]
    [Trait("Requirement", "REQ-API-003")]
    public async Task An_update_with_a_stale_version_is_refused_and_changes_nothing()
    {
        var rig = new UseCaseRig();
        var category = rig.SeedCategory();
        var product = rig.SeedProduct(category);
        var stale = InMemoryCatalog.TagForTests(99);

        var result = await rig.Command<UpdateProduct, ProductView>().HandleAsync(
            new UpdateProduct(product.Id.Value, "Keyboard 2", null, 99m, "USD", category.Id.Value, stale),
            CancellationToken.None);

        result.Error.Should().Be(new PreconditionFailed());
        product.Name.Value.Should().Be("Keyboard");
        rig.UnitOfWork.Saves.Should().Be(0);
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-004")]
    public async Task An_update_of_a_product_that_does_not_exist_is_not_found()
    {
        var rig = new UseCaseRig();
        var category = rig.SeedCategory();

        var result = await rig.Command<UpdateProduct, ProductView>().HandleAsync(
            new UpdateProduct(TestIds.Product(77).Value, "Keyboard 2", null, 99m, "USD", category.Id.Value, InMemoryCatalog.TagForTests(1)),
            CancellationToken.None);

        result.Error.Should().Be(new NotFound("product"));
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-003")]
    public async Task A_discontinued_product_refuses_a_new_price_as_a_conflict_and_still_takes_a_new_name()
    {
        var rig = new UseCaseRig();
        var category = rig.SeedCategory();
        var product = rig.SeedProduct(category, discontinued: true);
        var handler = rig.Command<UpdateProduct, ProductView>();

        var price = await handler.HandleAsync(new UpdateProduct(product.Id.Value, "Keyboard", null, 120m, "USD", category.Id.Value, rig.TagOf(product)), CancellationToken.None);
        var name = await handler.HandleAsync(new UpdateProduct(product.Id.Value, "Keyboard v2", null, 89.99m, "USD", category.Id.Value, rig.TagOf(product)), CancellationToken.None);

        price.Error.Should().Be(new Conflict(ConflictReason.ProductDiscontinued));
        name.Value.Name.Should().Be("Keyboard v2");
    }

    [Fact]
    [Trait("Requirement", "REQ-DOM-003")]
    public async Task A_move_to_a_discontinued_category_is_refused_and_staying_in_one_is_allowed()
    {
        var rig = new UseCaseRig();
        var category = rig.SeedCategory();
        var old = rig.SeedCategory("Old");
        var product = rig.SeedProduct(category);
        var stays = rig.SeedProduct(old, "OLD-1");
        old.Discontinue(rig.Clock);
        var handler = rig.Command<UpdateProduct, ProductView>();

        var move = await handler.HandleAsync(new UpdateProduct(product.Id.Value, "Keyboard", null, 89.99m, "USD", old.Id.Value, rig.TagOf(product)), CancellationToken.None);

        move.Error.Should().BeOfType<ValidationFailed>().Which.Errors[0].Field.Should().Be("categoryId");

        var rename = await handler.HandleAsync(new UpdateProduct(stays.Id.Value, "Old keyboard", null, 89.99m, "USD", old.Id.Value, rig.TagOf(stays)), CancellationToken.None);

        rename.IsSuccess.Should().BeTrue();
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-004")]
    public async Task The_update_handler_refuses_an_invalid_identifier_and_a_missing_category_on_its_own()
    {
        var rig = new UseCaseRig(withValidators: false);
        var category = rig.SeedCategory();
        var handler = rig.Command<UpdateProduct, ProductView>();
        var tag = InMemoryCatalog.TagForTests(1);

        var identifier = await handler.HandleAsync(new UpdateProduct(Make.NotUuid7, "Keyboard", null, 1m, "USD", category.Id.Value, tag), CancellationToken.None);
        var values = await handler.HandleAsync(new UpdateProduct(TestIds.Product(1).Value, " ", null, 1m, "USD", category.Id.Value, tag), CancellationToken.None);
        var product = rig.SeedProduct(category);
        var missingCategory = await handler.HandleAsync(new UpdateProduct(product.Id.Value, "Keyboard", null, 1m, "USD", TestIds.Category(500).Value, rig.TagOf(product)), CancellationToken.None);

        identifier.Error.Should().BeOfType<ValidationFailed>().Which.Errors[0].Field.Should().Be("productId");
        values.Error.Should().BeOfType<ValidationFailed>().Which.Errors[0].Field.Should().Be("name");
        missingCategory.Error.Should().BeOfType<ValidationFailed>().Which.Errors[0].Field.Should().Be("categoryId");
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-001")]
    public async Task Discontinuing_a_product_needs_the_current_version_and_discontinuing_twice_changes_nothing()
    {
        var rig = new UseCaseRig();
        var category = rig.SeedCategory();
        var product = rig.SeedProduct(category);
        var handler = rig.Command<DiscontinueProduct, ProductView>();

        var missing = await handler.HandleAsync(new DiscontinueProduct(product.Id.Value, null), CancellationToken.None);
        var stale = await handler.HandleAsync(new DiscontinueProduct(product.Id.Value, InMemoryCatalog.TagForTests(9)), CancellationToken.None);
        var first = await handler.HandleAsync(new DiscontinueProduct(product.Id.Value, rig.TagOf(product)), CancellationToken.None);
        var second = await handler.HandleAsync(new DiscontinueProduct(product.Id.Value, rig.TagOf(product)), CancellationToken.None);

        missing.Error.Should().Be(new PreconditionRequired());
        stale.Error.Should().Be(new PreconditionFailed());
        first.Value.Status.Should().Be(LifecycleStatus.Discontinued);
        second.Value.Should().Be(first.Value);
        rig.Catalog.PublishedEvents.OfType<ProductDiscontinued>().Should().ContainSingle();
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-004")]
    public async Task Discontinuing_a_product_that_does_not_exist_or_has_a_bad_identifier_is_refused()
    {
        var rig = new UseCaseRig(withValidators: false);
        var handler = rig.Command<DiscontinueProduct, ProductView>();
        var tag = InMemoryCatalog.TagForTests(1);

        var notFound = await handler.HandleAsync(new DiscontinueProduct(TestIds.Product(9).Value, tag), CancellationToken.None);
        var invalid = await handler.HandleAsync(new DiscontinueProduct(Make.NotUuid7, tag), CancellationToken.None);

        notFound.Error.Should().Be(new NotFound("product"));
        invalid.Error.Should().BeOfType<ValidationFailed>().Which.Errors[0].Field.Should().Be("productId");
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-001")]
    public async Task A_command_runs_the_whole_chain_and_the_outbox_is_woken_after_the_commit()
    {
        var rig = new UseCaseRig();
        var category = rig.SeedCategory();

        await rig.Command<CreateProduct, ProductView>().HandleAsync(Create(category.Id.Value), CancellationToken.None);

        rig.Journal.Entries.Should().ContainInOrder("telemetry:begin CreateProduct Command", "authorization", "idempotency:begin", "uow:begin", "uow:save", "uow:hooks", "idempotency:complete", "uow:commit", "outbox:dispatch");
    }
}
