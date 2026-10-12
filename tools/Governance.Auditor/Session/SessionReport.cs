using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Governance.Auditor.Common;

namespace Governance.Auditor.Session;

/// <summary>Turns the advice into text for a person, JSON for a script, and Markdown for a pull request or a job summary.</summary>
internal static class SessionReport
{
    private static readonly JsonSerializerOptions CompactJson = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>What each kind of advice means for the session that reads it. The meaning belongs to the command, and the policy decides when it applies.</summary>
    public static string Action(string advice) => advice switch
    {
        SessionVocabulary.Continue => "Carry on in this session.",
        SessionVocabulary.Fresh => "Stop here, and start a fresh session with the kickoff prompt below.",
        SessionVocabulary.FinishHandoff => "Close the handoff gaps, then run the check again.",
        _ => "Do not start work. Tell the maintainer what the previous session left behind, and ask how to proceed.",
    };

    public static string RenderText(SessionAdvice advice)
    {
        var builder = new StringBuilder();
        builder.Append($"session: {advice.Advice}\n");
        builder.Append($"  action:    {Action(advice.Advice)}\n");
        builder.Append($"  moment:    {advice.Facts.Moment}\n");
        builder.Append($"  task:      {advice.Facts.Task}\n");
        builder.Append($"  context:   {advice.Facts.Context}\n");
        builder.Append($"  rule:      {advice.Rule.Id}\n");
        builder.Append("  reasons:\n");

        foreach (var reason in advice.Reasons)
        {
            builder.Append($"    - {reason}\n");
        }

        builder.Append($"  boundary:  {advice.Facts.Boundary}\n");
        builder.Append($"  next:      {DescribeNext(advice.Next)}\n");
        builder.Append($"  handoff:   {advice.Facts.Handoff}\n");

        foreach (var gap in advice.Gaps)
        {
            builder.Append($"    - {gap.Message}\n");
        }

        builder.Append($"  git:       {DescribeGit(advice.Git)}\n");

        if (advice.Notes.Count > 0)
        {
            builder.Append("  notes:\n");

            foreach (var note in advice.Notes)
            {
                builder.Append($"    - {note}\n");
            }
        }

        if (advice.KickoffPrompt is not null)
        {
            builder.Append("\n--- kickoff prompt ---\n").Append(advice.KickoffPrompt).Append("\n--- end of kickoff prompt ---\n");
        }

        return builder.ToString().TrimEnd('\n');
    }

    public static string ToJson(SessionAdvice advice) => JsonSerializer.Serialize(
        new
        {
            advice = advice.Advice,
            rule = advice.Rule.Id,
            moment = advice.Facts.Moment,
            task = advice.Facts.Task,
            context = advice.Facts.Context,
            boundary = advice.Facts.Boundary,
            handoff = advice.Facts.Handoff,
            reasons = advice.Reasons,
            gaps = advice.Gaps.Select(gap => new { kind = gap.Kind, message = gap.Message }),
            notes = advice.Notes,
            nextWorkPackage = advice.Next is null
                ? null
                : new { id = advice.Next.Id, title = advice.Next.Title, issue = advice.Next.Issue, phase = advice.Next.Phase, branch = advice.Next.Branch, inProgress = advice.Next.InProgress },
            git = new
            {
                branch = advice.Git.Branch,
                upstream = advice.Git.Upstream,
                ahead = advice.Git.Ahead,
                behind = advice.Git.Behind,
                unpushed = advice.Git.Unpushed,
                changedPaths = advice.Git.ChangedPaths.Count,
            },
            kickoffPrompt = advice.KickoffPrompt,
        },
        CompactJson);

    public static string RenderMarkdown(SessionAdvice advice)
    {
        var builder = new StringBuilder();
        builder.Append($"### Session boundary: {advice.Advice}\n\n");
        builder.Append(MarkdownText.Table(
            ["Question", "Answer"],
            [
                ["Advice", MarkdownText.Cell(Action(advice.Advice))],
                ["Moment, task, and context", MarkdownText.Cell($"{advice.Facts.Moment}, {advice.Facts.Task}, {advice.Facts.Context}")],
                ["Rule", MarkdownText.Code(advice.Rule.Id)],
                ["Boundary", MarkdownText.Cell(advice.Facts.Boundary)],
                ["Next work package", MarkdownText.Cell(DescribeNext(advice.Next))],
                ["Handoff", MarkdownText.Cell(advice.Facts.Handoff)],
                ["Git", MarkdownText.Cell(DescribeGit(advice.Git))],
            ]));

        builder.Append("\nWhy:\n\n");
        builder.Append(string.Join("\n", advice.Reasons.Select(reason => $"- {MarkdownText.Cell(reason, 600)}"))).Append('\n');

        if (advice.Gaps.Count > 0)
        {
            builder.Append("\nHandoff gaps:\n\n");
            builder.Append(string.Join("\n", advice.Gaps.Select(gap => $"- {MarkdownText.Cell(gap.Message, 600)}"))).Append('\n');
        }

        if (advice.Notes.Count > 0)
        {
            builder.Append("\nNotes:\n\n");
            builder.Append(string.Join("\n", advice.Notes.Select(note => $"- {MarkdownText.Cell(note, 600)}"))).Append('\n');
        }

        if (advice.KickoffPrompt is not null)
        {
            // The fence is longer than any run of backticks in the prompt, so no value that was filled in can close it early.
            var fence = new string('`', Math.Max(3, LongestBacktickRun(advice.KickoffPrompt) + 1));
            builder.Append($"\nKickoff prompt:\n\n{fence}text\n{advice.KickoffPrompt}\n{fence}\n");
        }

        builder.Append('\n');
        return builder.ToString();
    }

    private static string DescribeNext(NextWorkPackage? next)
    {
        if (next is null)
        {
            return "none";
        }

        var issue = next.Issue is { } number ? $"Issue {number.ToString(CultureInfo.InvariantCulture)}" : "no Issue yet";
        return $"{next.Id}: {next.Title} ({issue}, phase {next.Phase}), branch {next.Branch}{(next.InProgress ? ", in progress" : string.Empty)}";
    }

    private static string DescribeGit(GitFacts git) =>
        $"branch {git.Branch}, upstream {git.Upstream ?? "none"}, {git.Ahead} ahead, {git.Behind} behind, {git.ChangedPaths.Count} changed paths";

    private static int LongestBacktickRun(string text)
    {
        int longest = 0, run = 0;

        foreach (var character in text)
        {
            run = character == '`' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }

        return longest;
    }
}
