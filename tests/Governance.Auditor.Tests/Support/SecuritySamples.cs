using Governance.Auditor.Common;
using Governance.Auditor.Security;
using Xunit;

namespace Governance.Auditor.Tests.Support;

/// <summary>Helpers for the security rule tests. They run the rules with the real policy file, so a test proves what the repository enforces.</summary>
internal static class SecuritySamples
{
    public const string WorkflowPath = ".github/workflows/test.yml";

    /// <summary>A workflow that every workflow rule accepts.</summary>
    // The source file may have CRLF line endings on Windows, so the text is normalized and every test sees the same lines.
    public static readonly string CleanWorkflow = """
        name: sample
        on:
          pull_request:
            branches: [master]
        permissions: {}
        jobs:
          build:
            name: Build
            runs-on: ubuntu-24.04
            timeout-minutes: 10
            permissions:
              contents: read
            steps:
              - name: Check out
                uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1
                with:
                  persist-credentials: false
              - name: Say hello
                run: echo hello
        """.ReplaceLineEndings("\n");

    public const string Sha = "3d3c42e5aac5ba805825da76410c181273ba90b1";

    public static TestRepository RepositoryWithPolicy()
    {
        return new TestRepository().CopyFromRepository(SecurityPolicy.Path);
    }

    public static TestRepository RepositoryWithWorkflow(string workflow)
    {
        return RepositoryWithPolicy().Add(WorkflowPath, workflow);
    }

    /// <summary>Replaces text in the clean workflow and fails when the text is not there, so a typo cannot make a test vacuous.</summary>
    public static string Mutate(string find, string replace)
    {
        if (!CleanWorkflow.Contains(find, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"The clean workflow does not contain '{find}'.");
        }

        return CleanWorkflow.Replace(find, replace, StringComparison.Ordinal);
    }

    public static async Task<IReadOnlyList<Finding>> RunAsync(TestRepository repository, string rule, GitRange? range = null, IProcessRunner? processes = null)
    {
        var policy = SecurityPolicy.Load(repository.Files);
        var context = new SecurityContext(repository.Files, policy, processes ?? new FakeProcessRunner(), range);

        return await SecurityAuditor.RunAsync(context, [rule], TestContext.Current.CancellationToken);
    }

    /// <summary>Runs one workflow rule on one workflow.</summary>
    public static async Task<IReadOnlyList<Finding>> RunWorkflowAsync(string workflow, string rule)
    {
        using var repository = RepositoryWithWorkflow(workflow);
        return await RunAsync(repository, rule);
    }
}
