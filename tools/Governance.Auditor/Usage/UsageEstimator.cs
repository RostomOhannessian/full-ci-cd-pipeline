using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Governance.Auditor.Status;

namespace Governance.Auditor.Usage;

/// <summary>How the AIU of the completed work packages of one size class are spread.</summary>
internal sealed record SizeStats(string Size, int Count, decimal Min, decimal Median, decimal Max);

/// <summary>The AIU for each day of weight across all the completed work packages.</summary>
internal sealed record RateStats(decimal Min, decimal Median, decimal Max);

internal sealed record ModelShare(string Model, string ReasoningEffort, decimal Aiu, decimal Share);

internal sealed record RemainingEstimate(string Id, string Title, string Size, WorkState State, string Method, int Observations, decimal Low, decimal Mid, decimal High);

internal sealed record PhaseEstimate(PhaseId Phase, string Name, IReadOnlyList<RemainingEstimate> WorkPackages, decimal Low, decimal Mid, decimal High);

internal sealed record UsageEstimate(
    IReadOnlyList<WorkPackageCost> Observed,
    IReadOnlyList<SizeStats> Sizes,
    RateStats? Rate,
    IReadOnlyList<PhaseEstimate> Phases,
    decimal NonDeliveryShare,
    IReadOnlyList<ModelShare> Mix,
    IReadOnlyList<string> Caveats);

/// <summary>
/// Turns the completed work packages into a range of AIU for each work package that remains. The method is fixed and has no randomness:
/// a size class that has completed work packages is estimated from their minimum, median, and maximum, and a size class that has none is
/// scaled from the AIU for each day of weight of the others. Weights are the midpoints of the focused effort in plan section 5.7.
/// </summary>
internal static class UsageEstimator
{
    public const string BySize = "by-size";
    public const string ScaledByWeight = "scaled-by-weight";

    public static UsageEstimate Estimate(UsageLedger ledger, StatusDocument status, PhaseId? onlyPhase = null)
    {
        var observed = UsageAnalysis.CompletedCosts(ledger, status);

        var sizes = UsageVocabulary.Sizes
            .Select(size => observed.Where(cost => cost.Size == size).Select(cost => cost.Aiu).ToList())
            .Select((values, index) => values.Count == 0 ? null : new SizeStats(UsageVocabulary.Sizes[index], values.Count, values.Min(), UsageAnalysis.Median(values), values.Max()))
            .OfType<SizeStats>()
            .ToList();

        RateStats? rate = observed.Count == 0
            ? null
            : new RateStats(observed.Min(cost => cost.AiuPerWeight), UsageAnalysis.Median(observed.Select(cost => cost.AiuPerWeight)), observed.Max(cost => cost.AiuPerWeight));

        List<PhaseEstimate> phases = [];

        if (observed.Count > 0)
        {
            foreach (var phase in status.Phases.Where(phase => onlyPhase is null || phase.Id == onlyPhase))
            {
                var remaining = phase.WorkPackages
                    .Where(workPackage => workPackage.State != WorkState.Completed)
                    .Select(workPackage => EstimateOne(workPackage, sizes, rate!))
                    .ToList();

                if (remaining.Count > 0)
                {
                    phases.Add(new PhaseEstimate(phase.Id, phase.Name, remaining, remaining.Sum(item => item.Low), remaining.Sum(item => item.Mid), remaining.Sum(item => item.High)));
                }
            }
        }

        var total = ledger.Records.Sum(record => record.Aiu);
        var nonDelivery = total == 0 ? 0m : ledger.Records.Where(record => record.Activity != UsageVocabulary.Delivery).Sum(record => record.Aiu) / total;

        var mix = observed
            .SelectMany(cost => ledger.DeliveryOf(cost.Id))
            .SelectMany(record => record.Usage)
            .GroupBy(row => (row.Model, row.ReasoningEffort))
            .Select(group => new { group.Key.Model, group.Key.ReasoningEffort, Aiu = group.Sum(row => row.Aiu) })
            .OrderByDescending(item => item.Aiu)
            .ThenBy(item => item.Model, StringComparer.Ordinal)
            .ThenBy(item => item.ReasoningEffort, StringComparer.Ordinal)
            .ToList();

        var mixTotal = mix.Sum(item => item.Aiu);
        var shares = mix.Select(item => new ModelShare(item.Model, item.ReasoningEffort, item.Aiu, mixTotal == 0 ? 0m : item.Aiu / mixTotal)).ToList();

        return new UsageEstimate(observed, sizes, rate, phases, nonDelivery, shares, Caveats(observed, sizes, phases, shares, nonDelivery));
    }

