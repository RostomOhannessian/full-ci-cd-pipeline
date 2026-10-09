// Conforming code. Every rule in ArchitectureRules must pass here and must match at least one type, which proves the rule
// is not vacuous. Each layer is a namespace, and the dependencies follow plan section 8.1.

namespace Catalog.Architecture.Tests.Fixtures.Clean.Domain
{
    public sealed class Widget
    {
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

    internal sealed class CreateWidgetHandler
    {
        public Domain.Widget Handle() => new();
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
}

namespace Catalog.Architecture.Tests.Fixtures.Clean.CrossCutting
{
    internal sealed class WidgetLoggingDecorator
    {
        public Application.IWidgetStore? Inner { get; init; }
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
