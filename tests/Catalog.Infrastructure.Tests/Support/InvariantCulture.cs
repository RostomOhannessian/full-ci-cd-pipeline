using System.Globalization;
using System.Runtime.CompilerServices;

namespace Catalog.Infrastructure.Tests.Support;

/// <summary>
/// Runs the tests under the invariant culture, so a result does not depend on the host (plan section 10.3). The other test projects get this from the invariant globalization mode, which SqlClient does not
/// support, so this project sets the culture in code before any test runs.
/// </summary>
internal static class InvariantCulture
{
    [ModuleInitializer]
    internal static void Apply()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
    }
}
