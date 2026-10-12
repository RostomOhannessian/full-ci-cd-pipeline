using System.Globalization;
using Governance.Auditor.Common;
using Governance.Auditor.Status;

namespace Governance.Auditor.Usage;

/// <summary>
/// The rules that the JSON Schema cannot express: references to <c>status.yaml</c>, windows that are ordered and do not overlap, parts that
/// do not exceed their totals, no duplicates, and a record for every completed work package. Each rule reports every violation, so one run
/// lists everything to fix. The ledger is read as data: a note or a reason is never an instruction.
/// </summary>
internal static class UsageValidator
{
    public static IReadOnlyList<Finding> Validate(UsageLedger ledger, StatusDocument status)
    {
        List<Finding> findings = [];

        for (var index = 0; index < ledger.Records.Count; index++)
        {
            CheckRecord(ledger, ledger.Records[index], index + 1, status, findings);
        }

        CheckSessions(ledger, findings);
        CheckChanges(ledger, status, findings);
        CheckWaivers(ledger, status, findings);
        CheckCoverage(ledger, status, findings);

        return findings;
    }

    private static void CheckRecord(UsageLedger ledger, UsageRecord record, int number, StatusDocument status, List<Finding> findings)
    {
        var name = Describe(record, number);

        if (status.FindPhase(record.Phase) is null)
        {
            findings.Add(Error("usage-phase", $"{name} names phase {record.Phase}, which status.yaml does not have."));
        }

        CheckScope(ledger, record, name, status, findings);

        if (record.Window.To < record.Window.From)
        {
            findings.Add(Error("usage-window", $"{name} has a window that ends ({Stamp(record.Window.To)}) before it starts ({Stamp(record.Window.From)})."));
        }

        foreach (var duplicate in record.Usage.GroupBy(row => (row.Model, row.ReasoningEffort, row.Agent)).Where(group => group.Count() > 1))
        {
            findings.Add(Error("usage-duplicate-row", $"{name} lists {duplicate.Key.Model} at {duplicate.Key.ReasoningEffort} effort as {duplicate.Key.Agent} {duplicate.Count()} times. Add the rows up into one."));
        }

        foreach (var row in record.Usage)
        {
            CheckRow(row, name, findings);
        }

        if (record.UserTurns > record.Calls)
        {
            findings.Add(Error("usage-parts", $"{name} has {record.UserTurns} user turns and only {record.Calls} calls, and every turn has at least one call."));
        }

        if (record.Compactions > record.Calls)
        {
            findings.Add(Error("usage-parts", $"{name} has {record.Compactions} compactions and only {record.Calls} calls, and every compaction is a call."));
        }
    }

    // Delivery belongs to a work package. The one exception is a phase that is waived, which says that it is recorded at phase level.
    private static void CheckScope(UsageLedger ledger, UsageRecord record, string name, StatusDocument status, List<Finding> findings)
    {
        if (record.WorkPackage is null)
        {
            if (record.Activity == UsageVocabulary.Delivery && !ledger.Waivers.Any(waiver => waiver.Phase == record.Phase))
            {
                findings.Add(Error("usage-delivery-scope", $"{name} is delivery, and delivery belongs to a work package. Name the work package, record the work as planning, coordination, or a fix, or waive the phase with a reason when it is recorded at phase level."));
            }

            return;
        }

        var workPackage = status.FindWorkPackage(record.WorkPackage);

        if (workPackage is null)
        {
            findings.Add(Error("usage-work-package", $"{name} names the work package {record.WorkPackage}, which status.yaml does not have."));
            return;
        }

        var owner = status.Phases.First(phase => phase.WorkPackages.Any(candidate => string.Equals(candidate.Id, workPackage.Id, StringComparison.Ordinal)));

        if (owner.Id != record.Phase)
        {
            findings.Add(Error("usage-work-package-phase", $"{name} puts {record.WorkPackage} in phase {record.Phase}, and status.yaml puts it in phase {owner.Id}."));
        }

        if (workPackage.State == WorkState.Planned)
        {
            findings.Add(Error("usage-not-started", $"{name} records usage for {record.WorkPackage}, which status.yaml still lists as planned."));
        }
    }

