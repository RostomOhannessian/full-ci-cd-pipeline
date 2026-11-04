using System.Net;
using System.Threading.RateLimiting;
using Catalog.Api.Configuration;
using Catalog.Api.Http;
using Catalog.Api.Security;
using Catalog.Application.Caching;
using Catalog.Application.Categories;
using Catalog.Application.Events;
using Catalog.Application.Idempotency;
using Catalog.Application.Identity;
using Catalog.Application.Persistence;
using Catalog.Application.Products;
using Catalog.Application.Security;
using Catalog.Application.Telemetry;
using Catalog.CrossCutting.Caching;
using Catalog.CrossCutting.Validation;
using Catalog.Infrastructure.Caching;
using Catalog.Infrastructure.Credentials;
using Catalog.Infrastructure.Health;
using Catalog.Infrastructure.Idempotency;
using Catalog.Infrastructure.Outbox;
using Catalog.Infrastructure.Persistence;
using Catalog.Infrastructure.Persistence.Reading;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion.Backplane;

namespace Catalog.Api.Composition;

/// <summary>
/// The composition root of the catalog API (plan section 8.1, REQ-ARC-002, ADR-0040). This is the one place that registers dependencies, names the adapters of the outer layers, and reads the container. The extension
/// methods below mirror the reference composition of the persistence tests (<c>TestComposition</c> and <c>CacheHost</c>), one method for each concern, so a reader can follow what the host starts and in which scope.
/// </summary>
internal static class CatalogComposition
{
    private const string OutboxSection = "Catalog:Outbox";
    private const string IdempotencySection = "Catalog:Idempotency";

    /// <summary>Registers everything that the API needs and configures the listeners.</summary>
    /// <param name="builder">The builder of the host.</param>
    public static WebApplicationBuilder AddCatalogApi(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services
            .AddCatalogOptions()
            .AddCatalogPersistence()
            .AddCatalogCache()
            .AddCatalogApplication()
            .AddCatalogSecurity()
            .AddCatalogHealth();

        builder.WebHost.ConfigureKestrel(ConfigureKestrel);

        return builder;
    }

    private static IServiceCollection AddCatalogOptions(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<HostingOptions>, HostingOptionsValidator>();
        services.AddSingleton<IValidateOptions<AuthOptions>, AuthOptionsValidator>();
        services.AddSingleton<IValidateOptions<LimitsOptions>, LimitsOptionsValidator>();
        services.AddSingleton<IValidateOptions<ForwardedHeadersSettings>, ForwardedHeadersSettingsValidator>();
        services.AddSingleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidator>();
        services.AddSingleton<IValidateOptions<OutboxOptions>, OutboxOptionsValidator>();
        services.AddSingleton<IValidateOptions<IdempotencyOptions>, IdempotencyOptionsValidator>();
        services.AddSingleton<IValidateOptions<ValkeyOptions>, ValkeyOptionsValidator>();
        services.AddSingleton<IValidateOptions<CatalogCacheOptions>, CatalogCacheOptionsValidator>();

        services.AddOptions<HostingOptions>().BindConfiguration(HostingOptions.SectionName).ValidateOnStart();
        services.AddOptions<AuthOptions>().BindConfiguration(AuthOptions.SectionName).ValidateOnStart();
        services.AddOptions<LimitsOptions>().BindConfiguration(LimitsOptions.SectionName).ValidateOnStart();
        services.AddOptions<ForwardedHeadersSettings>().BindConfiguration(ForwardedHeadersSettings.SectionName).ValidateOnStart();
        services.AddOptions<DatabaseOptions>().BindConfiguration(DatabaseOptions.SectionName).ValidateOnStart();
        services.AddOptions<OutboxOptions>().BindConfiguration(OutboxSection).ValidateOnStart();
        services.AddOptions<IdempotencyOptions>().BindConfiguration(IdempotencySection).ValidateOnStart();
        services.AddOptions<ValkeyOptions>().BindConfiguration(ValkeyOptions.SectionName).ValidateOnStart();
        services.AddOptions<CatalogCacheOptions>().BindConfiguration(CatalogCacheOptions.SectionName).ValidateOnStart();

        return services;
    }

