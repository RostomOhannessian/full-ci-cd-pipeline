using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;

namespace Catalog.Contract.Tests.Support;

/// <summary>The authored contract and the repository files that must agree with it.</summary>
internal static class Authored
{
    /// <summary>The path that ADR-0017 gives to the authored contract, relative to the repository root.</summary>
    public const string RelativePath = "contracts/openapi/catalog/v1/catalog.openapi.yaml";

    public static string Root { get; } = FindRoot();

    public static string ContractPath { get; } = Path.Combine(Root, "contracts", "openapi", "catalog", "v1", "catalog.openapi.yaml");

    public static OpenApiDocument Document { get; } = Load(ContractPath);

    /// <summary>Reads a repository file with LF line endings, so a test does not depend on the checkout.</summary>
    public static string ReadText(params string[] relativePath) =>
        File.ReadAllText(Path.Combine([Root, .. relativePath])).ReplaceLineEndings("\n");

    public static OpenApiDocument Load(string path)
    {
        var settings = new OpenApiReaderSettings();
        settings.AddYamlReader();

        var result = OpenApiDocument.Parse(File.ReadAllText(path), "yaml", settings);

        if (result.Diagnostic is { Errors.Count: > 0 } diagnostic)
        {
            throw new InvalidOperationException($"{path} has OpenAPI errors: {string.Join("; ", diagnostic.Errors.Select(error => $"{error.Pointer} {error.Message}"))}");
        }

        return result.Document ?? throw new InvalidOperationException($"{path} did not load as an OpenAPI document.");
    }

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
