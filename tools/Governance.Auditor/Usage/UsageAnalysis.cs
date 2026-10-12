using System.Globalization;
using Governance.Auditor.Status;

namespace Governance.Auditor.Usage;

/// <summary>What one completed work package cost to deliver: the sum of its delivery slices.</summary>
internal sealed record WorkPackageCost(
    string Id,
    string Title,
    string Size,
    PhaseId Phase,
    decimal Aiu,
    int Calls,
    long ModelSeconds,
    long PeakContextTokens,
    int Compactions,
    int UserTurns,
    string Confidence,
    UsageChange? Change)
{
    /// <summary>The AIU for each day of weight of the size class (plan section 5.7).</summary>
    public decimal AiuPerWeight => Aiu / UsageVocabulary.Weight(Size);

    public decimal? AiuPerThousandLines => Change is { ChangedLines: > 0 } ? Aiu * 1000m / Change.ChangedLines : null;
}

internal static class UsageAnalysis
{
    /// <summary>The completed work packages that have delivery usage, in the order of <c>status.yaml</c>.</summary>
    public static IReadOnlyList<WorkPackageCost> CompletedCosts(UsageLedger ledger, StatusDocument status)
    {
        List<WorkPackageCost> costs = [];

        foreach (var phase in status.Phases)
        {
            foreach (var workPackage in phase.WorkPackages.Where(workPackage => workPackage.State == WorkState.Completed))
            {
                var slices = ledger.DeliveryOf(workPackage.Id).ToList();

                if (slices.Count == 0)
                {
                    continue;
                }

                costs.Add(new WorkPackageCost(
                    workPackage.Id,
                    workPackage.Title,
                    workPackage.Size,
                    phase.Id,
                    slices.Sum(slice => slice.Aiu),
                    slices.Sum(slice => slice.Calls),
                    slices.Sum(slice => slice.Usage.Sum(row => row.ModelSeconds)),
                    slices.Max(slice => slice.PeakContextTokens),
                    slices.Sum(slice => slice.Compactions),
                    slices.Sum(slice => slice.UserTurns),
                    slices.All(slice => slice.Confidence == UsageVocabulary.Exact) ? UsageVocabulary.Exact : UsageVocabulary.Windowed,
                    ledger.ChangeOf(workPackage.Id)));
            }
        }

        return costs;
    }

    /// <summary>The median of the values: the middle one, or the mean of the two in the middle. The values need not be sorted.</summary>
    public static decimal Median(IEnumerable<decimal> values)
    {
        var sorted = values.Order().ToList();

        if (sorted.Count == 0)
        {
            throw new InvalidOperationException("The median of no values is not defined.");
        }

        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2m;
    }

    public static string Number(decimal value) => Math.Round(value, 0, MidpointRounding.AwayFromZero).ToString("N0", CultureInfo.InvariantCulture);

    public static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    public static string Percent(decimal share) => (share * 100m).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    public static string PhaseName(StatusDocument status, PhaseId phase) => status.FindPhase(phase)?.Name ?? phase.ToString();

    public static string Day(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
