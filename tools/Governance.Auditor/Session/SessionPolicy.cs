using System.Text.RegularExpressions;
using Governance.Auditor.Common;
using Governance.Auditor.Documents;

namespace Governance.Auditor.Session;

/// <summary>The names that the policy file, the command line, and the report share. A name that is not listed here is not a valid value.</summary>
internal static class SessionVocabulary
{
    public const string Start = "start";
    public const string End = "end";
    public const string Complete = "complete";
    public const string Incomplete = "incomplete";
    public const string Fresh = "fresh";
    public const string Used = "used";
    public const string Compacted = "compacted";
    public const string Package = "package";
    public const string Phase = "phase";
    public const string WorkPackage = "work-package";
    public const string PullRequest = "pull-request";
    public const string BugFix = "bug-fix";
    public const string Continue = "continue";
    public const string FinishHandoff = "finish-handoff";
    public const string Stop = "stop";

    public static IReadOnlyList<string> Moments { get; } = [Start, End];

    public static IReadOnlyList<string> Handoffs { get; } = [Complete, Incomplete];

    public static IReadOnlyList<string> Contexts { get; } = [Fresh, Used, Compacted];

    public static IReadOnlyList<string> Boundaries { get; } = [Package, Phase];

    public static IReadOnlyList<string> Tasks { get; } = [WorkPackage, PullRequest, BugFix];

    public static IReadOnlyList<string> Advice { get; } = [Continue, Fresh, FinishHandoff, Stop];

    /// <summary>Advice that tells the session not to move on. The command exits with 1 for it.</summary>
    public static bool IsBlocking(string advice) => advice is FinishHandoff or Stop;

    /// <summary>Every combination of facts, which is small enough to check exhaustively.</summary>
    public static IEnumerable<SessionFacts> AllFacts() =>
        from moment in Moments
        from handoff in Handoffs
        from context in Contexts
        from boundary in Boundaries
        from task in Tasks
        select new SessionFacts(moment, handoff, context, boundary, task);
}

/// <summary>What the rules look at. The caller gives the moment, the context, and the task, and the tool works out the handoff and the boundary.</summary>
internal sealed record SessionFacts(string Moment, string Handoff, string Context, string Boundary, string Task)
{
    public override string ToString() => $"moment {Moment}, handoff {Handoff}, context {Context}, boundary {Boundary}, task {Task}";
}

internal sealed record SessionConditions
{
    public string? Moment { get; init; }

    public string? Handoff { get; init; }

    public string? Context { get; init; }

    public string? Boundary { get; init; }

    public string? Task { get; init; }

    public bool Matches(SessionFacts facts) =>
        Holds(Moment, facts.Moment) && Holds(Handoff, facts.Handoff) && Holds(Context, facts.Context) && Holds(Boundary, facts.Boundary) && Holds(Task, facts.Task);

    private static bool Holds(string? wanted, string actual) => wanted is null || string.Equals(wanted, actual, StringComparison.Ordinal);
}

internal sealed record SessionRule
{
    public required string Id { get; init; }

    public string? Description { get; init; }

    public SessionConditions When { get; init; } = new();

    public required string Advice { get; init; }

    public required string Reason { get; init; }
}

internal sealed record SessionPrompts
{
    public required string WorkPackage { get; init; }

    public required string PullRequest { get; init; }

    public required string BugFix { get; init; }

    /// <summary>The placeholders that each template may use. The advisor fills exactly these.</summary>
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> Placeholders { get; } = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
    {
        [SessionVocabulary.WorkPackage] = new HashSet<string>(StringComparer.Ordinal)
        {
            "project-name", "plan", "work-package-id", "work-package-title", "issue", "phase", "phase-plan", "phase-branch", "branch",
        },
        [SessionVocabulary.PullRequest] = new HashSet<string>(StringComparer.Ordinal)
        {
            "project-name", "pull-request", "pull-request-title", "branch",
        },
        [SessionVocabulary.BugFix] = new HashSet<string>(StringComparer.Ordinal)
        {
            "project-name", "phase-branch",
        },
    };

    public string For(string task) => task switch
    {
        SessionVocabulary.WorkPackage => WorkPackage,
        SessionVocabulary.PullRequest => PullRequest,
        SessionVocabulary.BugFix => BugFix,
        _ => throw new GovernanceException($"'{task}' is not a kind of task. Use work-package, pull-request, or bug-fix."),
    };
}

/// <summary>
/// The typed view of <c>governance/policies/session-policy.yaml</c>: the rules that turn the facts of a session into advice, and the kickoff
/// prompt templates. The tool refuses a policy that the schema or the checks below reject, so a rule that cannot fire, a case that no rule
/// answers, or an open handoff that gets the advice to go on never reaches a session.
/// </summary>
internal sealed partial record SessionPolicy
{
    public const string Path = "governance/policies/session-policy.yaml";
    public const string SchemaPath = "governance/policies/session-policy.schema.json";
    public const string SchemaRule = "session-policy-schema";

