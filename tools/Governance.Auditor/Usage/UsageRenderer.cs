using System.Globalization;
using System.Text;
using Governance.Auditor.Common;
using Governance.Auditor.Status;

namespace Governance.Auditor.Usage;

/// <summary>
/// Renders <c>docs/project/AI-USAGE.md</c> from <c>ai-usage.yaml</c> and <c>status.yaml</c> and nothing else, so the same input always gives
/// the same page. Session IDs are shortened to their first eight characters, and no other identifying text enters the page.
/// </summary>
internal static class UsageRenderer
{
    public static string Render(UsageLedger ledger, StatusDocument status)
    {
        var builder = new StringBuilder();
        builder.Append($"""
            ---
            title: "AI usage"
            description: "What the AI-paired sessions of this project cost, by phase, work package, model, and effort, and the observed AIU for each size class."
            type: status
            audience: [maintainers, contributors, learners]
            last-verified: {ledger.Updated}
            owner: "@RostomOhannessian"
            ---

            # AI usage

            This page is generated from [ai-usage.yaml](ai-usage.yaml), which is the source of truth, by `governance usage render`. Do not edit it by hand. A session adds its own records with the `/ai-usage-recorder` skill, then runs the command and commits both files. CI fails when this page differs from the command's output. Last updated {ledger.Updated}.

            The unit is the AI unit (AIU) that the Copilot session store reports for each model call. [How to read the ledger, the attribution rules, and the estimate](../explanation/ai-usage-statistics.md) explain the columns. To estimate the work that remains, run `dotnet run --project tools/Governance.Auditor -- usage estimate`.


            """.ReplaceLineEndings("\n"));

        builder.Append(Summary(ledger));
        builder.Append(ByPhase(ledger, status));
        builder.Append(ByWorkPackage(ledger, status));
        builder.Append(BySize(ledger, status));
        builder.Append(ByModel(ledger));
        builder.Append(BySession(ledger));

        return builder.ToString().TrimEnd('\n') + "\n";
    }

    private static string Summary(UsageLedger ledger)
    {
        var rows = ledger.Records.SelectMany(record => record.Usage).ToList();
        var sessions = ledger.Records.Select(record => record.Session).Distinct(StringComparer.Ordinal).Count();
        var peak = ledger.Records.MaxBy(record => record.PeakContextTokens)!;

        var table = MarkdownText.Table(
            ["Measure", "Value"],
            [
                ["Sessions", UsageAnalysis.Number(sessions)],
                ["Slices (records)", UsageAnalysis.Number(ledger.Records.Count)],
                ["Calls", UsageAnalysis.Number(rows.Sum(row => (long)row.Calls))],
                ["AIU", UsageAnalysis.Number(ledger.Records.Sum(record => record.Aiu))],
                ["Model time", $"{(rows.Sum(row => row.ModelSeconds) / 3600m).ToString("0.0", CultureInfo.InvariantCulture)} hours"],
                ["Period", $"{UsageAnalysis.Day(ledger.Records.Min(record => record.Window.From))} to {UsageAnalysis.Day(ledger.Records.Max(record => record.Window.To))}"],
                ["Peak context", $"{UsageAnalysis.Number(peak.PeakContextTokens)} tokens in one call (session {Short(peak.Session)})"],
                ["Compactions", UsageAnalysis.Number(ledger.Records.Sum(record => record.Compactions))],
            ]);

        return $"## Totals\n\n{table}";
    }

