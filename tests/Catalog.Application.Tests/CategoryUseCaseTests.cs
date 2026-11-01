using AwesomeAssertions;
using Catalog.Application.Categories;
using Catalog.Application.Results;
using Catalog.Application.Tests.Support;
using Catalog.Domain.Categories;
using Catalog.Domain.Common;
using Catalog.Domain.Tests.Support;
using Xunit;

namespace Catalog.Application.Tests;

/// <summary>REQ-APP-001, REQ-APP-004, REQ-API-003, and REQ-API-004: the use cases of categories, through the production chain over the in-memory catalog.</summary>
public sealed class CategoryUseCaseTests
{
    [Fact]
    [Trait("Requirement", "REQ-APP-001")]
    public async Task Creating_a_category_returns_its_view_and_records_its_creation()
    {
        var rig = new UseCaseRig();

        var result = await rig.Command<CreateCategory, CategoryView>().HandleAsync(new CreateCategory("Keyboards", Make.Key("key-1")), CancellationToken.None);

        var view = result.Value;
        (view.Id, view.Name, view.Status, view.CreatedAt).Should().Be((TestIds.Category(1000).Value, "Keyboards", LifecycleStatus.Active, UseCaseRig.Epoch));
        view.ETag.Should().Be(InMemoryCatalog.TagForTests(1).Value);
        rig.Catalog.PublishedEvents.OfType<CategoryCreated>().Should().ContainSingle();
    }

