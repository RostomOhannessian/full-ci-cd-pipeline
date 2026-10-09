using System.Text.Json;
using Governance.Auditor.Common;
using Governance.Auditor.Status;

namespace Governance.Auditor.GitHubSync;

/// <summary>One change that would bring GitHub in line with the repository.</summary>
/// <param name="Kind">The kind of object: label, milestone, or issue.</param>
/// <param name="Description">What the change does, for the diff.</param>
/// <param name="Apply">Makes the change.</param>
internal sealed record SyncAction(string Kind, string Description, Func<IGitHubApi, CancellationToken, Task> Apply);

internal sealed record SyncPlan(IReadOnlyList<SyncAction> Actions, IReadOnlyList<Finding> Findings);

internal sealed record DesiredLabel(string Name, string Color, string Description);

internal sealed record DesiredMilestone(string Title, string Description, string State);

/// <summary>Which kinds of object to sync, and which phases' work packages get an issue.</summary>
/// <param name="Labels">Sync labels.</param>
/// <param name="Milestones">Sync milestones.</param>
/// <param name="Issues">Sync the work-package issues.</param>
/// <param name="Phases">The phases whose work packages are synced. Null means every work package that already has an issue, plus every work package of an in-progress phase.</param>
internal sealed record SyncScope(bool Labels = true, bool Milestones = true, bool Issues = true, IReadOnlySet<string>? Phases = null);

/// <summary>
/// Compares what the repository says with what GitHub holds, and lists the changes. Labels come from <c>labels.json</c>, milestones from
/// <c>milestones.json</c> and the phase states in <c>status.yaml</c>, and issues from the work packages in <c>status.yaml</c>, each
/// found again by the <c>governance:id</c> marker in its body. It never deletes anything, and it never changes an issue body except to
/// add a missing marker. Planning only reads from GitHub.
/// </summary>
internal static class GitHubSyncPlanner
{
    public const string Rule = "github-sync";
    public const string LabelsPath = "governance/github/labels.json";
    public const string MilestonesPath = "governance/github/milestones.json";

    private static readonly string[] ManagedLabelPrefixes = ["phase:", "size:"];
    private static readonly string[] ManagedLabelNames = ["work-package", "blocked"];

    public static string Marker(string workPackageId) => $"<!-- governance:id={workPackageId} -->";

    public static IReadOnlyList<DesiredLabel> LoadLabels(RepositoryFiles files)
    {
        using var document = JsonDocument.Parse(files.ReadAllText(LabelsPath));

        return
        [
            .. document.RootElement.EnumerateArray().Select(entry => new DesiredLabel(
                entry.GetProperty("name").GetString()!,
                entry.GetProperty("color").GetString()!.TrimStart('#').ToLowerInvariant(),
                entry.TryGetProperty("description", out var description) ? description.GetString() ?? string.Empty : string.Empty)),
        ];
    }

    public static IReadOnlyList<DesiredMilestone> LoadMilestones(RepositoryFiles files, StatusDocument status)
    {
        using var document = JsonDocument.Parse(files.ReadAllText(MilestonesPath));

        return
        [
            .. document.RootElement.EnumerateArray().Select(entry =>
            {
                var title = entry.GetProperty("title").GetString()!;
                var phase = status.Phases.FirstOrDefault(candidate => string.Equals(candidate.Milestone, title, StringComparison.Ordinal));

                // A phase that is completed closes its milestone, and every other phase keeps it open.
                var state = phase is null
                    ? entry.GetProperty("state").GetString()!
                    : phase.State == WorkState.Completed ? "closed" : "open";

                return new DesiredMilestone(title, entry.TryGetProperty("description", out var description) ? description.GetString() ?? string.Empty : string.Empty, state);
            }),
        ];
    }

    public static async Task<SyncPlan> PlanAsync(
        IGitHubApi api,
        StatusDocument status,
        IReadOnlyList<DesiredLabel> labels,
        IReadOnlyList<DesiredMilestone> milestones,
        SyncScope scope,
        CancellationToken cancellationToken)
    {
        List<SyncAction> actions = [];
        List<Finding> findings = [];

        if (scope.Labels)
        {
            PlanLabels(await api.GetLabelsAsync(cancellationToken), labels, actions);
        }

        if (scope.Milestones)
        {
            PlanMilestones(await api.GetMilestonesAsync(cancellationToken), milestones, actions);
        }

        if (scope.Issues)
        {
            PlanIssues(await api.GetIssuesAsync(cancellationToken), status, scope, actions, findings);
        }

        return new SyncPlan(actions, findings);
    }

