using System.Globalization;
using System.Text.RegularExpressions;
using Governance.Auditor.Common;
using Governance.Auditor.Status;

namespace Governance.Auditor.Session;

/// <summary>What the caller says about the session: when it is, what its context is like, and what the next task is.</summary>
internal sealed record SessionRequest(string Moment, string Task, string Context);

internal sealed record NextWorkPackage(string Id, string Title, int? Issue, string Phase, string Branch, bool InProgress);

/// <summary>The Git state that the handoff depends on, read without any network call, so the counts are as of the last fetch.</summary>
internal sealed record GitFacts(string Branch, string? Upstream, int Ahead, int Behind, int Unpushed, IReadOnlyList<string> ChangedPaths);

/// <summary>One thing that stops a handoff from being complete.</summary>
internal sealed record HandoffGap(string Kind, string Message);

internal sealed record SessionAdvice(
    SessionFacts Facts,
    SessionRule Rule,
    string BoundaryReason,
    NextWorkPackage? Next,
    IReadOnlyList<HandoffGap> Gaps,
    IReadOnlyList<string> Notes,
    GitFacts Git,
    string? KickoffPrompt)
{
    public string Advice => Rule.Advice;

    /// <summary>The rule's reason, and then why the boundary is of the kind it is.</summary>
    public IReadOnlyList<string> Reasons => [Rule.Reason, BoundaryReason];

    public int ExitCode => SessionVocabulary.IsBlocking(Advice) ? 1 : 0;
}

/// <summary>
/// Gathers the facts of a session from <c>status.yaml</c> and the local Git state and applies the policy. It reads nothing else: no network,
/// no GitHub CLI, no session store. The caller says whether its context was compacted, because only the agent knows.
/// </summary>
internal static partial class SessionAdvisor
{
    private const int ExamplePaths = 3;

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NonAlphanumeric();

    public static async Task<SessionAdvice> AdviseAsync(
        RepositoryFiles files,
        IProcessRunner processes,
        SessionPolicy policy,
        SessionRequest request,
        CancellationToken cancellationToken)
    {
        var git = await ReadGitAsync(files, processes, cancellationToken);

        List<Finding> findings = [];
        var status = StatusFile.Load(files, findings);

        if (status is not null)
        {
            findings.AddRange(StatusValidator.Validate(status, files));
        }

        var gaps = GitGaps(git);
        var usable = status is not null && !findings.HasErrors();

        if (!usable)
        {
            gaps.Add(new HandoffGap("status-invalid", $"{StatusFile.DocumentPath} is not valid ({findings.Count(Severity.Error)} errors). Run 'governance status validate', and fix them."));
        }
        else if (PageGap(files, status!) is { } pageGap)
        {
            gaps.Add(pageGap);
        }

        List<string> notes = [];

        if (git.Behind > 0)
        {
            notes.Add($"The branch is {git.Behind} commits behind {git.Upstream} as of the last fetch. Run 'git fetch origin' and fast-forward before you rely on it.");
        }

        var next = usable ? ChooseNext(status!, notes) : null;
        var (boundary, boundaryReason) = usable ? Classify(status!, next) : (SessionVocabulary.Package, "status.yaml is not valid, so the boundary cannot be worked out.");

        var facts = new SessionFacts(
            request.Moment,
            gaps.Count == 0 ? SessionVocabulary.Complete : SessionVocabulary.Incomplete,
            request.Context,
            boundary,
            request.Task);

        var rule = policy.Evaluate(facts);
        var prompt = usable && string.Equals(rule.Advice, SessionVocabulary.Fresh, StringComparison.Ordinal)
            ? BuildPrompt(policy, status!, request.Task, next, git, notes)
            : null;

        return new SessionAdvice(
            facts,
            rule,
            boundaryReason,
            next is null ? null : Describe(status!, next),
            gaps,
            notes,
            git,
            prompt);
    }

