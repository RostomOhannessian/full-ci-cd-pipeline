using AwesomeAssertions;
using Catalog.Application.Categories;
using Catalog.Application.Messaging;
using Catalog.Application.Products;
using Xunit;

namespace Catalog.Application.Tests;

/// <summary>REQ-APP-006: each feature registers its handlers through an explicit extension method, once and in a fixed order, so the wiring is the same on every start and a reviewer can read it.</summary>
public sealed class RegistrationTests
{
    private static RecordingRegistry Register() => (RecordingRegistry)new RecordingRegistry().AddCategories().AddProducts();

    [Fact]
    [Trait("Requirement", "REQ-APP-006")]
    public void The_features_register_every_handler_in_a_fixed_order_commands_before_queries()
    {
        var registry = Register();

        registry.Registrations.Should().Equal(
            "command CreateCategory -> CreateCategoryHandler",
            "command RenameCategory -> RenameCategoryHandler",
            "command DiscontinueCategory -> DiscontinueCategoryHandler",
            "query GetCategory -> GetCategoryHandler",
            "query ListCategories -> ListCategoriesHandler",
            "command CreateProduct -> CreateProductHandler",
            "command UpdateProduct -> UpdateProductHandler",
            "command DiscontinueProduct -> DiscontinueProductHandler",
            "query GetProduct -> GetProductHandler",
            "query ListProducts -> ListProductsHandler");
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-006")]
    public void Registering_twice_in_two_registries_gives_the_same_list()
    {
        Register().Registrations.Should().Equal(Register().Registrations);
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-006")]
    public void Every_request_of_the_application_is_registered_exactly_once()
    {
        var requests = typeof(CreateProduct).Assembly.GetExportedTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false } && typeof(IProtectedRequest).IsAssignableFrom(type))
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal);

        var registered = Register().Registrations.Select(line => line.Split(' ')[1]).Order(StringComparer.Ordinal);

        registered.Should().Equal(requests);
        registered.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-006")]
    public void A_feature_needs_a_registry()
    {
        var categories = () => CategoriesFeature.AddCategories(null!);
        var products = () => ProductsFeature.AddProducts(null!);

        categories.Should().Throw<ArgumentNullException>();
        products.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-006")]
    public void A_feature_returns_the_registry_so_that_features_chain()
    {
        var registry = new RecordingRegistry();

        registry.AddCategories().Should().BeSameAs(registry);
        registry.AddProducts().Should().BeSameAs(registry);
    }

    private sealed class RecordingRegistry : IFeatureRegistry
    {
        public List<string> Registrations { get; } = [];

        public void AddCommandHandler<TCommand, TResult, THandler>()
            where TCommand : IProtectedRequest
            where THandler : class, ICommandHandler<TCommand, TResult> =>
            Registrations.Add($"command {typeof(TCommand).Name} -> {typeof(THandler).Name}");

        public void AddQueryHandler<TQuery, TResult, THandler>()
            where TQuery : IProtectedRequest
            where THandler : class, IQueryHandler<TQuery, TResult> =>
            Registrations.Add($"query {typeof(TQuery).Name} -> {typeof(THandler).Name}");
    }
}
