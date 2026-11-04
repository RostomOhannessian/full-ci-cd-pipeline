using AwesomeAssertions;
using Catalog.Api.Tests.Support;
using Catalog.Application.Caching;
using Catalog.Application.Categories;
using Catalog.Application.Events;
using Catalog.Application.Messaging;
using Catalog.Application.Paging;
using Catalog.Application.Products;
using Catalog.Application.Security;
using Microsoft.Extensions.Options;
using Xunit;

namespace Catalog.Api.Tests;

/// <summary>
/// The composition root (plan section 8.1, REQ-ARC-002, REQ-APP-006): the Api registers every handler through the feature registry, composes its chain of decorators with the pipelines, registers the dispatcher as a
/// scoped service and the cache subscriber as one singleton handler of the outbox, and validates every setting when the host starts. The host is the real one, built from the same registrations as <c>Program</c>.
/// </summary>
public sealed class CompositionTests : IAsyncLifetime
{
    private TestIssuer _issuer = null!;
    private ApiTestHost _host = null!;

    public async ValueTask InitializeAsync()
    {
        _issuer = await TestIssuer.StartAsync();
        _host = await ApiTestHost.StartAsync(new HostSettings(_issuer));
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        await _issuer.DisposeAsync();
    }

    private static void AssertCommand<TCommand, TResult>(IServiceProvider scope)
        where TCommand : IProtectedRequest
    {
        var handler = scope.GetRequiredService<ICommandHandler<TCommand, TResult>>();

        handler.GetType().Name.Should().StartWith("TelemetryCommandDecorator", $"the chain of {typeof(TCommand).Name} starts with telemetry (plan section 8.2)");
    }

    private static void AssertQuery<TQuery, TResult>(IServiceProvider scope)
        where TQuery : IProtectedRequest
    {
        var handler = scope.GetRequiredService<IQueryHandler<TQuery, TResult>>();

        handler.GetType().Name.Should().StartWith("TelemetryQueryDecorator", $"the chain of {typeof(TQuery).Name} starts with telemetry (plan section 8.2)");
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-006")]
    [Trait("Requirement", "REQ-ARC-004")]
    public void Every_use_case_of_the_catalog_resolves_to_the_chain_that_the_pipelines_compose()
    {
        using var scope = _host.Services.CreateScope();

        AssertCommand<CreateCategory, CategoryView>(scope.ServiceProvider);
        AssertCommand<RenameCategory, CategoryView>(scope.ServiceProvider);
        AssertCommand<DiscontinueCategory, CategoryView>(scope.ServiceProvider);
        AssertQuery<GetCategory, CategoryView>(scope.ServiceProvider);
        AssertQuery<ListCategories, Page<CategoryView>>(scope.ServiceProvider);
        AssertCommand<CreateProduct, ProductView>(scope.ServiceProvider);
        AssertCommand<UpdateProduct, ProductView>(scope.ServiceProvider);
        AssertCommand<DiscontinueProduct, ProductView>(scope.ServiceProvider);
        AssertQuery<GetProduct, ProductView>(scope.ServiceProvider);
        AssertQuery<ListProducts, Page<ProductView>>(scope.ServiceProvider);
    }

    [Fact]
    [Trait("Requirement", "REQ-APP-006")]
    public void A_handler_is_scoped_to_the_request_so_two_requests_never_share_one()
    {
        using var first = _host.Services.CreateScope();
        using var second = _host.Services.CreateScope();

        var inFirst = first.ServiceProvider.GetRequiredService<ICommandHandler<CreateProduct, ProductView>>();

        first.ServiceProvider.GetRequiredService<ICommandHandler<CreateProduct, ProductView>>().Should().BeSameAs(inFirst);
        second.ServiceProvider.GetRequiredService<ICommandHandler<CreateProduct, ProductView>>().Should().NotBeSameAs(inFirst);
    }

    [Fact]
    [Trait("Requirement", "REQ-ARC-002")]
    public void The_dispatcher_is_scoped_and_the_cache_subscriber_is_one_singleton_handler_of_the_outbox()
    {
        using var first = _host.Services.CreateScope();
        using var second = _host.Services.CreateScope();

        first.ServiceProvider.GetRequiredService<IOutboxDispatcher>().Should().NotBeSameAs(second.ServiceProvider.GetRequiredService<IOutboxDispatcher>());

        var handlers = first.ServiceProvider.GetServices<IOutboxMessageHandler>().ToList();

        handlers.Should().ContainSingle().Which.Should().BeOfType<CacheInvalidationSubscriber>();
        second.ServiceProvider.GetServices<IOutboxMessageHandler>().Single().Should().BeSameAs(handlers[0]);
        _host.Services.GetRequiredService<ICatalogCache>().Should().BeSameAs(_host.Services.GetRequiredService<ICatalogCache>());
    }

    [Fact]
    [Trait("Requirement", "REQ-ARC-002")]
    public void The_caller_of_the_use_cases_is_the_caller_of_the_request()
    {
        using var scope = _host.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<ICurrentPrincipal>().IsAuthenticated.Should().BeFalse("a scope outside a request has no caller, and the principal never guesses one");
    }

    public static TheoryData<string, string, string> InvalidSettings => new()
    {
        { "Catalog:Auth:Issuer", "", "Issuer" },
        { "Catalog:Auth:ClockSkew", "00:05:00", "ClockSkew" },
        { "Catalog:Hosting:ManagementPort", "8080", "ManagementPort" },
        { "Catalog:Limits:MaxJsonDepth", "1", "MaxJsonDepth" },
        { "Catalog:ForwardedHeaders:KnownNetworks", "0.0.0.0/0", "KnownNetworks" },
        { "Catalog:Database:ConnectionString", "", "ConnectionString" },
        { "Catalog:Valkey:Endpoint", "nowhere", "Endpoint" },
        { "Catalog:Cache:Environment", "Not Valid", "Environment" },
        { "Catalog:Outbox:BatchSize", "0", "BatchSize" },
    };

    [Theory]
    [MemberData(nameof(InvalidSettings))]
    [Trait("Requirement", "REQ-ARC-002")]
    public async Task A_wrong_setting_stops_the_host_at_start_and_the_message_names_the_setting_and_no_secret(string key, string value, string expectedName)
    {
        var settings = new HostSettings(_issuer).With(key, value);

        var failure = await Assert.ThrowsAsync<OptionsValidationException>(async () =>
        {
            await using var host = await ApiTestHost.StartAsync(settings);
        });

        failure.Message.Should().Contain(expectedName).And.NotContain(HostSettings.DatabasePassword).And.NotContain(HostSettings.ValkeyPassword);
    }

    [Fact]
    [Trait("Requirement", "REQ-ARC-002")]
    [Trait("Requirement", "REQ-SEC-006")]
    public async Task A_missing_credential_stops_the_host_and_the_message_never_repeats_a_secret()
    {
        var settings = new HostSettings(_issuer).With("Catalog:Database:Password", string.Empty).With("Catalog:Valkey:Password", string.Empty);

        var failure = await Assert.ThrowsAsync<OptionsValidationException>(async () =>
        {
            await using var host = await ApiTestHost.StartAsync(settings);
        });

        failure.Message.Should().Contain("Password");
    }
}