    // The persistence of the reference composition (docs/reference/persistence.md): the credential source and its interceptor, the write and read contexts, the repositories, the unit of work, the stores, and the
    // outbox. The dispatcher is scoped, because it takes the write context of its scope, and the processor, the maintenance job, and the signal are singletons.
    private static IServiceCollection AddCatalogPersistence(this IServiceCollection services)
    {
        services.AddLogging();
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<CredentialMetrics>();
        services.AddSingleton<ICredentialSource>(provider => CredentialSources.Create(Database(provider), provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<ILoggerFactory>()));
        services.AddSingleton(provider => new CredentialConnectionInterceptor(provider.GetRequiredService<ICredentialSource>(), Database(provider).ConnectionString));
        services.AddSingleton(provider => new OutboxInterceptor(provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton(provider => new CredentialPoolJanitor(
            provider.GetRequiredService<ICredentialSource>(),
            Database(provider).ConnectionString,
            Database(provider).PoolClearGrace,
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<CredentialMetrics>(),
            provider.GetRequiredService<ILogger<CredentialPoolJanitor>>()));
        services.AddHostedService(provider => provider.GetRequiredService<CredentialPoolJanitor>());

        services.AddDbContextFactory<CatalogDbContext>((provider, builder) => builder
            .UseCatalogSqlServer(Database(provider).ConnectionString)
            .AddInterceptors(provider.GetRequiredService<CredentialConnectionInterceptor>(), provider.GetRequiredService<OutboxInterceptor>()));
        services.AddDbContextFactory<CatalogReadContext>((provider, builder) => builder
            .UseCatalogSqlServer(Database(provider).ConnectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .AddInterceptors(provider.GetRequiredService<CredentialConnectionInterceptor>()));
        services.AddScoped(provider => provider.GetRequiredService<IDbContextFactory<CatalogDbContext>>().CreateDbContext());
        services.AddScoped(provider => provider.GetRequiredService<IDbContextFactory<CatalogReadContext>>().CreateDbContext());

        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IProductReadStore, ProductReadStore>();
        services.AddScoped<ICategoryReadStore, CategoryReadStore>();
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();

        services.AddSingleton<OutboxSignal>();
        services.AddScoped<IOutboxDispatcher, OutboxDispatcher>();
        services.AddSingleton<OutboxMetrics>();
        services.AddSingleton<OutboxProcessor>();
        services.AddSingleton<CatalogMaintenance>();
        services.AddHostedService<OutboxBackgroundService>();

        return services;
    }

    // The cache of the reference composition (docs/reference/caching.md). The shared cache and the backplane are singletons that the container disposes after the adapter, which is built from them. The adapter is one
    // instance for the node, and CacheInvalidationSubscriber over it is the singleton handler that both the outbox processor and the scoped dispatcher take (ADR-0039).
    private static IServiceCollection AddCatalogCache(this IServiceCollection services)
    {
        services.AddSingleton<CacheMetrics>();
        services.AddSingleton(provider => ValkeyConnection.CreateSharedCache(provider.GetRequiredService<IOptions<ValkeyOptions>>().Value));
        services.AddSingleton(provider => ValkeyConnection.CreateBackplane(provider.GetRequiredService<IOptions<ValkeyOptions>>().Value));
        services.AddSingleton(provider => CatalogCacheFactory.Create(
            provider.GetRequiredService<IOptions<CatalogCacheOptions>>().Value,
            provider.GetRequiredService<CacheMetrics>(),
            provider.GetRequiredService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>(),
            provider.GetRequiredService<IFusionCacheBackplane>(),
            provider.GetRequiredService<ILoggerFactory>()));
        services.AddSingleton<ICatalogCache>(provider => provider.GetRequiredService<FusionCatalogCache>());
        services.AddSingleton<IOutboxMessageHandler>(provider => new CacheInvalidationSubscriber(provider.GetRequiredService<ICatalogCache>()));

        return services;
    }

    // The application: the clock, the identifiers, the caller, the telemetry, the validators, and the handlers of each feature, which the registry composes with the pipelines (REQ-APP-006).
    private static IServiceCollection AddCatalogApplication(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.AddScoped<ICurrentPrincipal, HttpContextCurrentPrincipal>();
        services.AddSingleton<IUseCaseTelemetry, LoggingUseCaseTelemetry>();

        services.AddSingleton<IValidator<CreateCategory>, CreateCategoryValidator>();
        services.AddSingleton<IValidator<RenameCategory>, RenameCategoryValidator>();
        services.AddSingleton<IValidator<DiscontinueCategory>, DiscontinueCategoryValidator>();
        services.AddSingleton<IValidator<GetCategory>, GetCategoryValidator>();
        services.AddSingleton<IValidator<ListCategories>, ListCategoriesValidator>();
        services.AddSingleton<IValidator<CreateProduct>, CreateProductValidator>();
        services.AddSingleton<IValidator<UpdateProduct>, UpdateProductValidator>();
        services.AddSingleton<IValidator<DiscontinueProduct>, DiscontinueProductValidator>();
        services.AddSingleton<IValidator<GetProduct>, GetProductValidator>();
        services.AddSingleton<IValidator<ListProducts>, ListProductsValidator>();

        new ServiceCollectionFeatureRegistry(services).AddCategories().AddProducts();

        return services;
    }

    // Authentication, the endpoint policies, the rate limits, the forwarded headers, and the error handling of the API (plan sections 8.6, 8.7, and 9).
    private static IServiceCollection AddCatalogSecurity(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // The post-configuration of the bearer options must come after the one that the handler registers, so it sees the manager of the metadata that the handler builds.
        services.AddSingleton<ConfigureJwtBearer>();
        services.AddSingleton<IConfigureOptions<JwtBearerOptions>>(provider => provider.GetRequiredService<ConfigureJwtBearer>());
        services.AddSingleton<IPostConfigureOptions<JwtBearerOptions>>(provider => provider.GetRequiredService<ConfigureJwtBearer>());

        services.AddAuthorization(CatalogPolicies.AddTo);
        services.AddSingleton<IAuthorizationHandler, AccessRequirementHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, CatalogAuthorizationResultHandler>();

        services.AddSingleton<ApiErrorHandler>();

        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>().Configure<IOptions<LimitsOptions>>(ConfigureRateLimiter);

        services.AddOptions<ForwardedHeadersOptions>().Configure<IOptions<ForwardedHeadersSettings>>(ConfigureForwardedHeaders);

        return services;
    }

    private static IServiceCollection AddCatalogHealth(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<CredentialHealthCheck>("credentials", tags: [HealthTags.Startup, HealthTags.Ready])
            .AddCheck<DatabaseHealthCheck>("database", tags: [HealthTags.Ready])
            .AddCheck<OutboxHealthCheck>("outbox", tags: [HealthTags.Ready])
            .AddCheck<CacheHealthCheck>("cache", HealthStatus.Degraded, [HealthTags.Ready]);

        return services;
    }

    private static DatabaseOptions Database(IServiceProvider provider) => provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

    // Two sockets on one address: the public API, and the management port that only the health endpoints answer on (plan sections 8.9 and 8.12). The limits of the server are the ones of the settings.
    private static void ConfigureKestrel(WebHostBuilderContext context, Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions kestrel)
    {
        var hosting = kestrel.ApplicationServices.GetRequiredService<IOptions<HostingOptions>>().Value;
        var limits = kestrel.ApplicationServices.GetRequiredService<IOptions<LimitsOptions>>().Value;
        var address = IPAddress.Parse(hosting.ListenAddress);

        kestrel.AddServerHeader = false;
        kestrel.Limits.MaxRequestBodySize = limits.MaxRequestBodyBytes;
        kestrel.Limits.MaxRequestHeadersTotalSize = 16 * 1024;
        kestrel.Limits.MaxRequestLineSize = 8 * 1024;
        kestrel.Listen(address, hosting.PublicPort);
        kestrel.Listen(address, hosting.ManagementPort);
    }

    // One limiter for every request. A caller that is known is counted by its identifier, and any other by its address, so a flood of bad tokens is limited too. A read and a write have separate counts, and a write has
    // the lower limit. The health endpoints are not limited, because a probe must always get an answer.
    private static void ConfigureRateLimiter(RateLimiterOptions options, IOptions<LimitsOptions> settings)
    {
        var limits = settings.Value;

        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = (context, _) => RateLimitRejection.WriteAsync(context);
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            if (context.GetEndpoint()?.Metadata.GetMetadata<ManagementEndpointMetadata>() is not null)
            {
                return RateLimitPartition.GetNoLimiter("management");
            }

            var write = !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method);
            var caller = context.User.Identity?.IsAuthenticated == true ? $"caller:{TokenClaims.CallerId(context.User)}" : $"address:{context.Connection.RemoteIpAddress}";

            return RateLimitPartition.GetFixedWindowLimiter(
                $"{(write ? "write" : "read")}:{caller}",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = write ? limits.WritePermits : limits.ReadPermits, Window = limits.Window, QueueLimit = 0 });
        });
    }

    // The default of ASP.NET Core trusts the loopback address. Both default lists are cleared, so a proxy is trusted only when a setting names its network or its address.
    private static void ConfigureForwardedHeaders(ForwardedHeadersOptions options, IOptions<ForwardedHeadersSettings> settings)
    {
        var forwarded = settings.Value;

        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
        options.ForwardLimit = forwarded.ForwardLimit;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();

        foreach (var network in forwarded.KnownNetworkList)
        {
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        }

        foreach (var proxy in forwarded.KnownProxyList)
        {
            options.KnownProxies.Add(IPAddress.Parse(proxy));
        }
    }
}

/// <summary>The tags of the health checks, which the endpoints use to choose what they run.</summary>
internal static class HealthTags
{
    /// <summary>The checks of the startup probe.</summary>
    public const string Startup = "startup";

    /// <summary>The checks of the readiness probe.</summary>
    public const string Ready = "ready";
}