    private static string ByPhase(UsageLedger ledger, StatusDocument status)
    {
        var rows = new List<IReadOnlyList<string>>();

        foreach (var phase in status.Phases.Where(phase => ledger.Records.Any(record => record.Phase == phase.Id)))
        {
            var records = ledger.Records.Where(record => record.Phase == phase.Id).ToList();
            decimal Aiu(string activity) => records.Where(record => record.Activity == activity).Sum(record => record.Aiu);

            rows.Add(
            [
                phase.Id.ToString(),
                MarkdownText.Cell(phase.Name),
                UsageAnalysis.Number(Aiu(UsageVocabulary.Delivery)),
                UsageAnalysis.Number(Aiu(UsageVocabulary.Planning)),
                UsageAnalysis.Number(Aiu(UsageVocabulary.Coordination)),
                UsageAnalysis.Number(Aiu(UsageVocabulary.Fix)),
                UsageAnalysis.Number(records.Sum(record => record.Aiu)),
                UsageAnalysis.Number(records.Sum(record => (long)record.Calls)),
            ]);
        }

        var table = MarkdownText.Table(["Phase", "Name", "Delivery AIU", "Planning AIU", "Coordination AIU", "Fix AIU", "Total AIU", "Calls"], rows);
        var waived = ledger.Waivers.Where(waiver => waiver.Phase is not null).ToList();
        var note = waived.Count == 0
            ? string.Empty
            : $"\n{string.Join("\n", waived.Select(waiver => $"- Phase {waiver.Phase} is recorded at phase level: {MarkdownText.Cell(waiver.Reason, 400)}{PhaseChange(ledger, waiver.Phase!.Value)}"))}\n";

        return $"\n## By phase\n\nDelivery is the work of a work package. Planning is the work on the plan itself, and coordination is a session that starts, monitors, or hands over other sessions.\n\n{table}{note}";
    }

    private static string PhaseChange(UsageLedger ledger, PhaseId phase)
    {
        var change = ledger.Changes.FirstOrDefault(item => item.Phase == phase);

        return change is null
            ? string.Empty
            : $" Its pull requests changed {UsageAnalysis.Number(change.Files)} files, with {UsageAnalysis.Number(change.Additions)} additions and {UsageAnalysis.Number(change.Deletions)} deletions.";
    }

    private static string ByWorkPackage(UsageLedger ledger, StatusDocument status)
    {
        var costs = UsageAnalysis.CompletedCosts(ledger, status);

        var rows = costs.Select<WorkPackageCost, IReadOnlyList<string>>(cost =>
        [
            cost.Id,
            cost.Size,
            MarkdownText.Cell(cost.Title),
            UsageAnalysis.Number(cost.Aiu),
            UsageAnalysis.Number(cost.Calls),
            UsageAnalysis.Number(cost.ModelSeconds / 60m),
            UsageAnalysis.Number(cost.PeakContextTokens),
            UsageAnalysis.Number(cost.Compactions),
            UsageAnalysis.Number(cost.UserTurns),
            cost.Change is null ? "n/a" : UsageAnalysis.Number(cost.Change.ChangedLines),
            cost.AiuPerThousandLines is { } perLines ? UsageAnalysis.Number(perLines) : "n/a",
            cost.Confidence,
        ]);

        var table = MarkdownText.Table(
            ["Work package", "Size", "Title", "AIU", "Calls", "Model minutes", "Peak context", "Compactions", "User turns", "Changed lines", "AIU per 1,000 lines", "Confidence"],
            rows);

        return $"\n## By work package\n\nThe completed work packages and what it cost to deliver each. Changed lines are the additions and deletions of its pull request. Exact means that one session held the work package alone, and windowed means that its window was derived from a session that held more.\n\n{table}";
    }

    private static string BySize(UsageLedger ledger, StatusDocument status)
    {
        var estimate = UsageEstimator.Estimate(ledger, status);

        if (estimate.Sizes.Count == 0)
        {
            return "\n## Observed AIU by size class\n\nNo work package is completed with delivery usage.\n";
        }

        var rows = estimate.Sizes.Select<SizeStats, IReadOnlyList<string>>(stats =>
        [
            stats.Size,
            UsageAnalysis.Number(stats.Count),
            UsageAnalysis.Number(stats.Min),
            UsageAnalysis.Number(stats.Median),
            UsageAnalysis.Number(stats.Max),
            UsageAnalysis.Number(stats.Median / UsageVocabulary.Weight(stats.Size)),
        ]);

        var table = MarkdownText.Table(["Size", "Completed", "Minimum AIU", "Median AIU", "Maximum AIU", "Median AIU per day of weight"], rows);
        return $"\n## Observed AIU by size class\n\nThe size classes are the focused effort of plan section 5.7, and the weights are their midpoints in days: S 0.5, M 3, and L 6.5. `governance usage estimate` turns these numbers into a range for each work package that remains.\n\n{table}";
    }