    private static async Task<GitFacts> ReadGitAsync(RepositoryFiles files, IProcessRunner processes, CancellationToken cancellationToken)
    {
        var changed = await RequireAsync(files, processes, "Git could not report the state of the working tree.", ["status", "--porcelain=v1", "--untracked-files=all"], cancellationToken);
        var branch = await RequireAsync(files, processes, "Git could not name the current branch.", ["rev-parse", "--abbrev-ref", "HEAD"], cancellationToken);

        var upstreamResult = await processes.RunAsync(new ProcessRequest("git", ["rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}"], files.Root), cancellationToken);
        var upstream = upstreamResult.Succeeded ? upstreamResult.StandardOutput.Trim() : null;
        var ahead = 0;
        var behind = 0;
        int unpushed;

        if (upstream is { Length: > 0 })
        {
            var counts = await RequireAsync(files, processes, "Git could not compare the branch with its upstream.", ["rev-list", "--left-right", "--count", "HEAD...@{upstream}"], cancellationToken);
            var parts = counts.Split(['\t', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out ahead) || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out behind))
            {
                throw new GovernanceException("Git gave an answer that the tool cannot read when it compared the branch with its upstream.");
            }

            unpushed = ahead;
        }
        else
        {
            upstream = null;
            var count = await RequireAsync(files, processes, "Git could not count the commits that no remote has.", ["rev-list", "--count", "HEAD", "--not", "--remotes"], cancellationToken);

            if (!int.TryParse(count.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out unpushed))
            {
                throw new GovernanceException("Git gave an answer that the tool cannot read when it counted the commits that no remote has.");
            }
        }

