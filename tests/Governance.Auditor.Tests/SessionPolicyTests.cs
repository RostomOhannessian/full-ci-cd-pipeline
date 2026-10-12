using Governance.Auditor.Common;
using Governance.Auditor.Session;
using Governance.Auditor.Tests.Support;
using Xunit;

namespace Governance.Auditor.Tests;

public sealed class SessionPolicyTests
{
    private const string Leftovers = "  - id: leftovers\n    when: { moment: start, handoff: incomplete }\n    advice: stop\n    reason: \"The previous session left its handoff open, so ask the maintainer.\"\n";
    private const string HandoffFirst = "  - id: handoff-first\n    when: { moment: end, handoff: incomplete }\n    advice: finish-handoff\n    reason: \"The handoff is open, so close its gaps first.\"\n";
    private const string Default = "  - id: work-package\n    advice: fresh\n    reason: \"A new work package starts in a fresh session.\"\n";

    private static string RealPolicyText => File.ReadAllText(Path.Combine(RealRepository.Root, SessionPolicy.Path)).ReplaceLineEndings("\n");

    // Each row is a fact set and the rule that decides it. The rows that start a comment say what the facts show.
    public static TheoryData<string, string, string, string, string, string> Decisions => new()
    {
        // leftovers: an open handoff at the start beats every other fact.
        { "start", "incomplete", "used", "package", "work-package", "leftovers" },
        { "start", "incomplete", "fresh", "phase", "bug-fix", "leftovers" },
        // handoff-first: an open handoff at the end beats a phase boundary and a compacted context.
        { "end", "incomplete", "used", "package", "work-package", "handoff-first" },
        { "end", "incomplete", "compacted", "phase", "pull-request", "handoff-first" },
        // fresh-session-start: a fresh context at the start is the fresh session, even at a boundary.
        { "start", "complete", "fresh", "package", "work-package", "fresh-session-start" },
        { "start", "complete", "fresh", "phase", "bug-fix", "fresh-session-start" },
        // phase-boundary: a phase boundary beats a compacted context and a follow-up.
        { "end", "complete", "used", "phase", "work-package", "phase-boundary" },
        { "start", "complete", "compacted", "phase", "pull-request", "phase-boundary" },
        // compacted: a compacted conversation beats a follow-up.
        { "end", "complete", "compacted", "package", "work-package", "compacted" },
        { "end", "complete", "compacted", "package", "pull-request", "compacted" },
        // pull-request-follow-up: an intact context continues on its own pull request.
        { "end", "complete", "used", "package", "pull-request", "pull-request-follow-up" },
        { "start", "complete", "used", "package", "pull-request", "pull-request-follow-up" },
        // bug-fix: a bug fix starts fresh, at the start of a used session and at the end of any.
        { "end", "complete", "used", "package", "bug-fix", "bug-fix" },
        { "start", "complete", "used", "package", "bug-fix", "bug-fix" },
        // work-package: the default for everything else.
        { "end", "complete", "used", "package", "work-package", "work-package" },
        { "start", "complete", "used", "package", "work-package", "work-package" },
    };