    private static void PlanLabels(IReadOnlyList<GitHubLabel> actual, IReadOnlyList<DesiredLabel> desired, List<SyncAction> actions)
    {
        foreach (var label in desired)
        {
            var current = actual.FirstOrDefault(item => string.Equals(item.Name, label.Name, StringComparison.OrdinalIgnoreCase));

            if (current is null)
            {
                actions.Add(new SyncAction("label", $"create the label '{label.Name}'", (api, token) => api.CreateLabelAsync(new GitHubLabel(label.Name, label.Color, label.Description), token)));
                continue;
            }

            var colorDiffers = !string.Equals(current.Color, label.Color, StringComparison.OrdinalIgnoreCase);
            var descriptionDiffers = !string.Equals(current.Description ?? string.Empty, label.Description, StringComparison.Ordinal);

            if (colorDiffers || descriptionDiffers)
            {
                var changes = string.Join(" and ", new[] { colorDiffers ? "color" : null, descriptionDiffers ? "description" : null }.Where(change => change is not null));
                actions.Add(new SyncAction(
                    "label",
                    $"update the {changes} of the label '{current.Name}'",
                    (api, token) => api.UpdateLabelAsync(current.Name, colorDiffers ? label.Color : null, descriptionDiffers ? label.Description : null, token)));
            }
        }
    }

    private static void PlanMilestones(IReadOnlyList<GitHubMilestone> actual, IReadOnlyList<DesiredMilestone> desired, List<SyncAction> actions)
    {
        foreach (var milestone in desired)
        {
            var current = actual.FirstOrDefault(item => string.Equals(item.Title, milestone.Title, StringComparison.Ordinal));

            if (current is null)
            {
                actions.Add(new SyncAction("milestone", $"create the milestone '{milestone.Title}' ({milestone.State})", (api, token) => api.CreateMilestoneAsync(milestone.Title, milestone.Description, milestone.State, token)));
                continue;
            }

            var stateDiffers = !string.Equals(current.State, milestone.State, StringComparison.Ordinal);
            var descriptionDiffers = !string.Equals(current.Description ?? string.Empty, milestone.Description, StringComparison.Ordinal);

            if (stateDiffers || descriptionDiffers)
            {
                var changes = string.Join(" and ", new[] { stateDiffers ? $"{current.State} to {milestone.State}" : null, descriptionDiffers ? "the description" : null }.Where(change => change is not null));
                actions.Add(new SyncAction(
                    "milestone",
                    $"change {changes} on the milestone '{milestone.Title}'",
                    (api, token) => api.UpdateMilestoneAsync(current.Number, descriptionDiffers ? milestone.Description : null, stateDiffers ? milestone.State : null, token)));
            }
        }
    }

    private static void PlanIssues(IReadOnlyList<GitHubIssue> issues, StatusDocument status, SyncScope scope, List<SyncAction> actions, List<Finding> findings)
    {
        foreach (var phase in status.Phases)
        {
            foreach (var workPackage in phase.WorkPackages.Where(workPackage => InScope(scope, phase, workPackage)))
            {
                PlanIssue(issues, status, phase, workPackage, actions, findings);
            }
        }
    }

    private static bool InScope(SyncScope scope, PhaseInfo phase, WorkPackageInfo workPackage) =>
        scope.Phases is { } phases
            ? phases.Contains(phase.Id.Value)
            : workPackage.Issue is not null || phase.State == WorkState.InProgress;

