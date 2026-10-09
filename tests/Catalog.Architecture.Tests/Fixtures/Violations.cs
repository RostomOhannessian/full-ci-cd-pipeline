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

    // Breaks "handlers-are-internal-and-sealed": the handler is public and not sealed.
    public class CreateProductHandler
    {
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
