using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Governance.Auditor.Common;
using Governance.Auditor.Documents;

namespace Documentation.Auditor.Discovery;

/// <summary>A tool that a repository file uses, as the scanner read it.</summary>
/// <param name="Kind">The kind of reference: <c>nuget</c>, <c>dotnet-tool</c>, <c>action</c>, or <c>image</c>.</param>
/// <param name="Identifier">What names the tool: a package ID, a tool command package, an action as <c>owner/repo</c>, or an image without its tag.</param>
/// <param name="Version">The version the file pins, when it pins one.</param>
/// <param name="Path">The repository-relative path of the file that uses the tool.</param>
/// <param name="Line">The one-based line, when the format has lines the scanner can name.</param>
internal sealed record DiscoveredTool(string Kind, string Identifier, string? Version, string Path, int? Line)
{
    public string Location => Line is null ? Path : $"{Path}:{Line}";
}

internal static class DiscoveryKinds
{
    public const string Nuget = "nuget";
    public const string DotnetTool = "dotnet-tool";
    public const string Action = "action";
    public const string Image = "image";
    public const string File = "file";
}

/// <summary>
/// Reads the repository files that name the tools the project uses. Each scanner handles one format and reports a file it cannot read, because
/// a scanner that skips a broken file silently would let a tool go undiscovered. A file is hostile input: XML with a DTD and YAML with anchors
/// are refused, as in the governance tool.
/// </summary>
internal static partial class Scanners
{
    [GeneratedRegex(@"^\s*(?:-\s+)?uses:\s*(?<quote>[""']?)(?<reference>[^\s""'#]+)\k<quote>\s*(?:#\s*(?<comment>.*))?$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex UsesPattern();

    [GeneratedRegex(@"^\s*FROM\s+(?:--\S+\s+)*(?<image>\S+)(?:\s+AS\s+(?<alias>\S+))?\s*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FromPattern();

    [GeneratedRegex(@"^\s*image:\s*(?<quote>[""']?)(?<image>[^\s""'#]+)\k<quote>\s*(?:#.*)?$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ImageLinePattern();

    [GeneratedRegex(@"v?\d+(?:\.\d+){1,3}", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"^(?:compose|docker-compose)(?:\..+)?\.ya?ml$|\.compose\.ya?ml$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ComposeFileNamePattern();

    [GeneratedRegex(@"^(?:Dockerfile(?:\..+)?|.+\.Dockerfile)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DockerfileNamePattern();

    public static IReadOnlyList<DiscoveredTool> ScanAll(RepositoryFiles files, IReadOnlyList<string> paths, List<Finding> findings)
    {
        List<DiscoveredTool> tools = [];

        foreach (var path in paths)
        {
            var name = System.IO.Path.GetFileName(path);

            try
            {
                if (name.EndsWith(".csproj", StringComparison.Ordinal) || name.EndsWith(".props", StringComparison.Ordinal) || name.EndsWith(".targets", StringComparison.Ordinal))
                {
                    tools.AddRange(ScanProjectFile(path, files.ReadAllText(path)));
                }
                else if (string.Equals(name, "dotnet-tools.json", StringComparison.Ordinal))
                {
                    tools.AddRange(ScanToolManifest(path, files.ReadAllText(path)));
                }
                else if (path.StartsWith(".github/workflows/", StringComparison.Ordinal) && (name.EndsWith(".yml", StringComparison.Ordinal) || name.EndsWith(".yaml", StringComparison.Ordinal)))
                {
                    tools.AddRange(ScanWorkflow(path, files.ReadAllText(path)));
                }
                else if (ComposeFileNamePattern().IsMatch(name))
                {
                    tools.AddRange(ScanCompose(path, files.ReadAllText(path)));
                }
                else if (DockerfileNamePattern().IsMatch(name))
                {
                    tools.AddRange(ScanDockerfile(path, files.ReadAllText(path)));
                }
            }
            catch (Exception exception) when (exception is GovernanceException or XmlException or System.Text.Json.JsonException)
            {
                findings.Add(Finding.Error("discovery-unreadable", $"The file cannot be read, so the tools in it cannot be discovered. {exception.Message}", path));
            }
        }

        return tools;
    }

    /// <summary>Finds the numbers in a version, so <c>v2.81.0</c>, <c>2.81.0</c>, and <c>lychee-v0.24.2</c> compare by the number alone.</summary>
    public static string? NormalizeVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var match = VersionPattern().Match(version);
        return match.Success ? match.Value.TrimStart('v') : null;
    }

    public static IEnumerable<DiscoveredTool> ScanProjectFile(string path, string text)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var reader = XmlReader.Create(new StringReader(text), settings);
        var document = XDocument.Load(reader, LoadOptions.SetLineInfo);

        foreach (var element in document.Descendants().Where(element => element.Name.LocalName is "PackageVersion" or "PackageReference" or "GlobalPackageReference" or "PackageDownload"))
        {
            var id = (string?)element.Attribute("Include") ?? (string?)element.Attribute("Update");

            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var version = (string?)element.Attribute("Version") ?? (string?)element.Attribute("VersionOverride");
            yield return new DiscoveredTool(DiscoveryKinds.Nuget, id, version, path, ((IXmlLineInfo)element).LineNumber);
        }
    }

    public static IEnumerable<DiscoveredTool> ScanToolManifest(string path, string text)
    {
        var node = JsonNode.Parse(text) as JsonObject ?? throw new GovernanceException($"{path}: the tool manifest is not a JSON object.");

        if (node["tools"] is not JsonObject tools)
        {
            yield break;
        }

        foreach (var (id, entry) in tools)
        {
            yield return new DiscoveredTool(DiscoveryKinds.DotnetTool, id, entry?["version"]?.GetValue<string>(), path, null);
        }
    }

    public static IEnumerable<DiscoveredTool> ScanWorkflow(string path, string text)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            if (UsesPattern().Match(lines[index]) is not { Success: true } match)
            {
                continue;
            }

            var reference = match.Groups["reference"].Value;

            // A local action or reusable workflow lives in this repository.
            if (reference.StartsWith("./", StringComparison.Ordinal))
            {
                continue;
            }

            if (reference.StartsWith("docker://", StringComparison.Ordinal))
            {
                yield return ReadImage(reference["docker://".Length..], path, index + 1);
                continue;
            }

            var at = reference.IndexOf('@', StringComparison.Ordinal);
            var action = at < 0 ? reference : reference[..at];
            var pinned = at < 0 ? null : reference[(at + 1)..];
            var comment = match.Groups["comment"].Value;
            var version = VersionPattern().Match(comment) is { Success: true } fromComment ? fromComment.Value : (pinned is not null && VersionPattern().IsMatch(pinned) ? pinned : null);

            yield return new DiscoveredTool(DiscoveryKinds.Action, action, version, path, index + 1);
        }

        var root = YamlDocument.Parse(text, path) as JsonObject;

        if (root?["jobs"] is not JsonObject jobs)
        {
            yield break;
        }

        foreach (var (_, jobNode) in jobs)
        {
            if (jobNode is not JsonObject job)
            {
                continue;
            }

            var container = job["container"] switch
            {
                JsonValue value when value.TryGetValue<string>(out var image) => image,
                JsonObject block => block["image"]?.GetValue<string>(),
                _ => null,
            };

            if (container is not null)
            {
                yield return ReadImage(container, path, null);
            }

            if (job["services"] is JsonObject services)
            {
                foreach (var (_, serviceNode) in services)
                {
                    if (serviceNode?["image"]?.GetValue<string>() is { } image)
                    {
                        yield return ReadImage(image, path, null);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Compose files are read by line, not as YAML, because a Compose file may use anchors and merge keys to share a service definition, and the
    /// YAML reader refuses both on purpose. An <c>image:</c> line is all the scanner needs.
    /// </summary>
    public static IEnumerable<DiscoveredTool> ScanCompose(string path, string text)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            if (ImageLinePattern().Match(lines[index]) is { Success: true } match)
            {
                yield return ReadImage(match.Groups["image"].Value, path, index + 1);
            }
        }
    }

    public static IEnumerable<DiscoveredTool> ScanDockerfile(string path, string text)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        HashSet<string> stages = new(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < lines.Length; index++)
        {
            if (FromPattern().Match(lines[index]) is not { Success: true } match)
            {
                continue;
            }

            var image = match.Groups["image"].Value;

            // 'scratch' is an empty image, and a name from an earlier stage is not an image at all.
            if (!string.Equals(image, "scratch", StringComparison.OrdinalIgnoreCase) && !stages.Contains(image))
            {
                yield return ReadImage(image, path, index + 1);
            }

            if (match.Groups["alias"].Success)
            {
                stages.Add(match.Groups["alias"].Value);
            }
        }
    }

    /// <summary>Splits <c>registry/name:tag@sha256:digest</c> into the name and the tag. A reference with a variable is kept as written, so it fails to match an entry.</summary>
    public static DiscoveredTool ReadImage(string reference, string path, int? line)
    {
        // A variable cannot be read until it is expanded, so the reference stays as written and fails to match an entry. That asks the
        // author to pin the image where the auditor can see it.
        if (reference.Contains('$', StringComparison.Ordinal))
        {
            return new DiscoveredTool(DiscoveryKinds.Image, reference, null, path, line);
        }

        var withoutDigest = reference.Split('@', 2)[0];
        var lastSlash = withoutDigest.LastIndexOf('/');
        var colon = withoutDigest.LastIndexOf(':');

        // A colon before the last slash belongs to a registry port, as in localhost:5000/name, and is not a tag.
        if (colon > lastSlash)
        {
            return new DiscoveredTool(DiscoveryKinds.Image, withoutDigest[..colon], withoutDigest[(colon + 1)..], path, line);
        }

        return new DiscoveredTool(DiscoveryKinds.Image, withoutDigest, null, path, line);
    }
}
