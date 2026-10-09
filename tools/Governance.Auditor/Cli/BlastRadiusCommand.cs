using System.CommandLine;
using System.Text;
using Governance.Auditor.BlastRadius;
using Governance.Auditor.Common;

namespace Governance.Auditor.Cli;

internal static class BlastRadiusCommand
{
    private const int MaxUnclassifiedShown = 20;

    public static Command Create(CliContext context)
    {
        var baseReference = new Option<string?>("--base") { Description = "The commit the change branches from. The changed paths are the paths that differ between it and --head." };
        var head = new Option<string?>("--head") { Description = "The last commit of the change. Defaults to HEAD.", DefaultValueFactory = _ => "HEAD" };
        var paths = new Option<string[]>("--path") { Description = "A changed path, relative to the repository root. Repeat the option for several. Use it instead of --base." };
        var allFiles = new Option<bool>("--all-files") { Description = "Treat every file in the repository as changed. Use it when the base is unknown, so selection fails closed and runs everything that applies." };
        var map = new Option<string>("--map") { Description = "The blast-radius map, relative to the repository root.", DefaultValueFactory = _ => BlastRadiusMap.Path };
        var json = new Option<bool>("--json") { Description = "Print the result as one line of JSON instead of text." };
        var githubOutput = new Option<string?>("--github-output") { Description = "Append the result as GitHub Actions step outputs to this file (pass the GITHUB_OUTPUT file)." };
        var summary = StatusCommands.SummaryOption();

        var command = new Command("blast-radius", "Say which layers and areas a change touches, and which jobs, documentation, and reviews it requires.")
        {
            baseReference, head, paths, allFiles, map, json, githubOutput, summary,
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var files = context.Files(parseResult);
            var changed = await ChangedPathsAsync(context, files, parseResult.GetValue(baseReference), parseResult.GetValue(head) ?? "HEAD", parseResult.GetValue(paths) ?? [], parseResult.GetValue(allFiles), cancellationToken);
            var result = BlastRadiusEvaluator.Evaluate(BlastRadiusMap.Load(files, parseResult.GetValue(map)!), changed);

            context.Output.WriteLine(parseResult.GetValue(json) ? result.ToJson() : RenderText(result));

            if (parseResult.GetValue(githubOutput) is { Length: > 0 } outputPath)
            {
                File.AppendAllText(outputPath, RenderGitHubOutput(result), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }

            if (parseResult.GetValue(summary) is { Length: > 0 } summaryPath)
            {
                MarkdownText.Append(summaryPath, RenderMarkdown(result));
            }

            // Evaluating a change is not a pass or fail check, except that a path no rule covers is worth a warning.
            List<Finding> warnings = [.. result.Unclassified.Take(MaxUnclassifiedShown).Select(path => Finding.Warning("blast-radius-unclassified", "No blast-radius rule covers this path, so the strict fallback applies. Add a rule to the map.", path))];

            if (parseResult.GetValue(json))
            {
                // Standard output stays pure JSON for a script to read.
                foreach (var warning in warnings)
                {
                    context.Error.WriteLine(warning);
                }

                return 0;
            }

            return context.Report("blast-radius", warnings);
        });

        return command;
    }

    private static async Task<IReadOnlyList<string>> ChangedPathsAsync(
        CliContext context,
        RepositoryFiles files,
        string? baseReference,
        string head,
        string[] explicitPaths,
        bool allFiles,
        CancellationToken cancellationToken)
    {
        if (explicitPaths.Length > 0)
        {
            return explicitPaths;
        }

        if (allFiles)
        {
            var listing = await context.Processes.RunAsync(new ProcessRequest("git", ["ls-files", "-z"], files.Root), cancellationToken);

            return listing.Succeeded
                ? listing.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries)
                : throw new GovernanceException($"Git could not list the files. {listing.StandardError.Trim()}");
        }

        if (string.IsNullOrEmpty(baseReference))
        {
            throw new GovernanceException("Give the change with --base (and optionally --head), list its paths with --path, or use --all-files when the base is unknown.");
        }

        foreach (var reference in new[] { baseReference, head })
        {
            // A reference that starts with a dash could be read as a Git option.
            if (reference.StartsWith('-') || reference.Any(char.IsWhiteSpace) || reference.Contains("..", StringComparison.Ordinal))
            {
                throw new GovernanceException($"'{reference}' is not a valid Git reference for the change.");
            }
        }

