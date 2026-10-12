---
title: "ADR-0030: Advise session boundaries with a deterministic check whose rules and prompts are data"
description: "Decide when a task belongs in a fresh AI-paired session with one command that reads status.yaml and the Git state and applies a policy file, so the advice is reproducible, a skill never decides it, and every kickoff prompt comes from reviewed text."
status: accepted
date: 2026-10-12
decision-makers: ["@RostomOhannessian"]
supersedes: []
superseded-by: []
tools: [governance-auditor, copilot-skills]
related-adrs: ["0003", "0006", "0021", "0022"]
evidence:
  - governance/policies/session-policy.yaml
  - governance/policies/session-policy.schema.json
  - tools/Governance.Auditor/Session
  - tools/Governance.Auditor/Cli/SessionCommand.cs
  - tests/Governance.Auditor.Tests/SessionPolicyTests.cs
  - tests/Governance.Auditor.Tests/SessionCommandTests.cs
  - tests/Governance.Auditor.Tests/Fixtures/skills/session-boundary-advisor
  - .github/skills/session-boundary-advisor/SKILL.md
  - docs/explanation/session-boundaries.md
---

# ADR-0030: Advise session boundaries with a deterministic check whose rules and prompts are data

## Context and problem statement

Every change to this repository is made in an AI-paired session, including the smallest bug fix, and a session is the unit of context
([plan section 5.8](../plans/implementation-plan.md)). A long session carries what it has read, and it loses detail when its conversation is
compacted. A short session starts clean, but only if the repository says where to start. The maintainer asked for a way to know, at the
beginning and at the end of each work package and phase, whether the next task belongs in a fresh session. This decision serves
`REQ-AI-004` and builds on `REQ-GOV-003`, the resume protocol.

Judging this by feel is not reproducible, and a skill that judged it would hold policy in prompt text, which
[the AI skills page](../governance/ai-skills.md) forbids. Facts that shape the choice (checked 2026-10-12):

- The state that decides a boundary is already in the repository. `docs/project/status.yaml` has the work packages, their states, and their
  dependencies, `STATUS.md` is generated from it ([ADR-0022](0022-generated-status-and-traceability.md)), and Git has the working tree, the
  branch, and its upstream.
- Whether the conversation was compacted is not in the repository. The Copilot CLI keeps a checkpoint list in the session's own folder, and
  only the agent that holds the conversation knows that it was summarized.
- The list order of `status.yaml` is not the order that the maintainer chooses. WP1.7 comes before WP1.14 and WP1.15 in the list, and the
  maintainer asked for WP1.14, WP1.15, and then WP1.7.
- `governance status validate` refuses a phase that is `in_progress`, `planned`, or `completed` when its work packages disagree with it,
  and it accepts a `blocked` phase whose work packages are all completed. It refuses a phase in progress with every work package completed,
  so the usual state of "the last work package is done and the phase is not" never validates.
- A kickoff prompt is text that an agent follows. Text that comes from reviewed repository files is safer than text that an agent writes.

## Decision drivers

- The same facts give the same advice on every machine, and a rule can be shown to fire and to stay silent (`REQ-AI-004`).
- The rules are data that a reviewer reads, and CODEOWNERS covers `governance/`
  ([ADR-0021](0021-governance-auditor-cli.md)).
- A session never moves on while its handoff is open, because the next session could not resume from the repository.
- The check needs no network and no session store, so it works in a fresh clone and cannot be fooled by a service.
- The maintainer's chosen order is respected, and the default order stays predictable.
- A bug fix is test-first and meets the same Definition of Done as a feature, so it also gets a prompt.

## Considered options

1. One command, `governance session`, with the rules and the kickoff prompts as data in a policy file, and a thin skill that explains it
   (chosen).
2. A skill that decides from a checklist in prose.
3. A CI check on the pull request.
4. A paragraph in `AGENTS.md` and nothing else.

## Decision outcome

Chosen option: **one command, a policy file, and a thin skill**, because it is the only option that gives a reproducible answer, keeps
policy out of prompt text, and puts the kickoff prompt in reviewed text.

The design:

