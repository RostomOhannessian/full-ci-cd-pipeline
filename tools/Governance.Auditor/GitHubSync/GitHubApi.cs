using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Governance.Auditor.Common;

namespace Governance.Auditor.GitHubSync;

internal sealed record GitHubLabel(string Name, string Color, string? Description);

internal sealed record GitHubMilestone(int Number, string Title, string? Description, string State);

internal sealed record GitHubIssue(int Number, string Title, string Body, string State, IReadOnlyList<string> Labels, string? MilestoneTitle);

/// <summary>What to create. The milestone is named by title, and the API resolves the number.</summary>
internal sealed record IssueDraft(string Title, string Body, IReadOnlyList<string> Labels, string? MilestoneTitle);

/// <summary>What to change on an issue. A null member is left as it is. <see cref="Labels"/> replaces the whole label list.</summary>
internal sealed record IssueUpdate(string? Title = null, string? Body = null, string? State = null, string? StateReason = null, IReadOnlyList<string>? Labels = null, string? MilestoneTitle = null);

/// <summary>The few GitHub operations that <c>github-sync</c> needs. Tests replace it with an in-memory fake.</summary>
internal interface IGitHubApi
{
    Task<IReadOnlyList<GitHubLabel>> GetLabelsAsync(CancellationToken cancellationToken);

    Task CreateLabelAsync(GitHubLabel label, CancellationToken cancellationToken);

    Task UpdateLabelAsync(string name, string? color, string? description, CancellationToken cancellationToken);

    Task<IReadOnlyList<GitHubMilestone>> GetMilestonesAsync(CancellationToken cancellationToken);

    Task CreateMilestoneAsync(string title, string description, string state, CancellationToken cancellationToken);

    Task UpdateMilestoneAsync(int number, string? description, string? state, CancellationToken cancellationToken);

    Task<IReadOnlyList<GitHubIssue>> GetIssuesAsync(CancellationToken cancellationToken);

    Task<int> CreateIssueAsync(IssueDraft draft, CancellationToken cancellationToken);

    Task UpdateIssueAsync(int number, IssueUpdate update, CancellationToken cancellationToken);
}

/// <summary>
/// Talks to GitHub through the GitHub CLI, so it uses the maintainer's own sign-in and the tool stores no token. Arguments go to
/// <c>gh</c> as a list and bodies go through standard input, so text from a document is never parsed as a command.
/// </summary>
internal sealed partial class GitHubCliApi : IGitHubApi
{
    // A null member means "leave it as it is", so it is left out of the request.
    private static readonly JsonSerializerOptions BodyOptions = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private readonly IProcessRunner _processes;
    private readonly string _repository;
    private readonly string _workingDirectory;
    private IReadOnlyList<GitHubMilestone>? _milestones;

    public GitHubCliApi(IProcessRunner processes, string repository, string workingDirectory)
    {
        if (!RepositoryPattern().IsMatch(repository))
        {
            throw new GovernanceException($"'{repository}' is not a repository name in the form owner/name.");
        }

        _processes = processes;
        _repository = repository;
        _workingDirectory = workingDirectory;
    }