    private static string ByModel(UsageLedger ledger)
    {
        var groups = ledger.Records
            .SelectMany(record => record.Usage)
            .GroupBy(row => (row.Model, row.ReasoningEffort, row.Agent))
            .Select(group => new
            {
                group.Key.Model,
                group.Key.ReasoningEffort,
                group.Key.Agent,
                Calls = group.Sum(row => (long)row.Calls),
                Aiu = group.Sum(row => row.Aiu),
                Fresh = group.Sum(row => row.FreshInputTokens),
                CacheRead = group.Sum(row => row.CacheReadTokens),
                CacheWrite = group.Sum(row => row.CacheWriteTokens),
                Output = group.Sum(row => row.OutputTokens),
                Reasoning = group.Sum(row => row.ReasoningTokens),
            })
            .OrderByDescending(group => group.Aiu)
            .ThenBy(group => group.Model, StringComparer.Ordinal)
            .ThenBy(group => group.ReasoningEffort, StringComparer.Ordinal)
            .ThenBy(group => group.Agent, StringComparer.Ordinal)
            .ToList();

        var total = groups.Sum(group => group.Aiu);
        List<IReadOnlyList<string>> rows = [];
        foreach (var group in groups)
        {
            rows.Add(
            [
                MarkdownText.Code(group.Model),
                group.ReasoningEffort,
                group.Agent,
                UsageAnalysis.Number(group.Calls),
                UsageAnalysis.Number(group.Aiu),
                UsageAnalysis.Percent(total == 0 ? 0m : group.Aiu / total),
                UsageAnalysis.Number(group.Fresh),
                UsageAnalysis.Number(group.CacheRead),
                UsageAnalysis.Number(group.CacheWrite),
                UsageAnalysis.Number(group.Output),
                UsageAnalysis.Number(group.Reasoning),
            ]);
        }

        var table = MarkdownText.Table(
            ["Model", "Effort", "Agent", "Calls", "AIU", "Share", "Fresh input", "Cache read", "Cache write", "Output", "Reasoning"],
            rows);

        return $"\n## By model and effort\n\nTokens are the sums over the calls. Fresh input is the input that was not served from the cache, and reasoning tokens are part of the output tokens. A sub-agent is a helper that the main agent started, often with a different model. Effort is unspecified for the calls that compact a conversation.\n\n{table}";
    }

    private static string BySession(UsageLedger ledger)
    {
        List<IReadOnlyList<string>> rows = [];

        foreach (var item in ledger.Records
            .GroupBy(record => record.Session, StringComparer.Ordinal)
            .Select(group => new
            {
                Session = group.Key,
                Slices = group.Count(),
                Calls = group.Sum(record => (long)record.Calls),
                Aiu = group.Sum(record => record.Aiu),
                Peak = group.Max(record => record.PeakContextTokens),
                Compactions = group.Sum(record => record.Compactions),
                First = group.Min(record => record.Window.From),
                Last = group.Max(record => record.Window.To),
            })
            .OrderBy(item => item.First)
            .ThenBy(item => item.Session, StringComparer.Ordinal))
        {
            rows.Add(
            [
                MarkdownText.Code(Short(item.Session)),
                UsageAnalysis.Number(item.Slices),
                UsageAnalysis.Number(item.Calls),
                UsageAnalysis.Number(item.Aiu),
                UsageAnalysis.Number(item.Peak),
                UsageAnalysis.Number(item.Compactions),
                UsageAnalysis.Day(item.First),
                UsageAnalysis.Day(item.Last),
            ]);
        }

        var table = MarkdownText.Table(["Session", "Slices", "Calls", "AIU", "Peak context", "Compactions", "First call", "Last call"], rows);
        return $"\n## By session\n\nA session is an opaque ID, shown by its first eight characters. A session that spans days, or that holds several work packages, appears once here and in several slices above.\n\n{table}";
    }

    private static string Short(string session) => session.Length > 8 ? session[..8] : session;
}