- **The command.** `governance session --moment start|end [--task work-package|pull-request|bug-fix] [--context fresh|used|compacted]
  [--json] [--summary <file>]`. It reads `status.yaml` through the existing loader and validator, and the Git state through the existing
  process runner: `git status`, the branch, and the commits ahead of and behind the upstream. It does not fetch, so the counts are as of the
  last fetch. The caller supplies the context, because only the agent knows whether its conversation was summarized, and it defaults to
  `used`.
- **The answer.** One of `continue`, `fresh`, `finish-handoff`, or `stop`, with the rule and the reasons, the next work package, the
  boundary kind, the handoff gaps, and the kickoff prompt. The exit code is 0 when advice was produced, 1 for `finish-handoff` and `stop`,
  and 2 when the tool could not run. A mistyped option also exits with 1, as it does for every governance command, and it prints no advice,
  so the first line of the output tells the two apart. The prompt is printed only for `fresh`.
- **The handoff gaps.** Uncommitted changes, commits that no remote has (the commits ahead of the upstream, or the commits on no remote when
  there is no upstream), a `status.yaml` that is not valid, and a `STATUS.md` that differs from what `status.yaml` renders. Being behind the
  upstream is a note and not a gap.
- **The next work package.** The one that `resume.next-work-package` names when it is ready, and otherwise the first planned or in-progress
  work package, in phase order, whose dependencies are all completed. `resume.next-work-package` is a new optional field of `status.yaml`
  that records the maintainer's chosen order as data, because the list order gave WP1.7 first. The validator refuses a name that is not in
  the file or that is already completed, and accepts one that is not ready yet.
- **The boundary kind.** `phase` when the next work package starts a phase (its number ends in `.0`, or its phase differs from the phase of
  the last completed work package), when no work package is completed yet, or when every work package of the current phase is completed and
  the phase is not. Otherwise `package`. The last case needs a phase that is blocked with every work package completed, because the validator
  refuses an in-progress phase in that state, so it is rare, and a test reaches it directly.
- **The rules.** Eight rules in `governance/policies/session-policy.yaml`, evaluated in order, where the first match wins: `leftovers`,
  `handoff-first`, `fresh-session-start`, `phase-boundary`, `compacted`, `pull-request-follow-up`, `bug-fix`, and `work-package`. A fresh
  session is the default at every work-package and phase boundary, after a compaction, and for a bug fix. A session continues only for
  follow-up on its own open pull request while its context is intact, and at the start of a session that began fresh.
- **The prompts.** Three templates in the same file, one for a work package, one for pull request follow-up, and one for a bug fix. The tool
  fills their placeholders from `status.yaml` in a single pass, so a title that looks like a placeholder is copied and not expanded.
- **The policy is checked, not only parsed.** The schema rejects an unknown value or key. The tool also tries all 72 combinations of facts
  and refuses a policy with a rule that no combination reaches, a combination that no rule answers, an open handoff that gets `continue` or
  `fresh`, a complete handoff that gets `finish-handoff` or `stop`, a placeholder that does not exist, a stray brace, or a code fence in a
  prompt.
- **The bug fix.** The prompt says that a bug fix is made on a `fix/<slug>` branch from `master` with its own pull request, and that
  `master` is merged back into the active phase branch afterwards, as [ADR-0003](0003-branch-and-work-package-protocol.md) says. It asks for
  the failing regression test first, tagged with the requirement that it protects, the blast-radius evaluator, the changelog, and the same
  Definition of Done as a feature.
- **The skill.** `/session-boundary-advisor` runs the command and explains the answer. It holds no write scope, and it hands the prompt over
  unchanged. `/session-handoff` ends by running the same check.

### Consequences

- Good: the advice is reproducible, every rule is tested on facts that match it and on facts that do not, and an independent reading of the
  rules agrees with the policy for all 72 combinations.
- Good: an open handoff always stops the session, so no session starts on top of work that is not committed, not pushed, or not recorded.
- Good: the kickoff prompts come from a reviewed file and carry the rules that every session must follow, so a fresh session needs no
  oral tradition.
- Bad: a wrong `--context` gives wrong advice. Claiming `fresh` at the start is the costly mistake, because the rule for a session that
  began fresh comes before the boundary and task rules, so every `fresh` advice becomes `continue`. The skill and the protocol say to claim
  `fresh` only when the conversation began with a kickoff prompt. Mixing up `used` and `compacted` changes only the follow-up on a pull
  request, which continues when the context is `used` and moves to a fresh session when it is `compacted`, and `used` is the default.
