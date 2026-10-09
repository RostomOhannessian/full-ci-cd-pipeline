using System.Globalization;
using System.Text.RegularExpressions;
using Governance.Auditor.Common;
using Governance.Auditor.Documents;

namespace Governance.Auditor.Status;

/// <summary>Where the status files live, and how to load them.</summary>
internal static class StatusFile
{
    public const string DocumentPath = "docs/project/status.yaml";
    public const string SchemaPath = "docs/project/status.schema.json";
    public const string PagePath = "docs/project/STATUS.md";

    /// <summary>Loads <c>status.yaml</c>, or returns null with the reasons in <paramref name="findings"/> when it is not valid.</summary>
    public static StatusDocument? Load(RepositoryFiles files, List<Finding> findings)
    {
        var node = SchemaDocument.Load(files, DocumentPath, SchemaPath, StatusValidator.SchemaRule, findings);
        return node is null ? null : DocumentJson.Deserialize<StatusDocument>(node, DocumentPath);
    }
}

/// <summary>
/// The rules the JSON Schema cannot express: references between work packages, the order they may start in, the consistency of the
/// current work, and whether the evidence it names exists. Each rule reports every violation, so one run lists everything to fix.
/// </summary>
internal static class StatusValidator
{
    public const string SchemaRule = "status-schema";
    public const string Rule = "status";

