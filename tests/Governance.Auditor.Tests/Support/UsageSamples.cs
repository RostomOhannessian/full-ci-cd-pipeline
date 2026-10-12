using Governance.Auditor.Usage;

namespace Governance.Auditor.Tests.Support;

/// <summary>
/// The pieces that the usage tests share: a repository with the real schemas, a small status file, and a valid ledger. A violating test
/// changes one fact of the valid ledger by text, and a change that does not find its text is an error, so a typo cannot make a test pass
/// without testing anything.
/// </summary>
internal static class UsageSamples
{
    public static string Status => Golden.Read("usage/status.yaml");

    public static string Ledger => Golden.Read("usage/ai-usage.yaml");

    /// <summary>The valid ledger with each pair applied: the text to find, and the text to put in its place.</summary>
    public static string Edit(params (string Find, string Replace)[] edits) => Edit(Ledger, edits);

    public static string Edit(string text, params (string Find, string Replace)[] edits)
    {
        foreach (var (find, replace) in edits)
        {
            var index = text.IndexOf(find, StringComparison.Ordinal);

            if (index < 0)
            {
                throw new InvalidOperationException($"The sample has no text '{find}'.");
            }

            text = string.Concat(text.AsSpan(0, index), replace, text.AsSpan(index + find.Length));
        }

        return text;
    }

    /// <summary>A repository with the schemas of the status file and the ledger, and the status file and the ledger of the sample.</summary>
    public static TestRepository Repository(string? ledger = null, string? status = null, string? page = null)
    {
        var repository = new TestRepository()
            .CopyFromRepository("docs/project/status.schema.json")
            .CopyFromRepository(UsageFile.SchemaPath)
            .Add("docs/project/status.yaml", status ?? Status)
            .Add(UsageFile.DocumentPath, ledger ?? Ledger);

        if (page is not null)
        {
            repository.Add(UsageFile.PagePath, page);
        }

        return repository;
    }
}
