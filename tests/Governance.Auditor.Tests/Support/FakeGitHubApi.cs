using Governance.Auditor.GitHubSync;

namespace Governance.Auditor.Tests.Support;

/// <summary>An in-memory GitHub, so a test can plan, apply, and plan again to prove that the sync converges.</summary>
internal sealed class FakeGitHubApi : IGitHubApi
{
    private int _nextIssue = 100;
    private int _nextMilestone = 10;

    public List<GitHubLabel> Labels { get; } = [];

    public List<GitHubMilestone> Milestones { get; } = [];

    public List<GitHubIssue> Issues { get; } = [];

    public List<string> Writes { get; } = [];

    public Task<IReadOnlyList<GitHubLabel>> GetLabelsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<GitHubLabel>>([.. Labels]);

    public Task CreateLabelAsync(GitHubLabel label, CancellationToken cancellationToken)
    {
        Writes.Add($"create label {label.Name}");
        Labels.Add(label);
        return Task.CompletedTask;
    }

    public Task UpdateLabelAsync(string name, string? color, string? description, CancellationToken cancellationToken)
    {
        Writes.Add($"update label {name}");
        var index = Labels.FindIndex(label => label.Name == name);
        Labels[index] = Labels[index] with { Color = color ?? Labels[index].Color, Description = description ?? Labels[index].Description };
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<GitHubMilestone>> GetMilestonesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<GitHubMilestone>>([.. Milestones]);

    public Task CreateMilestoneAsync(string title, string description, string state, CancellationToken cancellationToken)
    {
        Writes.Add($"create milestone {title}");
        Milestones.Add(new GitHubMilestone(_nextMilestone++, title, description, state));
        return Task.CompletedTask;
    }

    public Task UpdateMilestoneAsync(int number, string? description, string? state, CancellationToken cancellationToken)
    {
        Writes.Add($"update milestone {number}");
        var index = Milestones.FindIndex(milestone => milestone.Number == number);
        Milestones[index] = Milestones[index] with { Description = description ?? Milestones[index].Description, State = state ?? Milestones[index].State };
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<GitHubIssue>> GetIssuesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<GitHubIssue>>([.. Issues]);

    public Task<int> CreateIssueAsync(IssueDraft draft, CancellationToken cancellationToken)
    {
        Writes.Add($"create issue {draft.Title}");
        var number = _nextIssue++;
        Issues.Add(new GitHubIssue(number, draft.Title, draft.Body, "open", draft.Labels, draft.MilestoneTitle));
        return Task.FromResult(number);
    }

    public Task UpdateIssueAsync(int number, IssueUpdate update, CancellationToken cancellationToken)
    {
        Writes.Add($"update issue {number}");
        var index = Issues.FindIndex(issue => issue.Number == number);
        var current = Issues[index];
        Issues[index] = current with
        {
            Title = update.Title ?? current.Title,
            Body = update.Body ?? current.Body,
            State = update.State ?? current.State,
            Labels = update.Labels ?? current.Labels,
            MilestoneTitle = update.MilestoneTitle ?? current.MilestoneTitle,
        };
        return Task.CompletedTask;
    }
}
