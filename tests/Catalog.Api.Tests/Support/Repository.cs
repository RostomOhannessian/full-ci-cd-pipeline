namespace Catalog.Api.Tests.Support;

/// <summary>Finds the repository that holds the tests, so a test can read a file of the repository or write a file where a later step of CI reads it.</summary>
internal static class Repository
{
    public static string Root { get; } = FindRoot();

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ProductCatalog.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root, which holds ProductCatalog.slnx, was not found above the test binaries.");
    }
}