        // A porcelain line is two status letters, a space, and the path.
        var paths = changed
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.Length > 0)
            .Select(line => line.Length > 3 ? line[3..] : line)
            .ToList();

        return new GitFacts(branch.Trim(), upstream, ahead, behind, unpushed, paths);
    }

    private static async Task<string> RequireAsync(RepositoryFiles files, IProcessRunner processes, string failure, string[] arguments, CancellationToken cancellationToken)
    {
        var result = await processes.RunAsync(new ProcessRequest("git", arguments, files.Root), cancellationToken);

        return result.Succeeded
            ? result.StandardOutput
            : throw new GovernanceException($"{failure} {result.StandardError.Trim()}".TrimEnd());
    }

    private static List<HandoffGap> GitGaps(GitFacts git)
    {
        List<HandoffGap> gaps = [];

        if (git.ChangedPaths.Count > 0)
        {
            var examples = string.Join(", ", git.ChangedPaths.Take(ExamplePaths));
            var subject = git.ChangedPaths.Count == 1 ? "1 path has" : $"{git.ChangedPaths.Count} paths have";
            gaps.Add(new HandoffGap("uncommitted-changes", $"{subject} uncommitted changes (for example {examples}). Commit them, or ask the maintainer what to do with them."));
        }

        if (git.Unpushed > 0)
        {
            var commits = git.Unpushed == 1 ? "1 commit is" : $"{git.Unpushed} commits are";

            gaps.Add(git.Upstream is null
                ? new HandoffGap("unpushed-commits", $"The branch '{git.Branch}' has no upstream, and {commits} on no remote. Ask the maintainer to approve the push.")
                : new HandoffGap("unpushed-commits", $"{commits} not pushed to {git.Upstream}. Ask the maintainer to approve the push."));
        }

        return gaps;
    }

    private static HandoffGap? PageGap(RepositoryFiles files, StatusDocument status)
    {
        if (!files.FileExists(StatusFile.PagePath))
        {
            return new HandoffGap("status-page-stale", $"{StatusFile.PagePath} does not exist. Run 'governance status render'.");
        }

        var current = files.ReadAllText(StatusFile.PagePath).ReplaceLineEndings("\n");

        return string.Equals(current, StatusRenderer.Render(status), StringComparison.Ordinal)
            ? null
            : new HandoffGap("status-page-stale", $"{StatusFile.PagePath} is not what status.yaml renders. Run 'governance status render'.");
    }

    /// <summary>
    /// The work package that starts next: the one that the maintainer chose in <c>resume.next-work-package</c> when it is ready, and otherwise
    /// the first one in phase order that is planned or in progress and whose dependencies are all completed.
    /// </summary>
    internal static WorkPackageInfo? ChooseNext(StatusDocument status, List<string> notes)
    {
        bool Ready(WorkPackageInfo workPackage) =>
            (workPackage.State is WorkState.Planned or WorkState.InProgress)
            && workPackage.DependsOn.All(dependency => status.FindWorkPackage(dependency)?.State == WorkState.Completed);

        if (status.Resume.NextWorkPackage is { } chosen)
        {
            var workPackage = status.FindWorkPackage(chosen);

            if (workPackage is not null && Ready(workPackage))
            {
                return workPackage;
            }

            var why = workPackage is null
                ? "is not in the status file"
                : workPackage.State is WorkState.Planned or WorkState.InProgress
                    ? $"waits for {string.Join(", ", workPackage.DependsOn.Where(dependency => status.FindWorkPackage(dependency)?.State != WorkState.Completed))}"
                    : $"is {StatusValidator.Describe(workPackage.State)}";

            notes.Add($"resume.next-work-package names {chosen}, which {why}, so the first ready work package is used.");
        }

        var first = status.WorkPackages.FirstOrDefault(Ready);

        if (first is null)
        {
            notes.Add("No work package is ready: each one is completed, blocked, or waits for a dependency.");
        }

        return first;
    }

    internal static (string Kind, string Reason) Classify(StatusDocument status, WorkPackageInfo? next)
    {
        var current = status.FindPhase(status.Current.Phase);

        if (current is { WorkPackages.Count: > 0 } && current.State != WorkState.Completed && current.WorkPackages.All(workPackage => workPackage.State == WorkState.Completed))
        {
            return (SessionVocabulary.Phase, $"Every work package of phase {current.Id} is completed, so the phase exit gate comes next.");
        }

        if (next is null)
        {
            return (SessionVocabulary.Package, "No work package is ready, so there is no next work package to classify.");
        }

        if (next.Id.EndsWith(".0", StringComparison.Ordinal))
        {
            return (SessionVocabulary.Phase, $"{next.Id} opens a phase, because its number ends in .0.");
        }

        var last = status.WorkPackages.LastOrDefault(workPackage => workPackage.State == WorkState.Completed);

        if (last is null)
        {
            return (SessionVocabulary.Phase, $"No work package is completed yet, so {next.Id} starts the project.");
        }

        var nextPhase = PhaseOf(status, next);
        var lastPhase = PhaseOf(status, last);

        return string.Equals(nextPhase, lastPhase, StringComparison.Ordinal)
            ? (SessionVocabulary.Package, $"{next.Id} is in the same phase as the last completed work package, {last.Id}.")
            : (SessionVocabulary.Phase, $"{next.Id} is in phase {nextPhase}, and the last completed work package, {last.Id}, is in phase {lastPhase}.");
    }

    private static string PhaseOf(StatusDocument status, WorkPackageInfo workPackage) =>
        status.Phases.First(phase => phase.WorkPackages.Any(candidate => string.Equals(candidate.Id, workPackage.Id, StringComparison.Ordinal))).Id.ToString();

    private static NextWorkPackage Describe(StatusDocument status, WorkPackageInfo workPackage) => new(
        workPackage.Id,
        workPackage.Title,
        workPackage.Issue,
        PhaseOf(status, workPackage),
        workPackage.Branch ?? SuggestBranch(workPackage),
        workPackage.State == WorkState.InProgress);

    /// <summary>The branch protocol of ADR-0003 gives <c>wp/&lt;phase&gt;.&lt;nn&gt;-&lt;slug&gt;</c>. The slug is a suggestion that the maintainer may shorten.</summary>
    private static string SuggestBranch(WorkPackageInfo workPackage)
    {
        var slug = NonAlphanumeric().Replace(workPackage.Title.ToLowerInvariant(), "-").Trim('-');
        return $"wp/{workPackage.Id[2..]}-{(slug.Length == 0 ? "work" : slug)}";
    }

    private static string? BuildPrompt(SessionPolicy policy, StatusDocument status, string task, WorkPackageInfo? next, GitFacts git, List<string> notes)
    {
        var template = policy.Prompts.For(task);
        var projectName = status.Project.Name;

        switch (task)
        {
            case SessionVocabulary.WorkPackage:
                if (next is null)
                {
                    notes.Add("There is no kickoff prompt, because no work package is ready. Ask the maintainer what comes next.");
                    return null;
                }

                var phase = status.Phases.First(candidate => candidate.WorkPackages.Any(workPackage => string.Equals(workPackage.Id, next.Id, StringComparison.Ordinal)));

                if (next.Issue is null)
                {
                    notes.Add($"{next.Id} has no Issue in status.yaml. The Definition of Ready needs one before the work starts, so create it first.");
                }

                return Fill(template, task, new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["project-name"] = projectName,
                    ["plan"] = status.Project.Plan,
                    ["work-package-id"] = next.Id,
                    ["work-package-title"] = next.Title,
                    ["issue"] = next.Issue?.ToString(CultureInfo.InvariantCulture) ?? "none",
                    ["phase"] = phase.Id.ToString(),
                    ["phase-plan"] = phase.Plan,
                    ["phase-branch"] = phase.Branch ?? "master",
                    ["branch"] = next.Branch ?? SuggestBranch(next),
                });

            case SessionVocabulary.PullRequest:
                var pullRequest = FindPullRequest(status, git);

                if (pullRequest is null)
                {
                    notes.Add("There is no kickoff prompt, because status.yaml lists no open pull request for this branch. Name the pull request to the maintainer.");
                    return null;
                }

                return Fill(template, task, new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["project-name"] = projectName,
                    ["pull-request"] = pullRequest.Number.ToString(CultureInfo.InvariantCulture),
                    ["pull-request-title"] = pullRequest.Title,
                    ["branch"] = pullRequest.Branch,
                });

            default:
                return Fill(template, task, new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["project-name"] = projectName,
                    ["phase-branch"] = status.FindPhase(status.Current.Phase)?.Branch ?? "master",
                });
        }
    }

    /// <summary>The open pull request of the branch that is checked out, or the one of the current work package, or the only one that is open.</summary>
    private static OpenPullRequest? FindPullRequest(StatusDocument status, GitFacts git)
    {
        var open = status.Resume.OpenPullRequests;
        var onBranch = open.FirstOrDefault(pull => string.Equals(pull.Branch, git.Branch, StringComparison.Ordinal));

        if (onBranch is not null)
        {
            return onBranch;
        }

        var currentNumber = status.Current.WorkPackage is { } id ? status.FindWorkPackage(id)?.PullRequest : null;
        var ofCurrent = open.FirstOrDefault(pull => pull.Number == currentNumber);

        return ofCurrent ?? (open.Count == 1 ? open[0] : null);
    }

    /// <summary>Fills each placeholder in one pass, so a value that contains braces is never expanded a second time.</summary>
    private static string Fill(string template, string task, Dictionary<string, string> values)
    {
        var filled = SessionPolicy.PlaceholderPattern().Replace(template, match =>
        {
            var name = match.Groups["name"].Value;

            return values.TryGetValue(name, out var value)
                ? value
                : throw new GovernanceException($"The '{task}' prompt uses the placeholder '{{{name}}}', and the tool has no value for it.");
        });

        return filled.ReplaceLineEndings("\n").TrimEnd('\n');
    }
}
