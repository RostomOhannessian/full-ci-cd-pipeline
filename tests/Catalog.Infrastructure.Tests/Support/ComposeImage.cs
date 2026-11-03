using System.Text.RegularExpressions;

namespace Catalog.Infrastructure.Tests.Support;

/// <summary>
/// Reads the image of SQL Server from <c>compose.yaml</c>, so the tests and the local stack run the same digest and a change of the pin in one place is a change in both (REQ-QUA-001, plan section 10.3).
/// The repository root is the folder above the test output that holds <c>ProductCatalog.slnx</c>.
/// </summary>
internal static partial class ComposeImage
{
    public static string SqlServer() => Find("mcr.microsoft.com/mssql/server");

    public static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ProductCatalog.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"Could not find ProductCatalog.slnx above {AppContext.BaseDirectory}. Run the tests from a checkout of the repository.");
    }

    public static bool IsPinnedByDigest(string image) => PinnedImage().IsMatch(image);

    private static string Find(string repository)
    {
        var compose = File.ReadAllText(Path.Combine(RepositoryRoot(), "compose.yaml"));
        var match = Regex.Match(compose, $@"image:\s*({Regex.Escape(repository)}:\S+@sha256:[0-9a-f]{{64}})", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        return match.Success
            ? match.Groups[1].Value
            : throw new InvalidOperationException($"compose.yaml has no image of {repository} that is pinned by tag and digest.");
    }

    [GeneratedRegex(@"@sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PinnedImage();
}