    // Each row is a rule, facts that differ from its conditions in one fact, and the rule that decides instead.
    public static TheoryData<string, string, string, string, string, string, string> NearMisses => new()
    {
        { "leftovers", "end", "incomplete", "used", "package", "work-package", "handoff-first" },
        { "leftovers", "start", "complete", "used", "package", "work-package", "work-package" },
        { "handoff-first", "start", "incomplete", "used", "package", "work-package", "leftovers" },
        { "handoff-first", "end", "complete", "used", "package", "work-package", "work-package" },
        { "fresh-session-start", "start", "complete", "used", "package", "work-package", "work-package" },
        { "fresh-session-start", "end", "complete", "fresh", "package", "work-package", "work-package" },
        { "phase-boundary", "end", "complete", "used", "package", "work-package", "work-package" },
        { "phase-boundary", "end", "complete", "compacted", "package", "work-package", "compacted" },
        { "compacted", "end", "complete", "used", "package", "work-package", "work-package" },
        { "compacted", "end", "complete", "fresh", "package", "pull-request", "pull-request-follow-up" },
        { "pull-request-follow-up", "end", "complete", "used", "package", "work-package", "work-package" },
        { "pull-request-follow-up", "end", "complete", "used", "package", "bug-fix", "bug-fix" },
        { "bug-fix", "end", "complete", "used", "package", "work-package", "work-package" },
        { "bug-fix", "end", "complete", "used", "package", "pull-request", "pull-request-follow-up" },
        { "work-package", "end", "complete", "used", "package", "bug-fix", "bug-fix" },
        { "work-package", "end", "incomplete", "used", "package", "work-package", "handoff-first" },
    };

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public void The_repository_policy_is_valid_and_lists_the_eight_rules_in_the_documented_order()
    {
        var policy = SessionPolicy.Load(RealRepository.Files);

        Assert.Equal(
            ["leftovers", "handoff-first", "fresh-session-start", "phase-boundary", "compacted", "pull-request-follow-up", "bug-fix", "work-package"],
            policy.Rules.Select(rule => rule.Id));
        Assert.Equal(
            ["stop", "finish-handoff", "continue", "fresh", "fresh", "continue", "fresh", "fresh"],
            policy.Rules.Select(rule => rule.Advice));
        Assert.All(new[] { policy.Prompts.WorkPackage, policy.Prompts.PullRequest, policy.Prompts.BugFix }, prompt => Assert.True(prompt.Length > 200));
    }

    [Theory]
    [MemberData(nameof(Decisions))]
    [Trait("Requirement", "REQ-AI-004")]
    public void A_rule_decides_when_its_facts_hold(string moment, string handoff, string context, string boundary, string task, string expectedRule)
    {
        var policy = SessionPolicy.Load(RealRepository.Files);

        Assert.Equal(expectedRule, policy.Evaluate(new SessionFacts(moment, handoff, context, boundary, task)).Id);
    }