        // --no-renames lists both sides of a rename, so a file moved out of a sensitive path still counts.
        var result = await context.Processes.RunAsync(
            new ProcessRequest("git", ["diff", "--name-only", "--no-renames", "-z", $"{baseReference}...{head}"], files.Root),
            cancellationToken);

        if (!result.Succeeded)
        {
            throw new GovernanceException($"Git could not compare {baseReference} with {head}. Fetch the full history first. {result.StandardError.Trim()}");
        }

        return result.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    private static string RenderText(BlastRadiusResult result)
    {
        var builder = new StringBuilder();
        builder.Append($"blast-radius: {result.ChangedPaths} changed paths\n");
        builder.Append($"  layers:  {Join(result.Layers)}\n");
        builder.Append($"  areas:   {Join(result.Areas)}\n");
        builder.Append($"  jobs:    {Join(result.Jobs)}\n");
        builder.Append($"  docs:    {Join(result.Docs.Select(doc => doc.Id))}\n");
        builder.Append($"  reviews: {Join(result.Reviews.Select(review => review.Id))}\n");
        builder.Append($"  flags:   {Join(result.Flags)}");
        return builder.ToString();
    }

    private static string Join(IEnumerable<string> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? "none" : string.Join(", ", list);
    }

    private static string RenderGitHubOutput(BlastRadiusResult result)
    {
        var builder = new StringBuilder();
        builder.Append($"layers={System.Text.Json.JsonSerializer.Serialize(result.Layers)}\n");
        builder.Append($"areas={System.Text.Json.JsonSerializer.Serialize(result.Areas)}\n");
        builder.Append($"jobs={System.Text.Json.JsonSerializer.Serialize(result.Jobs)}\n");
        builder.Append($"docs={System.Text.Json.JsonSerializer.Serialize(result.Docs.Select(doc => doc.Id))}\n");
        builder.Append($"reviews={System.Text.Json.JsonSerializer.Serialize(result.Reviews.Select(review => review.Id))}\n");
        builder.Append($"flags={System.Text.Json.JsonSerializer.Serialize(result.Flags)}\n");
        builder.Append($"unclassified-count={result.Unclassified.Count}\n");

        foreach (var (job, run) in result.Run)
        {
            builder.Append($"run-{job}={(run ? "true" : "false")}\n");
        }

        return builder.ToString();
    }

    private static string RenderMarkdown(BlastRadiusResult result)
    {
        var builder = new StringBuilder();
        builder.Append($"### Blast radius: {result.ChangedPaths} changed paths\n\n");
        builder.Append(MarkdownText.Table(
            ["Question", "Answer"],
            [
                ["Layers touched", MarkdownText.Cell(Join(result.Layers))],
                ["Areas touched", MarkdownText.Cell(Join(result.Areas))],
                ["Jobs required", MarkdownText.Cell(Join(result.Jobs))],
                ["Risk flags", MarkdownText.Cell(Join(result.Flags))],
            ]));

        if (result.Docs.Count > 0)
        {
            builder.Append("\nDocumentation to update:\n\n");
            builder.Append(string.Join("\n", result.Docs.Select(doc => $"- {MarkdownText.Code(doc.Id)}: {MarkdownText.Cell(doc.Text)}"))).Append('\n');
        }

        if (result.Reviews.Count > 0)
        {
            builder.Append("\nReviews required:\n\n");
            builder.Append(string.Join("\n", result.Reviews.Select(review => $"- {MarkdownText.Code(review.Id)}: {MarkdownText.Cell(review.Text)}"))).Append('\n');
        }

        if (result.Unclassified.Count > 0)
        {
            builder.Append($"\n{result.Unclassified.Count} changed paths match no rule, so the strict fallback applies. The first {Math.Min(MaxUnclassifiedShown, result.Unclassified.Count)}:\n\n");
            builder.Append(string.Join("\n", result.Unclassified.Take(MaxUnclassifiedShown).Select(path => $"- {MarkdownText.Code(path)}"))).Append('\n');
        }

        builder.Append('\n');
        return builder.ToString();
    }
}
