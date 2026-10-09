using System.Globalization;
using Governance.Auditor.Common;

namespace Governance.Auditor.Status;

/// <summary>
/// Renders <c>docs/project/STATUS.md</c> from <c>status.yaml</c> and nothing else, so the same input always gives the same page. The
/// fixed wording lives in the embedded <c>StatusPage.template</c>, and this class fills in the tables.
/// </summary>
internal static class StatusRenderer
{
    private const string TemplateName = "Governance.Auditor.Status.StatusPage.template";
    private const string PageDirectory = "docs/project";

    public static string Render(StatusDocument status)
    {
        var page = LoadTemplate()
            .Replace("{{updated}}", status.Updated, StringComparison.Ordinal)
            .Replace("{{resume-table}}", ResumeTable(status), StringComparison.Ordinal)
            .Replace("{{environment-notes}}", EnvironmentNotes(status), StringComparison.Ordinal)
            .Replace("{{phases-table}}", PhasesTable(status), StringComparison.Ordinal)
            .Replace("{{work-package-sections}}", WorkPackageSections(status), StringComparison.Ordinal);

        return page.TrimEnd('\n') + "\n";
    }

    private static string LoadTemplate()
    {
        using var stream = typeof(StatusRenderer).Assembly.GetManifestResourceStream(TemplateName)
            ?? throw new GovernanceException($"The embedded template '{TemplateName}' is missing from the tool.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().ReplaceLineEndings("\n");
    }

    private static string ResumeTable(StatusDocument status)
    {
        var phase = status.FindPhase(status.Current.Phase);
        var active = status.Current.WorkPackage is null ? null : status.FindWorkPackage(status.Current.WorkPackage);

        var activePhase = phase is null
            ? status.Current.Phase.ToString()
            : $"{PhaseLink(phase)}, {StateLabel(phase.State).ToLowerInvariant()}";

        var activeWorkPackage = active is null
            ? "None"
            : $"{active.Id}: {Cell(active.Title)}{IssueSuffix(status, active)}, {StateLabel(active.State).ToLowerInvariant()}";

        var pullRequests = status.Resume.OpenPullRequests.Count == 0
            ? "None"
            : string.Join("<br>", status.Resume.OpenPullRequests.Select(pull =>
                $"[Pull request {pull.Number}]({PullRequestUrl(status, pull.Number)}): {Cell(pull.Title)} (`{pull.Branch}`)"));

        List<IReadOnlyList<string>> rows =
        [
            ["Active phase", activePhase],
            ["Active work package", activeWorkPackage],
            ["Branch", $"`{status.Current.Branch}`"],
            ["Last completed", Cell(status.Resume.LastCompleted)],
            ["Next action", Cell(status.Resume.NextAction)],
            ["Open pull requests", pullRequests],
        ];

        return MarkdownText.Table(["Item", "Value"], rows).TrimEnd('\n');
    }

    private static string EnvironmentNotes(StatusDocument status) =>
        status.Resume.EnvironmentNotes.Count == 0
            ? "None."
            : string.Join("\n", status.Resume.EnvironmentNotes.Select(note => $"- {Cell(note)}"));

    private static string PhasesTable(StatusDocument status)
    {
        var rows = status.Phases.Select<PhaseInfo, IReadOnlyList<string>>(phase =>
        [
            phase.Id.ToString(),
            Cell(phase.Name),
            StateLabel(phase.State),
            phase.Release is null ? "None" : $"`{phase.Release}`",
            $"[{(phase.Id.IsLaunch ? "Launch gate" : $"Phase {phase.Id}")}]({RelativeLink(phase.Plan)})",
            phase.Milestone is null ? "None" : phase.MilestoneUrl is null ? Cell(phase.Milestone) : $"[{Cell(phase.Milestone)}]({phase.MilestoneUrl})",
        ]);

        return MarkdownText.Table(["Phase", "Name", "State", "Release", "Plan", "Milestone"], rows).TrimEnd('\n');
    }

    private static string WorkPackageSections(StatusDocument status)
    {
        List<string> sections = [];

        foreach (var phase in status.Phases.Where(phase => phase.WorkPackages.Count > 0 && phase.State != WorkState.Planned))
        {
            var rows = phase.WorkPackages.Select<WorkPackageInfo, IReadOnlyList<string>>(workPackage =>
            [
                workPackage.Id,
                Cell(workPackage.Title),
                workPackage.Size,
                StateLabel(workPackage.State),
                workPackage.Issue is null ? "None" : $"[Issue {workPackage.Issue}]({IssueUrl(status, workPackage.Issue.Value)})",
                workPackage.PullRequest is null ? "None" : $"[Pull request {workPackage.PullRequest}]({PullRequestUrl(status, workPackage.PullRequest.Value)})",
                workPackage.DependsOn.Count == 0 ? "None" : string.Join(", ", workPackage.DependsOn),
            ]);

            var evidence = phase.WorkPackages
                .Where(workPackage => workPackage.Evidence.Count > 0)
                .Select(workPackage => $"- **{workPackage.Id}:** {string.Join(", ", workPackage.Evidence.Select(EvidenceLink))}")
                .ToList();

            var section = $"## Phase {phase.Id} work packages\n\n" +
                MarkdownText.Table(["WP", "Title", "Size", "State", "Issue", "Pull request", "Depends on"], rows).TrimEnd('\n');

            if (evidence.Count > 0)
            {
                section += "\n\nEvidence for the work packages that have it:\n\n" + string.Join("\n", evidence);
            }

            sections.Add(section);
        }

        var planned = status.Phases.Where(phase => phase.WorkPackages.Count > 0 && phase.State == WorkState.Planned).ToList();

        if (planned.Count > 0)
        {
            var counts = string.Join("; ", planned.Select(phase => $"phase {phase.Id} has {phase.WorkPackages.Count} work {(phase.WorkPackages.Count == 1 ? "package" : "packages")}"));
            sections.Add($"## Phases that have not started\n\nOn the phases that have not started, {counts}. Every one of these work packages is planned, and its order and dependencies are in [status.yaml](status.yaml).");
        }

        return string.Join("\n\n", sections);
    }

    private static string EvidenceLink(string evidence)
    {
        if (evidence.StartsWith("https://", StringComparison.Ordinal))
        {
            return $"[external evidence]({evidence})";
        }

        // A directory has no page to link to, so it is shown as a path.
        return evidence.EndsWith('/') ? $"`{evidence}`" : $"[{evidence}]({RelativeLink(evidence)})";
    }

    private static string PhaseLink(PhaseInfo phase) =>
        $"[{(phase.Id.IsLaunch ? Cell(phase.Name) : $"Phase {phase.Id}: {Cell(phase.Name)}")}]({RelativeLink(phase.Plan)})";

    private static string IssueSuffix(StatusDocument status, WorkPackageInfo workPackage) =>
        workPackage.Issue is null ? string.Empty : $" ([Issue {workPackage.Issue}]({IssueUrl(status, workPackage.Issue.Value)}))";

    private static string IssueUrl(StatusDocument status, int number) => $"https://github.com/{status.Project.Repository}/issues/{number.ToString(CultureInfo.InvariantCulture)}";

    private static string PullRequestUrl(StatusDocument status, int number) => $"https://github.com/{status.Project.Repository}/pull/{number.ToString(CultureInfo.InvariantCulture)}";

    private static string RelativeLink(string repositoryPath) => Path.GetRelativePath(PageDirectory, repositoryPath).Replace('\\', '/');

    private static string StateLabel(WorkState state) => state switch
    {
        WorkState.Planned => "Planned",
        WorkState.InProgress => "In progress",
        WorkState.Blocked => "Blocked",
        _ => "Completed",
    };

    // The status file is reviewed repository content, so formatting such as a code span is kept. Only what would break a table is changed.
    private static string Cell(string text) => text.ReplaceLineEndings(" ").Replace("|", "\\|", StringComparison.Ordinal).Trim();
}