    public required int SchemaVersion { get; init; }

    public string? Description { get; init; }

    public required IReadOnlyList<SessionRule> Rules { get; init; }

    public required SessionPrompts Prompts { get; init; }

    public static SessionPolicy Load(RepositoryFiles files)
    {
        List<Finding> findings = [];
        var node = SchemaDocument.Load(files, Path, SchemaPath, SchemaRule, findings);

        if (node is null)
        {
            throw new GovernanceException($"{Path}: {string.Join(" ", findings.Select(finding => finding.Message))}");
        }

        var policy = DocumentJson.Deserialize<SessionPolicy>(node, Path);
        policy.Validate();
        return policy;
    }

    /// <summary>The first rule whose conditions all hold. A validated policy always has one.</summary>
    public SessionRule Evaluate(SessionFacts facts) =>
        Rules.FirstOrDefault(rule => rule.When.Matches(facts)) ?? throw new GovernanceException($"{Path}: no rule matches {facts}.");

    [GeneratedRegex(@"\{(?<name>[a-z][a-z-]*)\}", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    internal static partial Regex PlaceholderPattern();

    private void Validate()
    {
        if (SchemaVersion != 1)
        {
            throw new GovernanceException($"{Path}: schema-version {SchemaVersion} is not supported. This tool reads version 1.");
        }

        List<string> problems = [];

        foreach (var duplicate in Rules.GroupBy(rule => rule.Id, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            problems.Add($"The rule '{duplicate.Key}' appears {duplicate.Count()} times.");
        }

        CheckRules(problems);
        CheckTemplate(SessionVocabulary.WorkPackage, Prompts.WorkPackage, problems);
        CheckTemplate(SessionVocabulary.PullRequest, Prompts.PullRequest, problems);
        CheckTemplate(SessionVocabulary.BugFix, Prompts.BugFix, problems);

        if (problems.Count > 0)
        {
            throw new GovernanceException($"{Path}: {string.Join(" ", problems)}");
        }
    }

    // The facts have 72 combinations, so every one is tried. That proves that each rule can fire, that every case has an answer, and that an
    // open handoff is never told to go on, which a reader of the rules would otherwise have to work out by hand.
    private void CheckRules(List<string> problems)
    {
        HashSet<string> reached = new(StringComparer.Ordinal);
        HashSet<string> blockingMismatch = new(StringComparer.Ordinal);
        List<SessionFacts> unanswered = [];

        foreach (var facts in SessionVocabulary.AllFacts())
        {
            var rule = Rules.FirstOrDefault(candidate => candidate.When.Matches(facts));

            if (rule is null)
            {
                unanswered.Add(facts);
                continue;
            }

            reached.Add(rule.Id);
            var incomplete = string.Equals(facts.Handoff, SessionVocabulary.Incomplete, StringComparison.Ordinal);

            if (incomplete != SessionVocabulary.IsBlocking(rule.Advice))
            {
                blockingMismatch.Add(incomplete
                    ? $"The rule '{rule.Id}' gives '{rule.Advice}' when the handoff is incomplete ({facts}). An open handoff must give finish-handoff or stop."
                    : $"The rule '{rule.Id}' gives '{rule.Advice}' when the handoff is complete ({facts}). Only an open handoff may give finish-handoff or stop.");
            }
        }

        if (unanswered.Count > 0)
        {
            problems.Add($"{unanswered.Count} combinations of facts match no rule, for example {unanswered[0]}. End the rules with one that has no conditions.");
        }

        problems.AddRange(Rules
            .Where(rule => !reached.Contains(rule.Id))
            .Select(rule => $"The rule '{rule.Id}' can never apply, because the rules before it already match every case that it matches."));
        problems.AddRange(blockingMismatch.Order(StringComparer.Ordinal));
    }

    private static void CheckTemplate(string task, string template, List<string> problems)
    {
        var allowed = SessionPrompts.Placeholders[task];

        foreach (var name in PlaceholderPattern().Matches(template).Select(match => match.Groups["name"].Value).Distinct(StringComparer.Ordinal).Where(name => !allowed.Contains(name)))
        {
            problems.Add($"The '{task}' prompt uses the placeholder '{{{name}}}', which does not exist. It may use {string.Join(", ", allowed.Order(StringComparer.Ordinal))}.");
        }

        var rest = PlaceholderPattern().Replace(template, string.Empty);

        if (rest.Contains('{', StringComparison.Ordinal) || rest.Contains('}', StringComparison.Ordinal))
        {
            problems.Add($"The '{task}' prompt has a brace that is not part of a placeholder. A placeholder is a lowercase word with dashes in braces.");
        }

        if (template.Contains("```", StringComparison.Ordinal))
        {
            problems.Add($"The '{task}' prompt contains a code fence. The Markdown report puts the prompt in one, so the prompt must not.");
        }
    }
}
