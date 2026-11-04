// Conforming code. Every rule in ArchitectureRules must pass here and must match at least one type, which proves the rule
// is not vacuous. Each layer is a namespace, and the dependencies follow plan section 8.1.

namespace Catalog.Architecture.Tests.Fixtures.Clean.Domain
{
    // The members of an enum are constants, so the static-state rule must ignore them.
    public enum WidgetKind
    {
        Plain,
        Fancy,
    }

    public sealed class Widget
    {
        // A constant cannot change, so the static-state rule must ignore it.
        public const int Capacity = 8;

        private static readonly int MaxNameLength = 100;

        public static int NameLimit => MaxNameLength;

        // The lambda below makes the compiler emit a static cache field. The static-state rule must ignore it.
        public IReadOnlyList<int> Evens(IEnumerable<int> values) => [.. values.Where(value => value % 2 == 0)];
    }
}

namespace Catalog.Architecture.Tests.Fixtures.Clean.Application
{
    public interface IWidgetStore
    {
        Domain.Widget Find();
    }

    public interface ICommandHandler<in TCommand, TResult>
    {
        TResult Handle(TCommand command);
    }

    public interface IQueryHandler<in TQuery, TResult>
    {
        TResult Handle(TQuery query);
    }

    public interface IWidgetRepository
    {
        void Add(Domain.Widget widget);
    }

    public interface IWidgetReadStore
    {
        Domain.Widget Find();
    }

    // A command handler writes through the repository, and a query handler reads through the read store.
    internal sealed class CreateWidgetHandler(IWidgetRepository repository) : ICommandHandler<string, Domain.Widget>
    {
        public Domain.Widget Handle(string command)
        {
            var widget = new Domain.Widget();
            repository.Add(widget);

            return widget;
        }
    }

    internal sealed class GetWidgetHandler(IWidgetReadStore store) : IQueryHandler<string, Domain.Widget>
    {
        public Domain.Widget Handle(string query) => store.Find();
    }
}

namespace Catalog.Architecture.Tests.Fixtures.Clean.Contracts
{
    public sealed record WidgetDto(string Name);
}

namespace Catalog.Architecture.Tests.Fixtures.Clean.Infrastructure
{
    internal sealed class SqlWidgetStore : Application.IWidgetStore
    {
        public Domain.Widget Find() => new();
    }

    // Infrastructure owns the connection to Valkey, and it builds the backplane that the adapter of CrossCutting uses.
    internal sealed class ValkeyConnectionFactory
    {
        public StackExchange.Redis.ConnectionMultiplexer? Connection { get; init; }

        public ZiggyCreatures.Caching.Fusion.Backplane.IFusionCacheBackplane? Backplane { get; init; }
    }
}

namespace Catalog.Architecture.Tests.Fixtures.Clean.CrossCutting
{
    internal sealed class WidgetLoggingDecorator
    {
        public Application.IWidgetStore? Inner { get; init; }
    }

    // A pipeline names the decorators, and a decorator does not.
    internal static class WidgetPipeline
    {
        public static WidgetLoggingDecorator Compose() => new();
    }
}

namespace Catalog.Architecture.Tests.Fixtures.Clean.CrossCutting.Caching
{
    // The adapter of the cache is the one place of CrossCutting that names FusionCache.
    internal sealed class WidgetCacheAdapter
    {
        public ZiggyCreatures.Caching.Fusion.FusionCache? Cache { get; init; }

        public ZiggyCreatures.Caching.Fusion.Backplane.IFusionCacheBackplane? Backplane { get; init; }
    }
}

namespace Catalog.Architecture.Tests.Fixtures.Clean.Api
{
    // The composition root may use a service provider and may hold static state.
    internal sealed class WidgetEndpoint
    {
        public static int RequestCount;

        public static object? Resolve(IServiceProvider services) => services.GetService(typeof(Application.IWidgetStore));
    }
}

namespace Catalog.Architecture.Tests.Fixtures.Clean.Api.Composition
{
    // The composition namespace is the one place of the Api that names the adapters of Infrastructure and CrossCutting, because it registers them.
    internal static class WidgetRegistration
    {
        public static Application.IWidgetStore Store() => new Infrastructure.SqlWidgetStore();

        public static CrossCutting.WidgetLoggingDecorator Decorator() => CrossCutting.WidgetPipeline.Compose();
    }
}

namespace Catalog.Architecture.Tests.Fixtures.Clean.Api.Endpoints
{
    // An endpoint depends on the ports and the requests of the Application and on the contracts.
    internal sealed class WidgetEndpoint
    {
        public Application.IWidgetStore? Store { get; init; }
    }
}