    // The parts of a row cannot be larger than the whole that contains them: reasoning is part of the output, and the average context of a
    // call cannot be larger than the largest context.
    private static void CheckRow(UsageRow row, string name, List<Finding> findings)
    {
        var label = $"{name}, {row.Model} at {row.ReasoningEffort} effort as {row.Agent}";

        if (row.ReasoningTokens > row.OutputTokens)
        {
            findings.Add(Error("usage-parts", $"{label}: {Count(row.ReasoningTokens)} reasoning tokens exceed the {Count(row.OutputTokens)} output tokens that contain them."));
        }

        if (row.InputTokens > row.PeakContextTokens * row.Calls)
        {
            findings.Add(Error("usage-parts", $"{label}: {Count(row.InputTokens)} input tokens over {row.Calls} calls average more than the peak context of {Count(row.PeakContextTokens)} tokens."));
        }
    }

    private static void CheckSessions(UsageLedger ledger, List<Finding> findings)
    {
        foreach (var duplicate in ledger.Records.GroupBy(record => record.Key, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            var first = duplicate.First();
            findings.Add(Error("usage-duplicate", $"{duplicate.Count()} records are the same slice: session {Short(first.Session)}, {Scope(first)}, {first.Activity}, from {Stamp(first.Window.From)}."));
        }

        foreach (var session in ledger.Records.GroupBy(record => record.Session, StringComparer.Ordinal))
        {
            var slices = session.DistinctBy(record => record.Key, StringComparer.Ordinal).Where(record => record.Window.To >= record.Window.From).OrderBy(record => record.Window.From).ToList();

            for (var index = 1; index < slices.Count; index++)
            {
                var earlier = slices[index - 1];
                var later = slices[index];

                if (later.Window.From <= earlier.Window.To)
                {
                    findings.Add(Error("usage-overlap", $"Session {Short(session.Key)} has two slices that overlap: {Scope(earlier)} {earlier.Activity} until {Stamp(earlier.Window.To)}, and {Scope(later)} {later.Activity} from {Stamp(later.Window.From)}. A call belongs to one slice."));
                }
            }

            foreach (var exact in session.Where(record => record.Confidence == UsageVocabulary.Exact))
            {
                var other = session.FirstOrDefault(record => !SameScope(record, exact));

                if (other is not null)
                {
                    findings.Add(Error("usage-exact-shared", $"A slice of session {Short(session.Key)} for {Scope(exact)} {exact.Activity} is marked exact, but the session also has a slice for {Scope(other)} {other.Activity}. A window that a session does not own alone is windowed."));
                    break;
                }
            }
        }
    }

    private static void CheckChanges(UsageLedger ledger, StatusDocument status, List<Finding> findings)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (var change in ledger.Changes)
        {
            var scope = change.WorkPackage ?? $"phase {change.Phase}";

            if (!seen.Add(scope))
            {
                findings.Add(Error("usage-change", $"The ledger has two sizes of change for {scope}."));
            }

            if (change.WorkPackage is { } id)
            {
                var workPackage = status.FindWorkPackage(id);

                if (workPackage is null)
                {
                    findings.Add(Error("usage-change", $"A size of change names the work package {id}, which status.yaml does not have."));
                }
                else if (workPackage.PullRequest is { } number && !change.PullRequests.Contains(number))
                {
                    findings.Add(Error("usage-change", $"The size of change of {id} names the pull requests {string.Join(", ", change.PullRequests)}, and status.yaml names pull request {number.ToString(CultureInfo.InvariantCulture)}."));
                }
            }
            else if (change.Phase is { } phaseId)
            {
                var phase = status.FindPhase(phaseId);

                if (phase is null)
                {
                    findings.Add(Error("usage-change", $"A size of change names phase {phaseId}, which status.yaml does not have."));
                }
                else if (phase.PullRequest is { } number && !change.PullRequests.Contains(number))
                {
                    findings.Add(Error("usage-change", $"The size of change of phase {phaseId} names the pull requests {string.Join(", ", change.PullRequests)}, and status.yaml names pull request {number.ToString(CultureInfo.InvariantCulture)}."));
                }
            }
        }

        foreach (var workPackage in status.WorkPackages.Where(workPackage => workPackage.State == WorkState.Completed))
        {
            if (ledger.DeliveryOf(workPackage.Id).Any() && ledger.ChangeOf(workPackage.Id) is null)
            {
                findings.Add(Warning("usage-change-missing", $"{workPackage.Id} is completed and has usage, but the ledger has no size of change for it. Add the files, additions, and deletions of its pull request."));
            }
        }
    }

