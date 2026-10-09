using System.Text;

namespace Governance.Auditor.Common;

/// <summary>
/// Reads and writes files under the repository root. Every path is relative with forward slashes and must stay inside the root, so a
/// path that comes from a document can never read or overwrite a file elsewhere.
/// </summary>
internal sealed class RepositoryFiles
{
    // Directories that hold build output or tool state, never repository content.
    private static readonly string[] SkippedDirectories = [".git", "bin", "obj", "node_modules", "TestResults", ".vs", ".idea"];

    private static readonly UTF8Encoding Utf8WithoutByteOrderMark = new(encoderShouldEmitUTF8Identifier: false);

    public RepositoryFiles(string root)
    {
        Root = Path.GetFullPath(root);
    }

    public string Root { get; }

    /// <summary>Finds the repository root by walking up from a directory to the first one that has a <c>.git</c> entry.</summary>
    public static RepositoryFiles Locate(string startDirectory)
    {
        for (var directory = new DirectoryInfo(Path.GetFullPath(startDirectory)); directory is not null; directory = directory.Parent)
        {
            var git = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(git) || File.Exists(git))
            {
                return new RepositoryFiles(directory.FullName);
            }
        }

        throw new GovernanceException($"No Git repository was found at or above '{startDirectory}'. Run the command inside the repository or pass --root.");
    }

    public string Resolve(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new GovernanceException($"The path '{relativePath}' must be relative to the repository root.");
        }

        var full = Path.GetFullPath(Path.Combine(Root, relativePath));
        var prefix = Root.EndsWith(Path.DirectorySeparatorChar) ? Root : Root + Path.DirectorySeparatorChar;

        if (!full.StartsWith(prefix, StringComparison.Ordinal) && !string.Equals(full, Root, StringComparison.Ordinal))
        {
            throw new GovernanceException($"The path '{relativePath}' leaves the repository root.");
        }

        return full;
    }

    public bool FileExists(string relativePath) => File.Exists(Resolve(relativePath));

    public bool DirectoryExists(string relativePath) => Directory.Exists(Resolve(relativePath));

    public string ReadAllText(string relativePath)
    {
        var full = Resolve(relativePath);

        if (!File.Exists(full))
        {
            throw new GovernanceException($"The file '{relativePath}' does not exist.");
        }

        return File.ReadAllText(full);
    }

    /// <summary>Writes text with LF line endings and no byte order mark, as <c>.gitattributes</c> requires.</summary>
    public void WriteAllText(string relativePath, string content)
    {
        var full = Resolve(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content.ReplaceLineEndings("\n"), Utf8WithoutByteOrderMark);
    }

    /// <summary>Every file under a directory, relative to the root and sorted, without build output.</summary>
    public IReadOnlyList<string> ListFiles(string relativeDirectory = "")
    {
        var start = relativeDirectory.Length == 0 ? Root : Resolve(relativeDirectory);

        if (!Directory.Exists(start))
        {
            return [];
        }

        List<string> files = [];
        Walk(new DirectoryInfo(start), files);
        files.Sort(StringComparer.Ordinal);
        return files;
    }

    private void Walk(DirectoryInfo directory, List<string> files)
    {
        foreach (var file in directory.EnumerateFiles())
        {
            files.Add(Path.GetRelativePath(Root, file.FullName).Replace('\\', '/'));
        }

        foreach (var child in directory.EnumerateDirectories())
        {
            if (!SkippedDirectories.Contains(child.Name, StringComparer.Ordinal) && !child.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                Walk(child, files);
            }
        }
    }
}