    [Theory]
    [MemberData(nameof(NearMisses))]
    [Trait("Requirement", "REQ-AI-004")]
    public void A_rule_stays_silent_when_one_of_its_facts_differs(string silentRule, string moment, string handoff, string context, string boundary, string task, string decidingRule)
    {
        var policy = SessionPolicy.Load(RealRepository.Files);

        var decided = policy.Evaluate(new SessionFacts(moment, handoff, context, boundary, task)).Id;

        Assert.NotEqual(silentRule, decided);
        Assert.Equal(decidingRule, decided);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public void Every_combination_of_facts_gets_the_advice_of_an_independent_reading_of_the_rules()
    {
        var policy = SessionPolicy.Load(RealRepository.Files);
        var all = SessionVocabulary.AllFacts().ToList();

        Assert.Equal(72, all.Count);

        foreach (var facts in all)
        {
            var expected = Reference(facts);
            var actual = policy.Evaluate(facts);

            Assert.True(
                string.Equals(expected.Rule, actual.Id, StringComparison.Ordinal) && string.Equals(expected.Advice, actual.Advice, StringComparison.Ordinal),
                $"{facts}: expected {expected.Rule} ({expected.Advice}), got {actual.Id} ({actual.Advice}).");
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public void An_open_handoff_never_gets_the_advice_to_go_on_and_a_complete_one_never_gets_the_advice_to_stop()
    {
        var policy = SessionPolicy.Load(RealRepository.Files);

        foreach (var facts in SessionVocabulary.AllFacts())
        {
            var advice = policy.Evaluate(facts).Advice;

            Assert.Equal(facts.Handoff == SessionVocabulary.Incomplete, SessionVocabulary.IsBlocking(advice));
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public void A_fresh_session_is_the_default_at_a_work_package_boundary_a_phase_boundary_and_after_a_compaction_and_for_a_bug_fix()
    {
        var policy = SessionPolicy.Load(RealRepository.Files);

        Assert.Equal("fresh", policy.Evaluate(new SessionFacts("end", "complete", "used", "package", "work-package")).Advice);
        Assert.Equal("fresh", policy.Evaluate(new SessionFacts("end", "complete", "used", "phase", "work-package")).Advice);
        Assert.Equal("fresh", policy.Evaluate(new SessionFacts("end", "complete", "compacted", "package", "work-package")).Advice);
        Assert.Equal("fresh", policy.Evaluate(new SessionFacts("start", "complete", "used", "package", "bug-fix")).Advice);
        Assert.Equal("continue", policy.Evaluate(new SessionFacts("end", "complete", "used", "package", "pull-request")).Advice);
    }

    [Theory]
    [Trait("Requirement", "REQ-AI-004")]
    [InlineData("advice: stop", "advice: wait", "/rules/0/advice")]
    [InlineData("moment: start", "moment: middle", "/rules/0/when/moment")]
    [InlineData("handoff: incomplete", "handoff: broken", "/rules/0/when/handoff")]
    [InlineData("context: fresh", "context: stale", "/rules/2/when/context")]
    [InlineData("boundary: phase", "boundary: epoch", "/rules/3/when/boundary")]
    [InlineData("task: pull-request", "task: issue", "/rules/5/when/task")]
    [InlineData("handoff: incomplete\n    advice: stop", "handoff: incomplete\n      mood: calm\n    advice: stop", "mood")]
    [InlineData("advice: stop\n", "advice: stop\n    priority: 1\n", "priority")]
    [InlineData("schema-version: 1\n", "schema-version: 1\nmode: strict\n", "mode")]
    [InlineData("schema-version: 1\n", "schema-version: 2\n", "/schema-version")]
    [InlineData("  - id: leftovers", "  - id: Left Overs", "/rules/0/id")]
    public void An_unknown_value_or_key_in_the_policy_file_fails_validation_and_names_where(string from, string to, string expectedPointer)
    {
        var exception = Assert.Throws<GovernanceException>(() => Load(Mutate(from, to)));

        Assert.Contains(expectedPointer, exception.Message, StringComparison.Ordinal);
        Assert.Contains(SessionPolicy.Path, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public void A_policy_without_a_prompt_for_each_kind_of_task_fails_validation()
    {
        var withoutBugFix = RealPolicyText[..RealPolicyText.IndexOf("  bug-fix: |", StringComparison.Ordinal)];

        var exception = Assert.Throws<GovernanceException>(() => Load(withoutBugFix));

        Assert.Contains("bug-fix", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public void A_policy_file_that_is_not_there_is_a_tool_error()
    {
        using var repository = new TestRepository().CopyFromRepository(SessionPolicy.SchemaPath);

        Assert.Throws<GovernanceException>(() => SessionPolicy.Load(repository.Files));
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public void A_rule_with_the_conditions_of_an_earlier_rule_can_never_apply_and_is_refused()
    {
        var again = "  - id: leftovers-again\n    when: { moment: start, handoff: incomplete }\n    advice: stop\n    reason: \"The first rule already decides every one of these cases.\"\n";

        var exception = Assert.Throws<GovernanceException>(() => Load(Policy(Leftovers + again + HandoffFirst + Default)));

        Assert.Contains("The rule 'leftovers-again' can never apply", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public void A_rule_after_the_default_can_never_apply_and_is_refused()
    {
        var shadowed = "  - id: later\n    when: { task: bug-fix }\n    advice: fresh\n    reason: \"The default before it decides every one of these cases.\"\n";

        var exception = Assert.Throws<GovernanceException>(() => Load(Policy(Leftovers + HandoffFirst + Default + shadowed)));

        Assert.Contains("The rule 'later' can never apply", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public void A_policy_that_leaves_a_case_without_a_rule_is_refused()
    {
        var exception = Assert.Throws<GovernanceException>(() => Load(Policy(Leftovers + HandoffFirst)));

        Assert.Contains("combinations of facts match no rule", exception.Message, StringComparison.Ordinal);
        Assert.Contains("End the rules with one that has no conditions", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public void A_rule_id_that_appears_twice_is_refused()
    {
        var exception = Assert.Throws<GovernanceException>(() => Load(Policy(Leftovers + HandoffFirst + Default + Default)));

        Assert.Contains("The rule 'work-package' appears 2 times", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public void An_open_handoff_that_gets_the_advice_to_go_on_is_refused()
    {
        var exception = Assert.Throws<GovernanceException>(() => Load(Policy(Leftovers + Default)));

        Assert.Contains("gives 'fresh' when the handoff is incomplete", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public void A_complete_handoff_that_gets_the_advice_to_stop_is_refused()
    {
        var stopsBugFixes = "  - id: no-bug-fixes\n    when: { handoff: complete, task: bug-fix }\n    advice: stop\n    reason: \"A bug fix is never allowed here.\"\n";

        var exception = Assert.Throws<GovernanceException>(() => Load(Policy(Leftovers + HandoffFirst + stopsBugFixes + Default)));

        Assert.Contains("gives 'stop' when the handoff is complete", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public void A_policy_that_is_minimal_but_complete_loads()
    {
        var policy = Load(Policy(Leftovers + HandoffFirst + Default));

        Assert.Equal(["leftovers", "handoff-first", "work-package"], policy.Rules.Select(rule => rule.Id));
    }

    [Theory]
    [Trait("Requirement", "REQ-AI-004")]
    [InlineData("work-package", "Start {work-package-id} and {nonsense}.", "The 'work-package' prompt uses the placeholder '{nonsense}'")]
    [InlineData("work-package", "Start {work-package-id} on {pull-request}.", "The 'work-package' prompt uses the placeholder '{pull-request}'")]
    [InlineData("pull-request", "Follow up on {issue}.", "The 'pull-request' prompt uses the placeholder '{issue}'")]
    [InlineData("bug-fix", "Fix it on {branch}.", "The 'bug-fix' prompt uses the placeholder '{branch}'")]
    [InlineData("bug-fix", "Fix it with { an unbalanced brace.", "The 'bug-fix' prompt has a brace that is not part of a placeholder")]
    [InlineData("bug-fix", "Fix it, and then close the placeholder } wrongly.", "The 'bug-fix' prompt has a brace that is not part of a placeholder")]
    [InlineData("work-package", "Show a ```text fence in the prompt.", "The 'work-package' prompt contains a code fence")]
    public void A_prompt_that_uses_a_placeholder_that_does_not_exist_or_a_stray_brace_or_a_fence_is_refused(string task, string text, string expectedMessage)
    {
        var exception = Assert.Throws<GovernanceException>(() => Load(Policy(Leftovers + HandoffFirst + Default, (task, Pad(text)))));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public void A_prompt_that_is_too_short_to_carry_the_rules_fails_the_schema()
    {
        var exception = Assert.Throws<GovernanceException>(() => Load(Policy(Leftovers + HandoffFirst + Default, ("bug-fix", "Fix it."))));

        Assert.Contains("/prompts/bug-fix", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public void The_prompts_of_the_repository_policy_use_only_placeholders_that_the_tool_fills()
    {
        var policy = SessionPolicy.Load(RealRepository.Files);

        foreach (var (task, prompt) in new[]
        {
            (SessionVocabulary.WorkPackage, policy.Prompts.WorkPackage),
            (SessionVocabulary.PullRequest, policy.Prompts.PullRequest),
            (SessionVocabulary.BugFix, policy.Prompts.BugFix),
        })
        {
            var used = SessionPolicy.PlaceholderPattern().Matches(prompt).Select(match => match.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);

            Assert.NotEmpty(used);
            Assert.Subset(SessionPrompts.Placeholders[task].ToHashSet(StringComparer.Ordinal), used);
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public void The_work_package_prompt_carries_the_rules_that_every_session_must_follow()
    {
        var prompt = SessionPolicy.Load(RealRepository.Files).Prompts.WorkPackage;

        foreach (var expected in new[]
        {
            "git status", "git fetch origin", "never in a worktree", "ask_user", "push, merge, or settings change", "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>",
            "noreply identity", "Definition of Done", "/session-handoff", "governance session --moment end", "{branch}", "{phase-branch}", "{phase-plan}", "gh issue view {issue}",
        })
        {
            Assert.Contains(expected, prompt, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public void The_bug_fix_prompt_asks_for_a_test_first_fix_on_its_own_branch_with_the_same_definition_of_done()
    {
        var prompt = SessionPolicy.Load(RealRepository.Files).Prompts.BugFix;

        foreach (var expected in new[]
        {
            "including the smallest bug fix", "fix/<slug>", "failing regression test first", "Trait(\"Requirement\"", "blast-radius", "CHANGELOG", "Definition of Done", "ADR-0003", "{phase-branch}", "/session-handoff",
        })
        {
            Assert.Contains(expected, prompt, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Requirement", "REQ-AI-004")]
    public void The_pull_request_prompt_limits_the_session_to_the_review_comments_and_the_failed_checks()
    {
        var prompt = SessionPolicy.Load(RealRepository.Files).Prompts.PullRequest;

        Assert.Contains("review comments and the failed checks of this pull request, and nothing else", prompt, StringComparison.Ordinal);
        Assert.Contains("#{pull-request}", prompt, StringComparison.Ordinal);
        Assert.Contains("{branch}", prompt, StringComparison.Ordinal);
    }

    // A plain reading of the eight rules in the policy file, written as an if chain so that it shares no code with the evaluator.
    private static (string Rule, string Advice) Reference(SessionFacts facts)
    {
        if (facts.Moment == "start" && facts.Handoff == "incomplete")
        {
            return ("leftovers", "stop");
        }

        if (facts.Moment == "end" && facts.Handoff == "incomplete")
        {
            return ("handoff-first", "finish-handoff");
        }

        if (facts.Moment == "start" && facts.Context == "fresh")
        {
            return ("fresh-session-start", "continue");
        }

        if (facts.Boundary == "phase")
        {
            return ("phase-boundary", "fresh");
        }

        if (facts.Context == "compacted")
        {
            return ("compacted", "fresh");
        }

        if (facts.Task == "pull-request")
        {
            return ("pull-request-follow-up", "continue");
        }

        return facts.Task == "bug-fix" ? ("bug-fix", "fresh") : ("work-package", "fresh");
    }

    private static string Mutate(string from, string to)
    {
        var text = RealPolicyText;
        var index = text.IndexOf(from, StringComparison.Ordinal);

        Assert.True(index >= 0, $"The policy file does not contain '{from}'.");
        return text[..index] + to + text[(index + from.Length)..];
    }

    private static SessionPolicy Load(string policyYaml)
    {
        using var repository = new TestRepository().CopyFromRepository(SessionPolicy.SchemaPath).Add(SessionPolicy.Path, policyYaml);
        return SessionPolicy.Load(repository.Files);
    }

    private static string ValidPrompt(string task) => task switch
    {
        "work-package" => Pad("Start {work-package-id}, {work-package-title}, for {project-name} on {branch} from {phase-branch}, phase {phase} ({phase-plan}), Issue {issue}."),
        "pull-request" => Pad("Follow up on #{pull-request}, {pull-request-title}, for {project-name} on {branch}."),
        _ => Pad("Fix the bug for {project-name}, and merge master back into {phase-branch}."),
    };

    private static string Pad(string text) => text + string.Concat(Enumerable.Repeat(" Keep the change small and the evidence exact.", 6));

    private static string Policy(string rules, (string Task, string Text)? prompt = null)
    {
        string For(string task) => prompt is { } custom && string.Equals(custom.Task, task, StringComparison.Ordinal) ? custom.Text : ValidPrompt(task);

        return $"schema-version: 1\nrules:\n{rules}prompts:\n  work-package: {Quote(For("work-package"))}\n  pull-request: {Quote(For("pull-request"))}\n  bug-fix: {Quote(For("bug-fix"))}\n";
    }

    private static string Quote(string text) => "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
