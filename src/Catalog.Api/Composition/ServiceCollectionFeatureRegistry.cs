using Catalog.Application.Caching;
using Catalog.Application.Events;
using Catalog.Application.Idempotency;
using Catalog.Application.Messaging;
using Catalog.Application.Persistence;
using Catalog.Application.Security;
using Catalog.Application.Telemetry;
using Catalog.CrossCutting.Decorators;
using FluentValidation;

namespace Catalog.Api.Composition;

/// <summary>
/// Implements <see cref="IFeatureRegistry"/> over the container, so each feature names its handlers once and the chain of decorators of each is composed by <see cref="CommandPipeline"/> and
/// <see cref="QueryPipeline"/>, the only places that fix the order (plan section 8.2, REQ-APP-006, ADR-0032). A handler and its validators are scoped to the request, and the chain is built from the same scope, so the
/// handler, the unit of work, the dispatcher, and the caller are the ones of the request. The registry holds no state beyond the collection that it fills, so the wiring is the same on every start.
/// </summary>
/// <param name="services">The collection of services of the host.</param>
internal sealed class ServiceCollectionFeatureRegistry(IServiceCollection services) : IFeatureRegistry
{
    private readonly IServiceCollection _services = services ?? throw new ArgumentNullException(nameof(services));

    /// <inheritdoc />
    public void AddCommandHandler<TCommand, TResult, THandler>()
        where TCommand : IProtectedRequest
        where THandler : class, ICommandHandler<TCommand, TResult>
    {
        _services.AddScoped<THandler>();
        _services.AddScoped(provider => CommandPipeline.Compose<TCommand, TResult>(
            provider.GetRequiredService<THandler>(),
            provider.GetRequiredService<IUseCaseTelemetry>(),
            provider.GetServices<IValidator<TCommand>>(),
            provider.GetRequiredService<ICurrentPrincipal>(),
            provider.GetRequiredService<IIdempotencyStore>(),
            provider.GetRequiredService<IUnitOfWork>(),
            provider.GetRequiredService<IOutboxDispatcher>()));
    }

    /// <inheritdoc />
    public void AddQueryHandler<TQuery, TResult, THandler>()
        where TQuery : IProtectedRequest
        where THandler : class, IQueryHandler<TQuery, TResult>
    {
        _services.AddScoped<THandler>();
        _services.AddScoped(provider => QueryPipeline.Compose<TQuery, TResult>(
            provider.GetRequiredService<THandler>(),
            provider.GetRequiredService<IUseCaseTelemetry>(),
            provider.GetServices<IValidator<TQuery>>(),
            provider.GetRequiredService<ICurrentPrincipal>(),
            provider.GetRequiredService<ICatalogCache>()));
    }
}