- Bad: the check does not fetch, so a branch can look current when it is not. The prompts start with `git fetch origin`, and the report says
  that the counts are as of the last fetch.
- Bad: the prompts name the Copilot App trailer and `ask_user`, which are the tools in use today. A change of host is a change to the policy
  file, which CODEOWNERS covers.
- Bad: ADR-0003 defines hotfixes for `master`, and during a phase most merged work lives only on the phase branch. A bug in that work is
  not covered, so the bug-fix prompt tells the session to stop and ask the maintainer which branch to use. A rule for that case is a change
  to ADR-0003 and its own decision, and this one does not make it.
- Neutral: the check is advice and not a required check. It runs in sessions and by hand, and no workflow job runs it.

### Confirmation

- **WP1.14** delivers the command, the policy file and its schema, the skill and its golden fixture, and the tests. `SessionPolicyTests`
  checks each rule on matching and non-matching facts, checks the policy against an independent reading, and shows that each kind of invalid
  policy fails. `SessionCommandTests` checks the gaps, the next work package, the boundaries, the three prompts, the exit codes, and that the
  command only reads Git.
- The skill fixture compares the output with `expected-output.txt` and `expected-output-finish-handoff.txt`, so a change to a rule or a prompt
  shows up in review.
- The end of WP1.14 runs the command on this repository and records the kickoff prompt for the next work package in the pull request.
- **WP1.15** records the usage of each session, so the cost of a fresh session and of a long one can be compared.

Execution record (squash message rule, 2026-10-12): the three kickoff prompts carry the squash commit message rule, which asks for one logical
change per pull request and a squash merge with an explicit subject and body, and `SessionPolicyTests` fails when a prompt lacks it. The reason
is the squash commit of pull request 26 (`18fd243`), which named only the session boundary check and dropped the description, so plan
amendment 2.1.0 does not show in `git log` or in the release notes of `v0.1.0`. The commit is not rewritten, because the phase ruleset
forbids non-fast-forward pushes and `status.yaml` cites it by hash. The rule is written once, in
[CONTRIBUTING.md](../../CONTRIBUTING.md#squash-commit-message), and the prompts, the pull request template, and AGENTS.md point to it. This
fix is the first bug in work that exists only on a phase branch, and it was made on a branch from `phase/1-api-core`, because the policy file
is not on `master`. That is a reading of ADR-0003 and not a change to it.

## Pros and cons of the options

### One command, a policy file, and a thin skill

- Good: reproducible, tested, and reviewable, and the policy is data.
- Good: the same answer for a person, an agent, and a script.
- Bad: another command and another policy file to keep correct.

### A skill that decides from a prose checklist

- Good: nothing to build.
- Bad: the answer depends on the model, the policy lives in prompt text, and a rule cannot be tested.

### A CI check on the pull request

- Good: it runs without anyone remembering it.
- Bad: CI sees a pull request and not a session, so it cannot know whether the conversation was compacted, and a check that only advises
  would add a job to the blast-radius map for no gate.

### A paragraph in AGENTS.md

- Good: the cheapest option.
- Bad: nothing checks it, and the project teaches by showing that its rules can fail.

## Revisit when

- The AI usage ledger from WP1.15 shows that fresh sessions cost more than the context that they save, or the reverse.
- A tool can report whether a conversation was compacted, so the caller no longer has to say.
- The maintainer decides how a bug in work that exists only on a phase branch is fixed, which changes ADR-0003.
- The project changes the host that runs its sessions, which changes the trailer and the question tool in the prompts.
- The validator allows a phase that is in progress with every work package completed, or the project moves the phase to completed at a
  different step.

## Further reading

- [Plan section 5.8](../plans/implementation-plan.md): AI-paired delivery, and section 6 for the resume and end-of-session protocols.
- [Session boundaries](../explanation/session-boundaries.md): how the check, the policy, and the prompts work together.
- [ADR-0021](0021-governance-auditor-cli.md): the governance command line that the check belongs to.
- [ADR-0003](0003-branch-and-work-package-protocol.md): the branches that the prompts name.
- [AI skills governance](../governance/ai-skills.md): why a skill reports a CLI and never decides.