    private static RemainingEstimate EstimateOne(WorkPackageInfo workPackage, IReadOnlyList<SizeStats> sizes, RateStats rate)
    {
        if (sizes.FirstOrDefault(stats => stats.Size == workPackage.Size) is { } known)
        {
            return new RemainingEstimate(workPackage.Id, workPackage.Title, workPackage.Size, workPackage.State, BySize, known.Count, known.Min, known.Median, known.Max);
        }

        var weight = UsageVocabulary.Weight(workPackage.Size);
        return new RemainingEstimate(workPackage.Id, workPackage.Title, workPackage.Size, workPackage.State, ScaledByWeight, 0, weight * rate.Min, weight * rate.Median, weight * rate.Max);
    }

    private static List<string> Caveats(
        IReadOnlyList<WorkPackageCost> observed,
        IReadOnlyList<SizeStats> sizes,
        IReadOnlyList<PhaseEstimate> phases,
        List<ModelShare> mix,
        decimal nonDeliveryShare)
    {
        List<string> caveats = [];

        if (observed.Count == 0)
        {
            caveats.Add("No work package is completed with delivery usage in the ledger, so there is nothing to estimate from. Record the usage of the completed work packages first.");
            return caveats;
        }

        caveats.Add($"Only {Count(observed.Count, "completed work package")} {(observed.Count == 1 ? "has" : "have")} delivery usage, and no size class has more than {sizes.Max(stats => stats.Count).ToString(CultureInfo.InvariantCulture)} of them. A range is a planning aid built on very few observations, and it is not a promise.");

        var missing = UsageVocabulary.Sizes.Where(size => sizes.All(stats => stats.Size != size)).ToList();

        if (missing.Count > 0 && phases.SelectMany(phase => phase.WorkPackages).Any(item => item.Method == ScaledByWeight))
        {
            caveats.Add($"No completed work package has the size {string.Join(" or ", missing)}, so those packages are scaled from the median AIU for each day of weight of the other sizes. They are the least reliable lines.");
        }
        else if (missing.Count > 0)
        {
            caveats.Add($"No completed work package has the size {string.Join(" or ", missing)}.");
        }

        caveats.Add("AIU does not grow with the days of a size class. A session pays to read the repository and to rewrite its cache whatever the size, so the small packages cost far more for each day of weight than the large ones. The estimate therefore uses the observed AIU of a size class and not a multiple of a day.");

        if (mix.Count > 0)
        {
            var top = mix[0];
            caveats.Add($"The estimate assumes the same models and efforts as the work that it is built from, which was {UsageAnalysis.Percent(top.Share)} {top.Model} at {top.ReasoningEffort} effort. A different model, a lower effort, or a different price per token changes the AIU of the same work by a large factor.");
        }

        caveats.Add("A request multiplier is recorded for some models but it does not scale the AIU, which the store computes from the token counts and the per-token price of each call (ADR-0031). A model with a multiplier is expensive because of its price per token.");

        var windowed = observed.Count(cost => cost.Confidence == UsageVocabulary.Windowed);

        if (windowed > 0)
        {
            caveats.Add($"Of the {observed.Count.ToString(CultureInfo.InvariantCulture)} observed work packages, {windowed.ToString(CultureInfo.InvariantCulture)} {(windowed == 1 ? "is" : "are")} windowed. A windowed work package shared its session with other work, and its window is derived from the user turns, the pull request times, and the commit times, so its numbers could be off by the calls at the edges of the window.");
        }

        caveats.Add($"Planning, coordination, and fixes were {UsageAnalysis.Percent(nonDeliveryShare)} of the AIU in the ledger, and they are not part of a work package. Add that share when you budget a phase.");

        return caveats;
    }

