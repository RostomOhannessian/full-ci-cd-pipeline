using Governance.Auditor.Common;
using Governance.Auditor.Session;
using Governance.Auditor.Status;

namespace Governance.Auditor.Tests.Support;

/// <summary>The pieces that the session tests share: a repository whose handoff is complete, and the answers that Git gives about it.</summary>
internal static class SessionSamples
{
    /// <summary>
    /// A repository with the real policy and the real schemas, a status file, and the page that the status file renders. Its handoff is
    /// complete until a test breaks it. A test that needs a broken page passes <paramref name="page"/>.
    /// </summary>
    public static TestRepository Repository(string? statusYaml = null, string? page = null, bool withPage = true)
    {
        var yaml = statusYaml ?? StatusSamples.Valid;

        var repository = new TestRepository()
            .CopyFromRepository(SessionPolicy.Path)
            .CopyFromRepository(SessionPolicy.SchemaPath)
            .CopyFromRepository(StatusFile.SchemaPath)
            .Add(StatusFile.DocumentPath, yaml)
            .Add("docs/evidence/first.md", "Evidence.");

        if (withPage)
        {
            repository.Add(StatusFile.PagePath, page ?? StatusRenderer.Render(StatusSamples.Parse(yaml)));
        }

        return repository;
    }

    /// <summary>A process runner that answers like Git does for a branch. A null upstream means that the branch has none.</summary>
    public static FakeProcessRunner Git(
        string branch = "wp/1.2-second",
        string? upstream = "origin/wp/1.2-second",
        int ahead = 0,
        int behind = 0,
        int unpushed = 0,
        string[]? changed = null)
    {
        static Func<ProcessRequest, bool> Is(params string[] arguments) =>
            request => string.Equals(request.FileName, "git", StringComparison.Ordinal) && request.Arguments.SequenceEqual(arguments);

        var porcelain = string.Concat((changed ?? []).Select(path => $" M {path}\n"));

        var runner = new FakeProcessRunner()
            .On(Is("status", "--porcelain=v1", "--untracked-files=all"), porcelain)
            .On(Is("rev-parse", "--abbrev-ref", "HEAD"), branch + "\n");

        return upstream is null
            ? runner
                .On(Is("rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}"), string.Empty, exitCode: 128, standardError: "fatal: no upstream configured for branch")
                .On(Is("rev-list", "--count", "HEAD", "--not", "--remotes"), $"{unpushed}\n")
            : runner
                .On(Is("rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}"), upstream + "\n")
                .On(Is("rev-list", "--left-right", "--count", "HEAD...@{upstream}"), $"{ahead}\t{behind}\n");
    }
}
