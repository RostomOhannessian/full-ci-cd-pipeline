using Catalog.Application.Categories;
using Catalog.Application.Identity;
using Catalog.Application.Messaging;
using Catalog.Application.Persistence;
using Catalog.Application.Products;
using Catalog.CrossCutting.Decorators;
using Catalog.CrossCutting.Validation;
using Catalog.Domain.Categories;
using Catalog.Domain.Products;
using FluentValidation;
using Microsoft.Extensions.Time.Testing;

namespace Catalog.Application.Tests.Support;

/// <summary>
/// The whole application over fakes. It stands in for the composition root: it implements <see cref="IFeatureRegistry"/>, builds each handler from the services that its constructor names, and wraps it in the
/// production chain of decorators with the production validators. A test then asks for the handler of a request and runs it as the API will.
/// </summary>
internal sealed class UseCaseRig : IFeatureRegistry
{
    public static readonly DateTimeOffset Epoch = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly Dictionary<Type, object> _handlers = [];
    private readonly Dictionary<Type, object> _services = [];
    private readonly bool _withValidators;
    private readonly List<IValidator> _validators;

    public UseCaseRig(TestPrincipal? caller = null, bool withValidators = true)
    {
        _withValidators = withValidators;
        Caller = caller ?? Callers.Admin;
        Clock = new FakeTimeProvider(Epoch);
        Catalog = new InMemoryCatalog();
        UnitOfWork = new FakeUnitOfWork(Journal, Catalog);
        Store = new FakeIdempotencyStore(Journal);
        Outbox = new FakeOutbox(Journal);
        Cache = new FakeCache(Journal);
        Telemetry = new FakeTelemetry(Journal);
        Ids = new CountingIdGenerator(1000);

        Register<IProductRepository>(Catalog.Products);
        Register<ICategoryRepository>(Catalog.Categories);
        Register<IProductReadStore>(Catalog.ProductReads);
        Register<ICategoryReadStore>(Catalog.CategoryReads);
        Register<IIdGenerator>(Ids);
        Register<IUnitOfWork>(UnitOfWork);
        Register<TimeProvider>(Clock);

        _validators =
        [
            new CreateCategoryValidator(), new RenameCategoryValidator(), new DiscontinueCategoryValidator(), new GetCategoryValidator(), new ListCategoriesValidator(),
            new CreateProductValidator(), new UpdateProductValidator(), new DiscontinueProductValidator(), new GetProductValidator(), new ListProductsValidator(),
        ];

        this.AddCategories().AddProducts();
    }

    public Journal Journal { get; } = new();

    public TestPrincipal Caller { get; }

    public FakeTimeProvider Clock { get; }

    public InMemoryCatalog Catalog { get; }

    public FakeUnitOfWork UnitOfWork { get; }

    public FakeIdempotencyStore Store { get; }

    public FakeOutbox Outbox { get; }

    public FakeCache Cache { get; }

    public FakeTelemetry Telemetry { get; }

    public CountingIdGenerator Ids { get; }

    public ICommandHandler<TCommand, TResult> Command<TCommand, TResult>() => (ICommandHandler<TCommand, TResult>)_handlers[typeof(ICommandHandler<TCommand, TResult>)];

    public IQueryHandler<TQuery, TResult> Query<TQuery, TResult>() => (IQueryHandler<TQuery, TResult>)_handlers[typeof(IQueryHandler<TQuery, TResult>)];

    public void AddCommandHandler<TCommand, TResult, THandler>()
        where TCommand : IProtectedRequest
        where THandler : class, ICommandHandler<TCommand, TResult>
    {
        var handler = Build<THandler>();
        _handlers.Add(
            typeof(ICommandHandler<TCommand, TResult>),
            CommandPipeline.Compose<TCommand, TResult>(handler, Telemetry, ValidatorsFor<TCommand>(), new RecordingPrincipal(Journal, Caller), Store, UnitOfWork, Outbox));
    }

    public void AddQueryHandler<TQuery, TResult, THandler>()
        where TQuery : IProtectedRequest
        where THandler : class, IQueryHandler<TQuery, TResult>
    {
        var handler = Build<THandler>();
        _handlers.Add(typeof(IQueryHandler<TQuery, TResult>), QueryPipeline.Compose<TQuery, TResult>(handler, Telemetry, ValidatorsFor<TQuery>(), new RecordingPrincipal(Journal, Caller), Cache));
    }

    /// <summary>Puts a category in the catalog, as if an earlier request had made it.</summary>
    public Category SeedCategory(string name = "Keyboards", bool discontinued = false)
    {
        var category = Category.Create(Ids.NewCategoryId(), CategoryName.Create(name).Value, Clock);
        if (discontinued)
        {
            category.Discontinue(Clock);
        }

        Catalog.Seed(category);

        return category;
    }

    /// <summary>Puts a product in the catalog, as if an earlier request had made it.</summary>
    public Product SeedProduct(Category category, string sku = "KB-100", string name = "Keyboard", decimal amount = 89.99m, string currency = "USD", bool discontinued = false)
    {
        var money = Domain.Pricing.Money.Create(amount, Domain.Pricing.Currency.Create(currency).Value).Value;
        var product = Product.Create(Ids.NewProductId(), Sku.Create(sku).Value, ProductName.Create(name).Value, null, money, category, Clock).Value;
        if (discontinued)
        {
            product.Discontinue(Clock);
        }

        Catalog.Seed(product);

        return product;
    }

    public EntityTag TagOf(Product product) => Catalog.Products.TagOf(product);

    public EntityTag TagOf(Category category) => Catalog.Categories.TagOf(category);

    private void Register<T>(T service)
        where T : notnull => _services[typeof(T)] = service;

    private List<IValidator<TRequest>> ValidatorsFor<TRequest>() => _withValidators ? [.. _validators.OfType<IValidator<TRequest>>()] : [];

    // A constructor names the services that the handler needs, as the container of the composition root would read them.
    private THandler Build<THandler>()
        where THandler : class
    {
        var constructor = typeof(THandler).GetConstructors().Single();
        var arguments = constructor.GetParameters().Select(parameter => _services[parameter.ParameterType]).ToArray();

        return (THandler)constructor.Invoke(arguments);
    }
}
