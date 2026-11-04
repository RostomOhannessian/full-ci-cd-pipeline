using Microsoft.Extensions.FileProviders;

namespace Catalog.Api.Tests.Support;

/// <summary>A host environment with a name that a test chooses.</summary>
/// <param name="environmentName">The name of the environment.</param>
internal sealed class FakeEnvironment(string environmentName) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = environmentName;

    public string ApplicationName { get; set; } = "Catalog.Api.Tests";

    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
