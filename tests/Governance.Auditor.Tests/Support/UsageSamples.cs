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

    public const string DerivedPrices = "        prices:\n          fresh-input: 200\n          cache-read: 10\n          cache-write: 250\n          output: 1000\n";

    /// <summary>
    /// The valid ledger with the usage of its second work package read from the cloud store: derived, with the prices that it used, an
    /// unknown effort, and no reasoning tokens. At those prices the 3,000 fresh, 900,000 cached, 40,000 cache-write, and 20,000 output
    /// tokens are 39.6 AIU.
    /// </summary>
    public static string DerivedLedger => Edit(
        (
            "        reasoning-effort: xhigh\n        agent: main\n        calls: 30\n        aiu: 400.0\n        fresh-input-tokens: 3000\n        cache-read-tokens: 900000\n        cache-write-tokens: 40000\n        output-tokens: 20000\n        reasoning-tokens: 5000\n",
            "        source: derived\n        reasoning-effort: unknown\n        agent: main\n        calls: 30\n        aiu: 39.6\n" + DerivedPrices + "        fresh-input-tokens: 3000\n        cache-read-tokens: 900000\n        cache-write-tokens: 40000\n        output-tokens: 20000\n"));

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