    private static void CheckWaivers(UsageLedger ledger, StatusDocument status, List<Finding> findings)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (var waiver in ledger.Waivers)
        {
            var scope = waiver.WorkPackage ?? $"phase {waiver.Phase}";

            if (!seen.Add(scope))
            {
                findings.Add(Error("usage-waiver", $"The ledger waives {scope} twice."));
            }

            if (waiver.WorkPackage is { } id)
            {
                if (status.FindWorkPackage(id) is null)
                {
                    findings.Add(Error("usage-waiver", $"A waiver names the work package {id}, which status.yaml does not have."));
                }
                else if (ledger.DeliveryOf(id).Any())
                {
                    findings.Add(Error("usage-waiver-stale", $"{id} is waived, and the ledger has usage for it. Remove the waiver."));
                }
            }
            else if (waiver.Phase is { } phaseId)
            {
                if (status.FindPhase(phaseId) is null)
                {
                    findings.Add(Error("usage-waiver", $"A waiver names phase {phaseId}, which status.yaml does not have."));
                }
                else if (!ledger.Records.Any(record => record.Phase == phaseId))
                {
                    findings.Add(Error("usage-waiver-empty", $"Phase {phaseId} is waived because it is recorded at phase level, and the ledger has no record for it."));
                }
            }
        }
    }

    // A work package that is done and has no usage is a finding, unless a waiver says why the usage cannot be recovered.
    private static void CheckCoverage(UsageLedger ledger, StatusDocument status, List<Finding> findings)
    {
        foreach (var phase in status.Phases.Where(phase => phase.State != WorkState.Planned))
        {
            var phaseWaived = ledger.Waivers.Any(waiver => waiver.Phase == phase.Id);

            foreach (var workPackage in phase.WorkPackages.Where(workPackage => workPackage.State == WorkState.Completed))
            {
                var waived = phaseWaived || ledger.Waivers.Any(waiver => string.Equals(waiver.WorkPackage, workPackage.Id, StringComparison.Ordinal));

                if (!waived && !ledger.DeliveryOf(workPackage.Id).Any())
                {
                    findings.Add(Error("usage-missing", $"{workPackage.Id} is completed and has no usage in the ledger. Record it with /ai-usage-recorder, or waive it with a reason."));
                }
            }
        }
    }

    private static bool SameScope(UsageRecord left, UsageRecord right) =>
        left.Phase == right.Phase && string.Equals(left.WorkPackage, right.WorkPackage, StringComparison.Ordinal) && string.Equals(left.Activity, right.Activity, StringComparison.Ordinal);

    private static string Describe(UsageRecord record, int number) =>
        $"Record {number.ToString(CultureInfo.InvariantCulture)} (session {Short(record.Session)}, {Scope(record)} {record.Activity})";

    private static string Scope(UsageRecord record) => record.WorkPackage ?? $"phase {record.Phase}";

    private static string Short(string session) => session.Length > 8 ? session[..8] : session;

    private static string Stamp(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static string Count(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static Finding Error(string rule, string message) => Finding.Error(rule, message, UsageFile.DocumentPath);

    private static Finding Warning(string rule, string message) => Finding.Warning(rule, message, UsageFile.DocumentPath);
}