    [GeneratedRegex(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RepositoryPattern();

    public async Task<IReadOnlyList<GitHubLabel>> GetLabelsAsync(CancellationToken cancellationToken) =>
        [.. (await GetAllAsync($"repos/{_repository}/labels?per_page=100", cancellationToken))
            .Select(label => new GitHubLabel(Text(label, "name"), Text(label, "color").ToLowerInvariant(), NullableText(label, "description")))];

    public Task CreateLabelAsync(GitHubLabel label, CancellationToken cancellationToken) =>
        SendAsync("POST", $"repos/{_repository}/labels", new { name = label.Name, color = label.Color, description = label.Description ?? string.Empty }, cancellationToken);

    public Task UpdateLabelAsync(string name, string? color, string? description, CancellationToken cancellationToken) =>
        SendAsync("PATCH", $"repos/{_repository}/labels/{Uri.EscapeDataString(name)}", new { color, description }, cancellationToken);

    public async Task<IReadOnlyList<GitHubMilestone>> GetMilestonesAsync(CancellationToken cancellationToken)
    {
        _milestones = [.. (await GetAllAsync($"repos/{_repository}/milestones?state=all&per_page=100", cancellationToken))
            .Select(milestone => new GitHubMilestone((int)milestone["number"]!, Text(milestone, "title"), NullableText(milestone, "description"), Text(milestone, "state")))];
        return _milestones;
    }

    public async Task CreateMilestoneAsync(string title, string description, string state, CancellationToken cancellationToken)
    {
        await SendAsync("POST", $"repos/{_repository}/milestones", new { title, description, state }, cancellationToken);
        _milestones = null;
    }

    public Task UpdateMilestoneAsync(int number, string? description, string? state, CancellationToken cancellationToken) =>
        SendAsync("PATCH", $"repos/{_repository}/milestones/{number}", new { description, state }, cancellationToken);

    public async Task<IReadOnlyList<GitHubIssue>> GetIssuesAsync(CancellationToken cancellationToken) =>
        [.. (await GetAllAsync($"repos/{_repository}/issues?state=all&per_page=100", cancellationToken))
            .Where(issue => issue["pull_request"] is null)
            .Select(issue => new GitHubIssue(
                (int)issue["number"]!,
                Text(issue, "title"),
                NullableText(issue, "body") ?? string.Empty,
                Text(issue, "state"),
                [.. (issue["labels"] as JsonArray ?? []).Select(label => Text(label!, "name"))],
                issue["milestone"] is JsonObject milestone ? Text(milestone, "title") : null))];

    public async Task<int> CreateIssueAsync(IssueDraft draft, CancellationToken cancellationToken)
    {
        var milestone = draft.MilestoneTitle is null ? (int?)null : await MilestoneNumberAsync(draft.MilestoneTitle, cancellationToken);
        var created = await SendAsync("POST", $"repos/{_repository}/issues", new { title = draft.Title, body = draft.Body, labels = draft.Labels, milestone }, cancellationToken);
        return (int)created!["number"]!;
    }

    public async Task UpdateIssueAsync(int number, IssueUpdate update, CancellationToken cancellationToken)
    {
        var body = new JsonObject();

        if (update.Title is not null)
        {
            body["title"] = update.Title;
        }

        if (update.Body is not null)
        {
            body["body"] = update.Body;
        }

        if (update.State is not null)
        {
            body["state"] = update.State;
        }

        if (update.StateReason is not null)
        {
            body["state_reason"] = update.StateReason;
        }

        if (update.Labels is not null)
        {
            body["labels"] = new JsonArray([.. update.Labels.Select(label => (JsonNode?)JsonValue.Create(label))]);
        }

        if (update.MilestoneTitle is not null)
        {
            body["milestone"] = await MilestoneNumberAsync(update.MilestoneTitle, cancellationToken);
        }

        await SendAsync("PATCH", $"repos/{_repository}/issues/{number}", body, cancellationToken);
    }

    private async Task<int> MilestoneNumberAsync(string title, CancellationToken cancellationToken)
    {
        var milestones = _milestones ?? await GetMilestonesAsync(cancellationToken);
        return milestones.FirstOrDefault(milestone => string.Equals(milestone.Title, title, StringComparison.Ordinal))?.Number
            ?? throw new GovernanceException($"The milestone '{title}' does not exist on {_repository}.");
    }

    private async Task<IReadOnlyList<JsonNode>> GetAllAsync(string endpoint, CancellationToken cancellationToken)
    {
        var result = await RunAsync(["api", "--paginate", "--slurp", endpoint], null, cancellationToken);
        var pages = JsonNode.Parse(result) as JsonArray ?? throw new GovernanceException($"GitHub returned an unexpected response for {endpoint}.");

        // --slurp wraps the pages in an outer array, and each page is an array of items.
        return [.. pages.SelectMany(page => page as JsonArray ?? []).Where(item => item is not null).Select(item => item!)];
    }

    private async Task<JsonNode?> SendAsync(string method, string endpoint, object body, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(body, BodyOptions);
        var result = await RunAsync(["api", "--method", method, endpoint, "--input", "-"], json, cancellationToken);
        return string.IsNullOrWhiteSpace(result) ? null : JsonNode.Parse(result);
    }

    private async Task<string> RunAsync(string[] arguments, string? standardInput, CancellationToken cancellationToken)
    {
        var result = await _processes.RunAsync(new ProcessRequest("gh", arguments, _workingDirectory, standardInput), cancellationToken);

        if (!result.Succeeded)
        {
            throw new GovernanceException($"The GitHub CLI failed ({string.Join(' ', arguments.Take(4))}). Sign in with 'gh auth login' and check the repository name. {FirstLine(result.StandardError)}");
        }

        return result.StandardOutput;
    }

    private static string Text(JsonNode node, string name) => (string?)node[name] ?? throw new GovernanceException($"GitHub returned an item without '{name}'.");

    private static string? NullableText(JsonNode node, string name) => (string?)node[name];

    private static string FirstLine(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;
}