    public static string RenderText(UsageEstimate estimate)
    {
        var builder = new StringBuilder();
        builder.Append("usage estimate: AI units (AIU) for the work packages that remain\n");
        builder.Append("  method:\n");
        builder.Append("    - Observe each completed work package: the AIU of its delivery slices in the ledger.\n");
        builder.Append("    - A size class with completed work packages is estimated from their minimum, median, and maximum AIU.\n");
        builder.Append("    - A size class with none is scaled from the AIU for each day of weight of all completed work packages. The weights are the midpoints of plan section 5.7: S 0.5, M 3, and L 6.5 days.\n");
        builder.Append("    - A phase is the sum of its work packages. The command is deterministic, reads the ledger and status.yaml only, and never blocks a merge.\n");
        builder.Append($"  observed: {Count(estimate.Observed.Count, "completed work package")} with delivery usage");

        if (estimate.Observed.Count > 0)
        {
            builder.Append($" ({string.Join(", ", estimate.Observed.Select(cost => cost.Id))})");
        }

        builder.Append('\n');

        if (estimate.Sizes.Count > 0)
        {
            builder.Append("  by size (observed AIU: n, minimum, median, maximum):\n");

            foreach (var stats in estimate.Sizes)
            {
                builder.Append($"    {stats.Size}: n {stats.Count}, min {UsageAnalysis.Number(stats.Min)}, median {UsageAnalysis.Number(stats.Median)}, max {UsageAnalysis.Number(stats.Max)}\n");
            }

            foreach (var size in UsageVocabulary.Sizes.Where(size => estimate.Sizes.All(stats => stats.Size != size)))
            {
                builder.Append($"    {size}: no completed work package of this size\n");
            }
        }

        if (estimate.Rate is { } rate)
        {
            builder.Append($"  AIU for each day of weight: min {UsageAnalysis.Number(rate.Min)}, median {UsageAnalysis.Number(rate.Median)}, max {UsageAnalysis.Number(rate.Max)}\n");
        }

        foreach (var phase in estimate.Phases)
        {
            builder.Append($"  phase {phase.Phase}: {phase.Name}\n");

            foreach (var item in phase.WorkPackages)
            {
                var basis = item.Method == BySize
                    ? item.Observations == 1
                        ? "by size, 1 observed, so the range is a single point"
                        : $"by size, {item.Observations.ToString(CultureInfo.InvariantCulture)} observed"
                    : "scaled by weight, no work package of this size is completed";
                builder.Append($"    {item.Id} ({item.Size}, {StateWord(item.State)}): {UsageAnalysis.Number(item.Low)} to {UsageAnalysis.Number(item.High)}, median {UsageAnalysis.Number(item.Mid)} ({basis})\n");
            }

            builder.Append($"    total: {UsageAnalysis.Number(phase.Low)} to {UsageAnalysis.Number(phase.High)}, sum of medians {UsageAnalysis.Number(phase.Mid)}\n");
        }

        if (estimate.Observed.Count > 0 && estimate.Phases.Count == 0)
        {
            builder.Append("  no work package remains in the phases that you asked for\n");
        }

        builder.Append("  caveats:\n");

        foreach (var caveat in estimate.Caveats)
        {
            builder.Append($"    - {caveat}\n");
        }

        return builder.ToString().TrimEnd('\n');
    }

    private static string StateWord(WorkState state) => state == WorkState.InProgress ? "in progress" : state.ToString().ToLowerInvariant();

    private static string Count(int number, string noun) => $"{number.ToString(CultureInfo.InvariantCulture)} {noun}{(number == 1 ? string.Empty : "s")}";

    private static readonly JsonSerializerOptions CompactJson = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static decimal Round(decimal value) => Math.Round(value, 1, MidpointRounding.AwayFromZero);

    public static string ToJson(UsageEstimate estimate) => JsonSerializer.Serialize(
        new
        {
            unit = "AIU",
            weights = UsageVocabulary.Sizes.ToDictionary(size => size, UsageVocabulary.Weight),
            observed = estimate.Observed.Select(cost => new
            {
                id = cost.Id,
                size = cost.Size,
                aiu = Round(cost.Aiu),
                calls = cost.Calls,
                confidence = cost.Confidence,
            }),
            sizes = estimate.Sizes.Select(stats => new { size = stats.Size, count = stats.Count, min = Round(stats.Min), median = Round(stats.Median), max = Round(stats.Max) }),
            aiuPerWeight = estimate.Rate is null ? null : new { min = Round(estimate.Rate.Min), median = Round(estimate.Rate.Median), max = Round(estimate.Rate.Max) },
            phases = estimate.Phases.Select(phase => new
            {
                phase = phase.Phase.ToString(),
                name = phase.Name,
                low = Round(phase.Low),
                median = Round(phase.Mid),
                high = Round(phase.High),
                workPackages = phase.WorkPackages.Select(item => new
                {
                    id = item.Id,
                    size = item.Size,
                    state = StateWord(item.State),
                    method = item.Method,
                    observations = item.Observations,
                    low = Round(item.Low),
                    median = Round(item.Mid),
                    high = Round(item.High),
                }),
            }),
            nonDeliveryShare = Math.Round(estimate.NonDeliveryShare, 4, MidpointRounding.AwayFromZero),
            caveats = estimate.Caveats,
        },
        CompactJson);
}
