// Deliberately wrong code. Each type breaks exactly one rule in ArchitectureRules, and the tests assert that the rule reports it.
// Nothing here is shipped or called. The analyzer exceptions for this folder are in .editorconfig.

namespace Catalog.Architecture.Tests.Fixtures.Violations.Domain
{
    // Breaks "domain-depends-on-the-bcl-only": the domain reaches into infrastructure.
    public sealed class LeakyProduct
    {
        private readonly Infrastructure.SqlProductStore _store = new();
    }
}

namespace Catalog.Architecture.Tests.Fixtures.Violations.Application
{
    // Breaks "application-depends-on-domain-and-the-bcl-only".
    public sealed class LeakyCoordinator
    {
        private readonly Infrastructure.SqlProductStore _store = new();
    }

    // Breaks "no-service-locator-outside-the-composition-root".
    public sealed class ServiceLocatorUser(IServiceProvider services)
    {
        private readonly IServiceProvider _services = services;
    }

    // Breaks "application-ports-are-segregated": a port with six operations asks every caller to depend on all of them.
    public interface IEverythingStore
    {
        void Add();

        void Remove();

        void Find();

        void Count();

        void Archive();

        void Restore();
    }

    // Breaks "handlers-are-internal-and-sealed": the handler is public and not sealed.
    public class CreateProductHandler
    {
    }

    public interface ICommandHandler<in TCommand, TResult>
    {
        TResult Handle(TCommand command);
    }

    public interface IQueryHandler<in TQuery, TResult>
    {
        TResult Handle(TQuery query);
    }

    public interface IUnitOfWork
    {
        void Commit();
    }

    public interface IWidgetReadStore
    {
        void Find();
    }

    // Breaks "handlers-implement-a-handler-interface": a handler that no endpoint can inject through the interface of plan section 8.2.
    internal sealed class UntypedHandler
    {
    }

    // Breaks "query-handlers-depend-on-no-write-port": a query that needs the unit of work can write.
    internal sealed class GreedyQueryHandler(IUnitOfWork unitOfWork) : IQueryHandler<string, string>
    {
        public string Handle(string query)
        {
            unitOfWork.Commit();

            return query;
        }
    }

    // Breaks "command-handlers-depend-on-no-read-store": a command that reads through the query side skips the aggregate that holds the rules.
    internal sealed class NosyCommandHandler(IWidgetReadStore store) : ICommandHandler<string, string>
    {
        public string Handle(string command)
        {
            store.Find();

            return command;
        }
    }

    // Breaks "pipeline-code-uses-no-reflection-or-activator": it looks a method up by name at run time.
    public sealed class ReflectiveDispatcher
    {
        private readonly System.Reflection.MethodInfo? _method = typeof(ReflectiveDispatcher).GetMethod("Dispatch");

        public void Dispatch() => _ = _method;
    }

    // Breaks "no-static-mutable-fields-outside-the-composition-root".
    public sealed class GlobalCounter
    {
        public static int Count;
    }

    // Breaks "no-static-mutable-properties-outside-the-composition-root".
    public sealed class StaticSettings
    {
        public static string Name { get; set; } = string.Empty;
    }
}

namespace Catalog.Architecture.Tests.Fixtures.Violations.Contracts
{
    // Breaks "contracts-depend-on-the-bcl-only": a domain type appears in a contract.
    public sealed class LeakyDto
    {
        public Domain.LeakyProduct? Product { get; init; }
    }
}

namespace Catalog.Architecture.Tests.Fixtures.Violations.Infrastructure
{
    public sealed class SqlProductStore
    {
    }

    // Breaks "infrastructure-does-not-depend-on-cross-cutting-or-the-composition-root".
    public sealed class ApiAwareStore
    {
        private readonly Api.Startup _startup = new();
    }
}

namespace Catalog.Architecture.Tests.Fixtures.Violations.CrossCutting
{
    // Breaks "decorators-are-composed-only-by-pipelines": a decorator that names another decorator fixes the order of the chain outside the pipeline.
    public sealed class ChainingDecorator
    {
        private readonly DirectDatabaseDecorator _next = new();
    }

    // Breaks "cross-cutting-does-not-depend-on-infrastructure-or-the-composition-root".
    public sealed class DirectDatabaseDecorator
    {
        private readonly Infrastructure.SqlProductStore _store = new();
    }
}

namespace Catalog.Architecture.Tests.Fixtures.Violations.Api
{
    public sealed class Startup
    {
    }
}