    private static void PlanIssue(IReadOnlyList<GitHubIssue> issues, StatusDocument status, PhaseInfo phase, WorkPackageInfo workPackage, List<SyncAction> actions, List<Finding> findings)
    {
        var marker = Marker(workPackage.Id);
        var byMarker = issues.Where(issue => issue.Body.Contains(marker, StringComparison.Ordinal)).ToList();
        var byNumber = workPackage.Issue is { } number ? issues.FirstOrDefault(issue => issue.Number == number) : null;

        if (byMarker.Count > 1)
        {
            findings.Add(Finding.Error(Rule, $"{workPackage.Id}: more than one issue carries the marker ({string.Join(", ", byMarker.Select(issue => $"#{issue.Number}"))}). Remove the marker from the extra ones."));
            return;
        }

        var issue = byMarker.FirstOrDefault() ?? byNumber;

        if (byMarker.Count == 1 && byNumber is not null && byMarker[0].Number != byNumber.Number)
        {
            findings.Add(Finding.Error(Rule, $"{workPackage.Id}: status.yaml names issue #{byNumber.Number}, but issue #{byMarker[0].Number} carries the marker. Fix status.yaml or the marker."));
            return;
        }

        if (issue is null)
        {
            if (workPackage.Issue is { } missing)
            {
                findings.Add(Finding.Error(Rule, $"{workPackage.Id}: status.yaml names issue #{missing}, which does not exist."));
                return;
            }

            var draft = new IssueDraft(DesiredTitle(workPackage), DesiredBody(status, phase, workPackage), DesiredLabels([], phase, workPackage), phase.Milestone);
            actions.Add(new SyncAction("issue", $"create an issue for {workPackage.Id}", async (api, token) => await api.CreateIssueAsync(draft, token)));
            return;
        }

        if (byMarker.Count == 0 && ForeignMarker(issue.Body, workPackage.Id) is { } foreign)
        {
            findings.Add(Finding.Error(Rule, $"{workPackage.Id}: issue #{issue.Number} carries the marker of {foreign}. Fix status.yaml or the marker."));
            return;
        }

        if (byMarker.Count == 1 && workPackage.Issue is null)
        {
            findings.Add(Finding.Warning(Rule, $"{workPackage.Id}: issue #{issue.Number} carries its marker, but status.yaml records no issue number. Set 'issue: {issue.Number}' in status.yaml.", StatusFile.DocumentPath));
        }

        var changes = new List<string>();
        string? title = null;
        string? body = null;
        string? state = null;
        string? stateReason = null;
        IReadOnlyList<string>? desiredLabels = null;
        string? milestone = null;

        if (!issue.Body.Contains(marker, StringComparison.Ordinal))
        {
            body = $"{marker}\n{issue.Body}";
            changes.Add("add the marker to the body");
        }

        if (!string.Equals(issue.Title, DesiredTitle(workPackage), StringComparison.Ordinal))
        {
            title = DesiredTitle(workPackage);
            changes.Add("set the title");
        }

        var labels = DesiredLabels(issue.Labels, phase, workPackage);

        if (!labels.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(issue.Labels))
        {
            desiredLabels = labels;
            changes.Add($"set the labels to {string.Join(", ", labels.Select(label => $"'{label}'"))}");
        }

        if (phase.Milestone is not null && !string.Equals(issue.MilestoneTitle, phase.Milestone, StringComparison.Ordinal))
        {
            milestone = phase.Milestone;
            changes.Add($"set the milestone to '{phase.Milestone}'");
        }

        var desiredState = workPackage.State == WorkState.Completed ? "closed" : "open";

        if (!string.Equals(issue.State, desiredState, StringComparison.Ordinal))
        {
            state = desiredState;
            stateReason = desiredState == "closed" ? "completed" : "reopened";
            changes.Add(desiredState == "closed" ? $"close it, because {workPackage.Id} is completed" : $"reopen it, because {workPackage.Id} is {StatusValidator.Describe(workPackage.State)}");
        }

        if (changes.Count > 0)
        {
            var update = new IssueUpdate(title, body, state, stateReason, desiredLabels, milestone);
            actions.Add(new SyncAction("issue", $"issue #{issue.Number} ({workPackage.Id}): {string.Join("; ", changes)}", (api, token) => api.UpdateIssueAsync(issue.Number, update, token)));
        }
    }

    private static string DesiredTitle(WorkPackageInfo workPackage) => $"{workPackage.Id}: {workPackage.Title}";

    // Labels that this tool manages are set from status.yaml. Any other label on the issue is kept.
    private static IReadOnlyList<string> DesiredLabels(IReadOnlyList<string> current, PhaseInfo phase, WorkPackageInfo workPackage)
    {
        List<string> labels = [.. current.Where(label => !IsManaged(label)), "work-package", $"phase:{phase.Id}", $"size:{workPackage.Size}"];

        if (workPackage.State == WorkState.Blocked)
        {
            labels.Add("blocked");
        }

        return [.. labels.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal)];
    }

    private static bool IsManaged(string label) =>
        ManagedLabelNames.Contains(label, StringComparer.OrdinalIgnoreCase) || ManagedLabelPrefixes.Any(prefix => label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static string? ForeignMarker(string body, string ownId)
    {
        const string start = "<!-- governance:id=";
        var index = body.IndexOf(start, StringComparison.Ordinal);

        if (index < 0)
        {
            return null;
        }

        var end = body.IndexOf(" -->", index, StringComparison.Ordinal);
        var id = end < 0 ? string.Empty : body[(index + start.Length)..end];
        return string.Equals(id, ownId, StringComparison.Ordinal) ? null : id;
    }

    private static string DesiredBody(StatusDocument status, PhaseInfo phase, WorkPackageInfo workPackage) =>
        $"""
        {Marker(workPackage.Id)}
        ### Work package ID

        {workPackage.Id}

        ### Phase

        Phase {phase.Id} ({phase.Name})

        ### Scope and non-goals

        See the {workPackage.Id} row in [the phase plan](https://github.com/{status.Project.Repository}/blob/master/{phase.Plan}).

        ### Size

        {workPackage.Size}

        ### Depends on

        {(workPackage.DependsOn.Count == 0 ? "None" : string.Join(", ", workPackage.DependsOn))}
        """.ReplaceLineEndings("\n");
}