    private static readonly Regex ExternalLink = new(@"^https://", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex PhaseBranchPattern = new(@"^phase/(?<phase>\d+)-[a-z0-9][a-z0-9-]*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex WorkPackageBranchPattern = new(@"^wp/(?<id>\d+\.\d+)-[a-z0-9][a-z0-9-]*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <param name="status">The parsed status file.</param>
    /// <param name="files">The repository, used to check that evidence exists. Null skips that check.</param>
    public static IReadOnlyList<Finding> Validate(StatusDocument status, RepositoryFiles? files)
    {
        List<Finding> findings = [];

        CheckDate(status, findings);
        CheckIdentifiers(status, findings);
        CheckBranches(status, findings);
        CheckDependencies(status, findings);
        CheckWorkPackageStates(status, findings);
        CheckPhaseStates(status, findings);
        CheckCurrent(status, findings);

        if (files is not null)
        {
            CheckEvidence(status, files, findings);
        }

        return findings;
    }

    // ADR-0003: a phase branch is phase/<n>-<slug>, and a work-package branch is wp/<phase>.<nn>-<slug>. A docs-only phase may do its
    // work on the phase branch itself.
    private static void CheckBranches(StatusDocument status, List<Finding> findings)
    {
        foreach (var phase in status.Phases.Where(phase => phase.Branch is not null))
        {
            var match = PhaseBranchPattern.Match(phase.Branch!);

            if (!match.Success || !string.Equals(match.Groups["phase"].Value, phase.Id.Value, StringComparison.Ordinal))
            {
                findings.Add(Error($"Phase {phase.Id} names the branch '{phase.Branch}', which is not phase/{phase.Id}-<slug> (ADR-0003)."));
            }

            foreach (var workPackage in phase.WorkPackages.Where(workPackage => workPackage.Branch is not null && !string.Equals(workPackage.Branch, phase.Branch, StringComparison.Ordinal)))
            {
                var branch = WorkPackageBranchPattern.Match(workPackage.Branch!);

                if (!branch.Success || !string.Equals($"WP{branch.Groups["id"].Value}", workPackage.Id, StringComparison.Ordinal))
                {
                    findings.Add(Error($"{workPackage.Id} names the branch '{workPackage.Branch}', which is not wp/{workPackage.Id[2..]}-<slug> (ADR-0003)."));
                }
            }
        }
    }

    private static void CheckDate(StatusDocument status, List<Finding> findings)
    {
        if (!DateOnly.TryParseExact(status.Updated, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            findings.Add(Error($"'updated' is {status.Updated}, which is not a real calendar date."));
        }
    }

    private static void CheckIdentifiers(StatusDocument status, List<Finding> findings)
    {
        foreach (var duplicate in status.Phases.GroupBy(phase => phase.Id).Where(group => group.Count() > 1))
        {
            findings.Add(Error($"Phase {duplicate.Key} appears {duplicate.Count()} times."));
        }

        foreach (var duplicate in status.WorkPackages.GroupBy(workPackage => workPackage.Id, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            findings.Add(Error($"Work package {duplicate.Key} appears {duplicate.Count()} times."));
        }

        foreach (var phase in status.Phases)
        {
            foreach (var workPackage in phase.WorkPackages.Where(workPackage => !workPackage.Id.StartsWith($"WP{phase.Id}.", StringComparison.Ordinal)))
            {
                findings.Add(Error($"{workPackage.Id} is listed under phase {phase.Id}, but its ID says it belongs to another phase."));
            }
        }

        foreach (var duplicate in status.WorkPackages.Where(workPackage => workPackage.Issue is not null).GroupBy(workPackage => workPackage.Issue).Where(group => group.Count() > 1))
        {
            findings.Add(Error($"Issue {duplicate.Key} is linked to {string.Join(" and ", duplicate.Select(workPackage => workPackage.Id))}, and each work package needs its own Issue."));
        }
    }

    private static void CheckDependencies(StatusDocument status, List<Finding> findings)
    {
        var known = status.WorkPackages.Select(workPackage => workPackage.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var workPackage in status.WorkPackages)
        {
            foreach (var dependency in workPackage.DependsOn.Where(dependency => !known.Contains(dependency)))
            {
                findings.Add(Error($"{workPackage.Id} depends on {dependency}, which is not in the status file."));
            }

            if (workPackage.DependsOn.Contains(workPackage.Id, StringComparer.Ordinal))
            {
                findings.Add(Error($"{workPackage.Id} depends on itself."));
            }
        }

        var byId = status.WorkPackages.GroupBy(workPackage => workPackage.Id, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        HashSet<string> reported = new(StringComparer.Ordinal);

        foreach (var start in status.WorkPackages)
        {
            var cycle = FindCycle(start.Id, byId, [], []);

            if (cycle is not null && reported.Add(string.Join(">", cycle.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))))
            {
                findings.Add(Error($"The dependencies form a cycle: {string.Join(" -> ", cycle)}."));
            }
        }
    }

    private static List<string>? FindCycle(string id, Dictionary<string, WorkPackageInfo> byId, List<string> path, HashSet<string> visited)
    {
        if (path.Contains(id, StringComparer.Ordinal))
        {
            return [.. path.SkipWhile(item => !string.Equals(item, id, StringComparison.Ordinal)), id];
        }

        if (!visited.Add(id) || !byId.TryGetValue(id, out var workPackage))
        {
            return null;
        }

        path.Add(id);

        foreach (var dependency in workPackage.DependsOn)
        {
            if (FindCycle(dependency, byId, path, visited) is { } cycle)
            {
                return cycle;
            }
        }

        path.RemoveAt(path.Count - 1);
        return null;
    }

    private static void CheckWorkPackageStates(StatusDocument status, List<Finding> findings)
    {
        foreach (var workPackage in status.WorkPackages)
        {
            if (workPackage.State is WorkState.InProgress or WorkState.Completed)
            {
                foreach (var dependency in workPackage.DependsOn
                    .Select(status.FindWorkPackage)
                    .Where(dependency => dependency is not null && dependency.State != WorkState.Completed))
                {
                    findings.Add(Error($"{workPackage.Id} is {Describe(workPackage.State)}, but its dependency {dependency!.Id} is {Describe(dependency.State)}. A work package starts after its dependencies are complete."));
                }
            }

            switch (workPackage.State)
            {
                case WorkState.Completed when workPackage.Evidence.Count == 0:
                    findings.Add(Error($"{workPackage.Id} is completed but lists no evidence."));
                    break;
                case WorkState.InProgress when string.IsNullOrWhiteSpace(workPackage.Branch):
                    findings.Add(Error($"{workPackage.Id} is in progress but names no branch."));
                    break;
                case WorkState.Blocked when workPackage.Blockers.Count == 0:
                    findings.Add(Error($"{workPackage.Id} is blocked but lists no blocker."));
                    break;
            }

            if (workPackage.State == WorkState.Completed && workPackage.Blockers.Count > 0)
            {
                findings.Add(Error($"{workPackage.Id} is completed but still lists blockers."));
            }
        }
    }

    private static void CheckPhaseStates(StatusDocument status, List<Finding> findings)
    {
        foreach (var phase in status.Phases.Where(phase => phase.WorkPackages.Count > 0))
        {
            var completed = phase.WorkPackages.Count(workPackage => workPackage.State == WorkState.Completed);

            if (phase.State == WorkState.Completed && completed != phase.WorkPackages.Count)
            {
                findings.Add(Error($"Phase {phase.Id} is completed, but only {completed} of its {phase.WorkPackages.Count} work packages are."));
            }

            if (phase.State == WorkState.Planned && phase.WorkPackages.Any(workPackage => workPackage.State != WorkState.Planned))
            {
                findings.Add(Error($"Phase {phase.Id} is planned, but some of its work packages have started."));
            }

            if (phase.State == WorkState.InProgress && completed == phase.WorkPackages.Count)
            {
                findings.Add(Error($"Phase {phase.Id} is in progress, but all of its work packages are completed. Mark the phase completed when its phase pull request merges."));
            }
        }
    }

    private static void CheckCurrent(StatusDocument status, List<Finding> findings)
    {
        var phase = status.FindPhase(status.Current.Phase);

        if (phase is null)
        {
            findings.Add(Error($"The current phase {status.Current.Phase} is not in the status file."));
            return;
        }

        if (status.Current.WorkPackage is null)
        {
            if (phase.Branch is not null && !string.Equals(status.Current.Branch, phase.Branch, StringComparison.Ordinal))
            {
                findings.Add(Error($"No work package is active, so the current branch should be the phase branch '{phase.Branch}' and not '{status.Current.Branch}'."));
            }

            if (status.WorkPackages.Any(workPackage => workPackage.State == WorkState.InProgress))
            {
                findings.Add(Error("A work package is in progress, but 'current.work-package' is null."));
            }

            return;
        }

        var active = status.FindWorkPackage(status.Current.WorkPackage);

        if (active is null)
        {
            findings.Add(Error($"The current work package {status.Current.WorkPackage} is not in the status file."));
            return;
        }

        if (!phase.WorkPackages.Contains(active))
        {
            findings.Add(Error($"The current work package {active.Id} does not belong to the current phase {phase.Id}."));
        }

        if (active.State is not (WorkState.InProgress or WorkState.Blocked))
        {
            findings.Add(Error($"The current work package {active.Id} is {Describe(active.State)}, and the active work package must be in progress or blocked."));
        }

        if (!string.Equals(status.Current.Branch, active.Branch, StringComparison.Ordinal))
        {
            findings.Add(Error($"The current branch '{status.Current.Branch}' differs from the branch of {active.Id}, '{active.Branch}'."));
        }
    }

    private static void CheckEvidence(StatusDocument status, RepositoryFiles files, List<Finding> findings)
    {
        foreach (var workPackage in status.WorkPackages)
        {
            foreach (var duplicate in workPackage.Evidence.GroupBy(item => item, StringComparer.Ordinal).Where(group => group.Count() > 1))
            {
                findings.Add(Error($"{workPackage.Id} lists the evidence '{duplicate.Key}' more than once."));
            }

            foreach (var evidence in workPackage.Evidence.Distinct(StringComparer.Ordinal))
            {
                if (ExternalLink.IsMatch(evidence))
                {
                    continue;
                }

                if (Uri.TryCreate(evidence, UriKind.Absolute, out _) || Path.IsPathRooted(evidence) || evidence.Contains("..", StringComparison.Ordinal))
                {
                    findings.Add(Error($"{workPackage.Id} evidence '{evidence}' must be a repository-relative path or an https link."));
                    continue;
                }

                var isDirectory = evidence.EndsWith('/');
                var exists = isDirectory ? files.DirectoryExists(evidence) : files.FileExists(evidence);

                if (!exists)
                {
                    findings.Add(Error($"{workPackage.Id} evidence '{evidence}' does not exist. A directory entry ends with '/'."));
                }
            }
        }
    }

    public static string Describe(WorkState state) => state switch
    {
        WorkState.Planned => "planned",
        WorkState.InProgress => "in progress",
        WorkState.Blocked => "blocked",
        _ => "completed",
    };

    private static Finding Error(string message) => Finding.Error(Rule, message, StatusFile.DocumentPath);
}
