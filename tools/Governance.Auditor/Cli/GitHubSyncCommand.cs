using System.CommandLine;
using Governance.Auditor.Common;
using Governance.Auditor.GitHubSync;
using Governance.Auditor.Status;

namespace Governance.Auditor.Cli;

internal static class GitHubSyncCommand
{
    private static readonly string[] Kinds = ["labels", "milestones", "issues"];

    public static Command Create(CliContext context)
    {
        var repository = new Option<string?>("--repo") { Description = "The repository as owner/name. Defaults to the repository named in status.yaml." };
        var apply = new Option<bool>("--apply") { Description = "Make the changes. Without it, the command only shows the diff and writes nothing." };
        var only = new Option<string[]>("--only") { Description = $"Sync only this kind of object ({string.Join(", ", Kinds)}). Repeat the option for several." };
        var phases = new Option<string[]>("--phase") { Description = "Sync the issues of this phase's work packages, including those without an issue yet. Repeat for several. By default, every work package that has an issue, and every work package of an in-progress phase." };
        var failOnDrift = new Option<bool>("--fail-on-drift") { Description = "In a dry run, exit with 1 when GitHub differs from the repository." };

        var command = new Command(
            "github-sync",
            "Show, and with --apply make, the changes that bring GitHub labels, milestones, and work-package issues in line with the repository. It never deletes anything.")
        {
            repository, apply, only, phases, failOnDrift,
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var files = context.Files(parseResult);
            List<Finding> findings = [];

            if (StatusFile.Load(files, findings) is not { } status)
            {
                return context.Report("github-sync", findings);
            }

            var request = new SyncRequest(
                parseResult.GetValue(repository) ?? status.Project.Repository,
                parseResult.GetValue(apply),
                parseResult.GetValue(only) ?? [],
                parseResult.GetValue(phases) ?? [],
                parseResult.GetValue(failOnDrift));

            return await RunAsync(context, files, status, request, cancellationToken);
        });

        return command;
    }

    private static async Task<int> RunAsync(CliContext context, RepositoryFiles files, StatusDocument status, SyncRequest request, CancellationToken cancellationToken)
    {
        if (request.Apply && string.Equals(context.Environment("GITHUB_ACTIONS"), "true", StringComparison.Ordinal))
        {
            // Writing to GitHub needs the maintainer's own sign-in, and a workflow must never hold that on behalf of untrusted input.
            throw new GovernanceException("--apply is for the maintainer's workstation. It refuses to run in GitHub Actions.");
        }

        var unknown = request.Only.Where(kind => !Kinds.Contains(kind, StringComparer.Ordinal)).ToList();

        if (unknown.Count > 0)
        {
            throw new GovernanceException($"Unknown kind '{string.Join("', '", unknown)}'. The kinds are: {string.Join(", ", Kinds)}.");
        }

        var scope = new SyncScope(
            request.Only.Length == 0 || request.Only.Contains("labels", StringComparer.Ordinal),
            request.Only.Length == 0 || request.Only.Contains("milestones", StringComparer.Ordinal),
            request.Only.Length == 0 || request.Only.Contains("issues", StringComparer.Ordinal),
            request.Phases.Length == 0 ? null : request.Phases.ToHashSet(StringComparer.Ordinal));

        var api = new GitHubCliApi(context.Processes, request.Repository, files.Root);

        var plan = await GitHubSyncPlanner.PlanAsync(
            api,
            status,
            GitHubSyncPlanner.LoadLabels(files),
            GitHubSyncPlanner.LoadMilestones(files, status),
            scope,
            cancellationToken);

        context.Output.WriteLine($"github-sync: {request.Repository}");

        foreach (var action in plan.Actions)
        {
            context.Output.WriteLine($"  would {action.Description}.");
        }

        if (plan.Findings.HasErrors())
        {
            context.Output.WriteLine("github-sync: nothing was changed, because the findings below must be fixed first.");
            return context.Report("github-sync", [.. plan.Findings]);
        }

        if (plan.Actions.Count == 0)
        {
            context.Output.WriteLine("github-sync: GitHub already matches the repository.");
            return context.Report("github-sync", [.. plan.Findings]);
        }

        if (!request.Apply)
        {
            context.Output.WriteLine($"github-sync: dry run. {plan.Actions.Count} changes would be made. Run again with --apply to make them.");
            var exit = context.Report("github-sync", [.. plan.Findings]);
            return request.FailOnDrift ? 1 : exit;
        }

        foreach (var action in plan.Actions)
        {
            await action.Apply(api, cancellationToken);
            context.Output.WriteLine($"  done: {action.Description}.");
        }

        context.Output.WriteLine($"github-sync: {plan.Actions.Count} changes made.");
        return context.Report("github-sync", [.. plan.Findings]);
    }

    private sealed record SyncRequest(string Repository, bool Apply, string[] Only, string[] Phases, bool FailOnDrift);
}
