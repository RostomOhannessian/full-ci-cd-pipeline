using System.Text.RegularExpressions;
using Governance.Auditor.Common;
using Governance.Auditor.Status;

namespace Governance.Auditor.Trace;

/// <summary>Which requirements must have proof for the check to pass.</summary>
/// <param name="All">Treat every active requirement as in scope. Used to list what is still missing.</param>
/// <param name="ThroughPhase">Treat every requirement that the phase and the phases before it deliver as in scope. Used at a phase exit gate.</param>
internal sealed record TraceOptions(bool All = false, PhaseId? ThroughPhase = null);

internal enum CoverageState
{
    Covered,
    Missing,
    NotDue,
    Deferred,
    Withdrawn,
}

internal sealed record RequirementCoverage(Requirement Requirement, bool InScope, IReadOnlyList<TestReference> Tests, IReadOnlyList<EvidenceEntry> Evidence)
{
    public bool HasProof => Tests.Count > 0 || Evidence.Count > 0;

    public CoverageState State => Requirement.Status switch
    {
        "deferred" => CoverageState.Deferred,
        "withdrawn" => CoverageState.Withdrawn,
        _ when HasProof => CoverageState.Covered,
        _ when InScope => CoverageState.Missing,
        _ => CoverageState.NotDue,
    };
}

internal sealed record TraceResult(IReadOnlyList<RequirementCoverage> Coverage, IReadOnlyList<Finding> Findings);

/// <summary>
/// Decides which requirements are in scope and whether each one has proof. By default a requirement is in scope once every work
/// package that delivers it is completed, or when its status already says it is verified. A requirement is proven by a test that carries
/// its ID, or by an entry in the evidence register whose file exists.
/// </summary>
internal static partial class TraceAnalyzer
{
    public const string MissingProof = "trace-missing-proof";

    private const string LaunchWorkPackage = "LAUNCH";

    [GeneratedRegex(@"^REQ-[A-Z]+-\d{3}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RequirementIdPattern();

    public static TraceResult Analyze(
        RequirementsRegister requirements,
        StatusDocument status,
        IReadOnlyList<TestReference> tests,
        EvidenceRegister evidence,
        RepositoryFiles files,
        TraceOptions options)
    {
        List<Finding> findings = [];
        var known = requirements.Requirements.Select(requirement => requirement.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var duplicate in requirements.Requirements.GroupBy(requirement => requirement.Id, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            findings.Add(Finding.Error("trace-duplicate-requirement", $"{duplicate.Key} appears {duplicate.Count()} times in the requirements register.", TraceFiles.RequirementsPath));
        }

        foreach (var test in tests.Where(test => !known.Contains(test.Requirement)))
        {
            var reason = RequirementIdPattern().IsMatch(test.Requirement) ? "is not in the requirements register" : "is not a valid requirement ID (REQ-<AREA>-<NNN>)";
            findings.Add(Finding.Error("trace-unknown-requirement", $"The test names '{test.Requirement}', which {reason}.", test.Path, test.Line));
        }

        foreach (var entry in evidence.Entries)
        {
            foreach (var requirement in entry.Requirements.Where(requirement => !known.Contains(requirement)))
            {
                findings.Add(Finding.Error("trace-unknown-requirement", $"The evidence entry for '{entry.Path}' names '{requirement}', which is not in the requirements register.", TraceFiles.EvidencePath));
            }

            if (!PathExists(files, entry.Path))
            {
                findings.Add(Finding.Error("trace-evidence-path", $"The evidence file '{entry.Path}' does not exist. Evidence must point to a real file, or to a directory that ends with '/'.", TraceFiles.EvidencePath));
            }
        }

        var coverage = requirements.Requirements
            .Select(requirement => Cover(requirement, status, tests, evidence, options, findings))
            .ToList();

        foreach (var item in coverage)
        {
            Judge(item, status, findings);
        }

        return new TraceResult(coverage, findings);
    }

    private static RequirementCoverage Cover(
        Requirement requirement,
        StatusDocument status,
        IReadOnlyList<TestReference> tests,
        EvidenceRegister evidence,
        TraceOptions options,
        List<Finding> findings)
    {
        foreach (var workPackage in requirement.DeliveredBy.Where(id => !IsKnownWorkPackage(status, id)))
        {
            findings.Add(Finding.Error("trace-unknown-work-package", $"{requirement.Id} is delivered by {workPackage}, which is not in the status file.", TraceFiles.RequirementsPath));
        }

        var requirementTests = tests.Where(test => string.Equals(test.Requirement, requirement.Id, StringComparison.Ordinal)).ToList();
        var requirementEvidence = evidence.Entries.Where(entry => entry.Requirements.Contains(requirement.Id, StringComparer.Ordinal)).ToList();

        return new RequirementCoverage(requirement, IsInScope(requirement, status, options), requirementTests, requirementEvidence);
    }

    private static void Judge(RequirementCoverage item, StatusDocument status, List<Finding> findings)
    {
        var requirement = item.Requirement;

        if (item.State == CoverageState.Missing)
        {
            findings.Add(Finding.Error(
                MissingProof,
                $"{requirement.Id} ({requirement.Title}) is in scope but has no test or evidence. Tag a test with its ID, or add an entry to {TraceFiles.EvidencePath}.",
                TraceFiles.RequirementsPath));
        }

        var delivered = requirement.DeliveredBy.All(id => IsCompleted(status, id));

        // A requirement that is in progress may be partly met for a reason its notes give, so only 'planned' is treated as forgotten.
        if (item.HasProof && delivered && requirement.Status == "planned")
        {
            findings.Add(Finding.Warning(
                "trace-status",
                $"{requirement.Id} has proof and every work package that delivers it is completed, but its status is still 'planned'. Set it to 'in_progress' or 'verified'.",
                TraceFiles.RequirementsPath));
        }
    }

    private static bool IsInScope(Requirement requirement, StatusDocument status, TraceOptions options)
    {
        if (requirement.Status is "deferred" or "withdrawn")
        {
            return false;
        }

        if (requirement.Status == "verified" || options.All)
        {
            return true;
        }

        if (options.ThroughPhase is { } through)
        {
            return requirement.DeliveredBy.All(id => PhaseOrder(status, id) <= through.Order);
        }

        return requirement.DeliveredBy.All(id => IsCompleted(status, id));
    }

    private static bool IsKnownWorkPackage(StatusDocument status, string id) =>
        string.Equals(id, LaunchWorkPackage, StringComparison.Ordinal) || status.FindWorkPackage(id) is not null;

    private static bool IsCompleted(StatusDocument status, string id)
    {
        if (string.Equals(id, LaunchWorkPackage, StringComparison.Ordinal))
        {
            return status.Phases.Any(phase => phase.Id.IsLaunch && phase.State == WorkState.Completed);
        }

        return status.FindWorkPackage(id)?.State == WorkState.Completed;
    }

    private static int PhaseOrder(StatusDocument status, string id)
    {
        if (string.Equals(id, LaunchWorkPackage, StringComparison.Ordinal))
        {
            return int.MaxValue;
        }

        return status.Phases.FirstOrDefault(phase => phase.WorkPackages.Any(workPackage => string.Equals(workPackage.Id, id, StringComparison.Ordinal)))?.Id.Order
            ?? int.MaxValue;
    }

    private static bool PathExists(RepositoryFiles files, string path) =>
        path.EndsWith('/') ? files.DirectoryExists(path) : files.FileExists(path);
}
