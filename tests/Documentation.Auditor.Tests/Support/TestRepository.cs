using System.Text;
using Governance.Auditor.Common;

namespace Documentation.Auditor.Tests.Support;

/// <summary>A throwaway repository in a temporary directory. Tests build exactly the files they need, so a failure points at one cause.</summary>
internal sealed class TestRepository : IDisposable
{
    private static readonly UTF8Encoding Utf8WithoutByteOrderMark = new(encoderShouldEmitUTF8Identifier: false);

    public TestRepository()
    {
        Root = Directory.CreateTempSubdirectory("documentation-tests-").FullName;
    }

    public string Root { get; }

    public RepositoryFiles Files => new(Root);

    /// <summary>Writes a file, replacing it when it exists, so a test changes one file of a conforming repository.</summary>
    public TestRepository Add(string relativePath, string content)
    {
        var full = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content.ReplaceLineEndings("\n"), Utf8WithoutByteOrderMark);
        return this;
    }

    /// <summary>Copies a file from the real repository, so a test checks the real contract and not a copy of it.</summary>
    public TestRepository CopyFromRepository(string relativePath) => Add(relativePath, File.ReadAllText(Path.Combine(RealRepository.Root, relativePath)));

    public TestRepository Remove(string relativePath)
    {
        File.Delete(Path.Combine(Root, relativePath));
        return this;
    }

    public string Read(string relativePath) => File.ReadAllText(Path.Combine(Root, relativePath)).ReplaceLineEndings("\n");

    /// <summary>Replaces text in a file of the repository. The text must be there, so a test cannot pass because an edit did nothing.</summary>
    public TestRepository Replace(string relativePath, string oldText, string newText)
    {
        var content = Read(relativePath);

        if (!content.Contains(oldText, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{relativePath} does not contain '{oldText}', so the test would change nothing.");
        }

        return Add(relativePath, content.Replace(oldText, newText, StringComparison.Ordinal));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A temporary directory that cannot be removed is not a test failure.
        }
    }
}

/// <summary>The real repository that holds these tests, found by walking up to the solution file.</summary>
internal static class RealRepository
{
    public static string Root { get; } = Find();

    public static RepositoryFiles Files => new(Root);

    private static string Find()
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
}