    [Fact]
    [Trait("Requirement", "REQ-API-004")]
    public async Task Creating_a_category_twice_with_the_same_key_makes_one_category()
    {
        var rig = new UseCaseRig();
        var handler = rig.Command<CreateCategory, CategoryView>();

        var first = await handler.HandleAsync(new CreateCategory("Keyboards", Make.Key("key-1")), CancellationToken.None);
        var replay = await handler.HandleAsync(new CreateCategory("Keyboards", Make.Key("key-1")), CancellationToken.None);

        replay.Value.Should().Be(first.Value);
        rig.Catalog.AllCategories.Should().ContainSingle();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Requirement", "REQ-APP-004")]
    public async Task A_name_that_is_empty_is_a_validation_error_on_the_name(bool withValidators)
    {
        var rig = new UseCaseRig(withValidators: withValidators);

        var result = await rig.Command<CreateCategory, CategoryView>().HandleAsync(new CreateCategory(" "), CancellationToken.None);

        result.Error.Should().BeOfType<ValidationFailed>().Which.Errors.Select(error => error.Field).Should().Equal("name");
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-001")]
    public async Task Renaming_a_category_needs_the_current_version_and_changes_the_name_and_the_version()
    {
        var rig = new UseCaseRig();
        var category = rig.SeedCategory();
        var handler = rig.Command<RenameCategory, CategoryView>();

        var missing = await handler.HandleAsync(new RenameCategory(category.Id.Value, "Boards", null), CancellationToken.None);
        var stale = await handler.HandleAsync(new RenameCategory(category.Id.Value, "Boards", InMemoryCatalog.TagForTests(9)), CancellationToken.None);
        var renamed = await handler.HandleAsync(new RenameCategory(category.Id.Value, "Boards", rig.TagOf(category)), CancellationToken.None);

        missing.Error.Should().Be(new PreconditionRequired());
        stale.Error.Should().Be(new PreconditionFailed());
        renamed.Value.Name.Should().Be("Boards");
        renamed.Value.ETag.Should().Be(rig.TagOf(category).Value).And.NotBe(InMemoryCatalog.TagForTests(1).Value);
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-004")]
    public async Task Renaming_a_category_that_does_not_exist_or_with_an_invalid_value_is_refused()
    {
        var rig = new UseCaseRig(withValidators: false);
        var category = rig.SeedCategory();
        var handler = rig.Command<RenameCategory, CategoryView>();
        var tag = InMemoryCatalog.TagForTests(1);

        var notFound = await handler.HandleAsync(new RenameCategory(TestIds.Category(88).Value, "Boards", tag), CancellationToken.None);
        var badId = await handler.HandleAsync(new RenameCategory(Make.NotUuid7, "Boards", tag), CancellationToken.None);
        var badName = await handler.HandleAsync(new RenameCategory(category.Id.Value, " ", tag), CancellationToken.None);

        notFound.Error.Should().Be(new NotFound("category"));
        badId.Error.Should().BeOfType<ValidationFailed>().Which.Errors[0].Field.Should().Be("categoryId");
        badName.Error.Should().BeOfType<ValidationFailed>().Which.Errors[0].Field.Should().Be("name");
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-001")]
    public async Task Discontinuing_a_category_needs_the_current_version_and_a_second_call_changes_nothing()
    {
        var rig = new UseCaseRig();
        var category = rig.SeedCategory();
        var handler = rig.Command<DiscontinueCategory, CategoryView>();

        var missing = await handler.HandleAsync(new DiscontinueCategory(category.Id.Value, null), CancellationToken.None);
        var stale = await handler.HandleAsync(new DiscontinueCategory(category.Id.Value, InMemoryCatalog.TagForTests(9)), CancellationToken.None);
        var first = await handler.HandleAsync(new DiscontinueCategory(category.Id.Value, rig.TagOf(category)), CancellationToken.None);
        var second = await handler.HandleAsync(new DiscontinueCategory(category.Id.Value, rig.TagOf(category)), CancellationToken.None);

        missing.Error.Should().Be(new PreconditionRequired());
        stale.Error.Should().Be(new PreconditionFailed());
        first.Value.Status.Should().Be(LifecycleStatus.Discontinued);
        second.Value.Should().Be(first.Value);
        rig.Catalog.PublishedEvents.OfType<CategoryDiscontinued>().Should().ContainSingle();
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-004")]
    public async Task Discontinuing_a_category_that_does_not_exist_or_has_a_bad_identifier_is_refused()
    {
        var rig = new UseCaseRig(withValidators: false);
        var handler = rig.Command<DiscontinueCategory, CategoryView>();
        var tag = InMemoryCatalog.TagForTests(1);

        var notFound = await handler.HandleAsync(new DiscontinueCategory(TestIds.Category(9).Value, tag), CancellationToken.None);
        var invalid = await handler.HandleAsync(new DiscontinueCategory(Make.NotUuid7, tag), CancellationToken.None);

        notFound.Error.Should().Be(new NotFound("category"));
        invalid.Error.Should().BeOfType<ValidationFailed>().Which.Errors[0].Field.Should().Be("categoryId");
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-001")]
    public async Task Reading_a_category_returns_its_view_or_not_found()
    {
        var rig = new UseCaseRig();
        var category = rig.SeedCategory();
        var handler = rig.Query<GetCategory, CategoryView>();

        var found = await handler.HandleAsync(new GetCategory(category.Id.Value), CancellationToken.None);
        var missing = await handler.HandleAsync(new GetCategory(TestIds.Category(404).Value), CancellationToken.None);
        var invalid = await handler.HandleAsync(new GetCategory(Make.NotUuid7), CancellationToken.None);

        found.Value.Name.Should().Be("Keyboards");
        missing.Error.Should().Be(new NotFound("category"));
        invalid.Error.Should().BeOfType<ValidationFailed>().Which.Errors[0].Field.Should().Be("categoryId");
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-001")]
    public async Task Listing_categories_pages_by_name_and_hands_out_a_cursor_for_the_next_page()
    {
        var rig = new UseCaseRig();
        foreach (var name in new[] { "Delta", "Alpha", "Charlie", "Bravo", "Echo" })
        {
            rig.SeedCategory(name);
        }

        var handler = rig.Query<ListCategories, Paging.Page<CategoryView>>();

        var first = (await handler.HandleAsync(new ListCategories(2), CancellationToken.None)).Value;
        var second = (await handler.HandleAsync(new ListCategories(2, first.NextCursor), CancellationToken.None)).Value;
        var last = (await handler.HandleAsync(new ListCategories(2, second.NextCursor), CancellationToken.None)).Value;

        first.Items.Select(view => view.Name).Should().Equal("Alpha", "Bravo");
        second.Items.Select(view => view.Name).Should().Equal("Charlie", "Delta");
        last.Items.Select(view => view.Name).Should().Equal("Echo");
        last.NextCursor.Should().BeNull();
        rig.Catalog.CategoryReads.Limit.Should().Be(3);
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-004")]
    public async Task A_page_size_or_a_cursor_that_is_not_valid_is_a_validation_error()
    {
        var rig = new UseCaseRig(withValidators: false);
        var handler = rig.Query<ListCategories, Paging.Page<CategoryView>>();

        var result = await handler.HandleAsync(new ListCategories(0, "not a cursor"), CancellationToken.None);

        result.Error.Should().BeOfType<ValidationFailed>().Which.Errors.Select(error => error.Field).Should().Equal("pageSize", "cursor");
    }
}
