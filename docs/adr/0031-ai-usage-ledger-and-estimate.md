---
title: "ADR-0031: Record AI usage in a validated ledger and estimate the remaining work from it"
description: "Decide how the project records what each AI-paired session costs and estimates the AI units of the remaining phases: a ledger of session slices with a schema, a deterministic command that validates, renders, and estimates, and a skill that reads the local session store."
status: accepted
date: 2026-10-12
decision-makers: ["@RostomOhannessian"]
supersedes: []
superseded-by: []
tools: [governance-auditor, copilot-skills]
related-adrs: ["0021", "0022", "0030"]
evidence:
  - docs/project/ai-usage.yaml
  - docs/project/ai-usage.schema.json
  - docs/project/AI-USAGE.md
  - tools/Governance.Auditor/Usage
  - tools/Governance.Auditor/Cli/UsageCommand.cs
  - tests/Governance.Auditor.Tests/UsageValidationTests.cs
  - tests/Governance.Auditor.Tests/UsageReportTests.cs
  - tests/Governance.Auditor.Tests/Fixtures/usage
  - .github/skills/ai-usage-recorder/SKILL.md
  - docs/explanation/ai-usage-statistics.md
---

# ADR-0031: Record AI usage in a validated ledger and estimate the remaining work from it

## Context and problem statement

One of the main goals of this project is fully AI-paired coding, and every change is made in an AI-paired session
([ADR-0030](0030-session-boundaries-and-ai-paired-delivery.md)). The maintainer asked for statistics on the AI credits that each phase and
work package used, with the model, the effort, and the context, so that the AI credits of the future phases can be estimated. This decision
serves `REQ-AI-005` and plan section 5.8. Most of the history can be recovered from the session store, and every later session records its
own numbers as it goes.

Facts that shape the design (checked 2026-10-12):

- **The data source.** The Copilot app keeps a per-machine SQLite store. Its table `assistant_usage_events` has one row for each model call,
  including the calls that compact a conversation, with the session, the turn, the model, the reasoning effort, the tokens, the AI units, the
  duration, and a flag for the calls of sub-agents. The tables `sessions`, `turns`, and `checkpoints` say when each conversation started,
  when the maintainer spoke, and when a conversation was summarized. The store belongs to one machine, it holds the sessions of every
  repository on it, and nothing in the repository guarantees that it exists on another machine.
- **The token columns.** `input_tokens` includes the cache reads and the cache writes, so the fresh input of a call is
  `input_tokens - cache_read_tokens - cache_write_tokens`. This holds for all 3,255 calls of the sessions of this repository, checked
  against the per-type token counts that each call carries in `token_details_json`. The `input_tokens` of a call is the size of the context at
  that call, so its maximum over a window is the peak context.
- **The AI unit.** `total_nano_aiu` is the sum over the token types of the count times the price of each call, in billionths of an AI unit,
  and it matches that sum for all 3,255 calls. The request multiplier of a model, 15 for the Opus calls, is recorded but does not enter the
  sum, so it does not scale the AIU.
- **The relationship to AI credits and money.** GitHub bills Copilot usage in AI credits, and 1 AI credit is $0.01 USD
  ([usage-based billing for individuals](https://docs.github.com/en/copilot/concepts/billing-and-usage/individuals/billing) and
  [models and pricing](https://docs.github.com/en/copilot/reference/copilot-billing/models-and-pricing), checked 2026-10-12). The store's
  price for a million tokens equals the published price in dollars times 100 for Claude Sonnet 5.5 (200 for input, 10 for cached input,
  250 for cache writes, and 1,000 for output), Claude Sonnet 5, Claude Opus 5.5, and GPT-5.4, so for those models an AIU is an AI credit.
  Two caveats hold. The store's price for GPT-5.6 Sol in August 2026 was 35 percent of the list price that is published now, and the
  cached-input price of Sonnet 5.5 was 20 on 438 calls up to 2026-10-07 and 10 from 2026-10-08, so prices move and an AIU is the cost at the
  time of the call. And the bill depends on the plan, its included allowance, and discounts such as the 10 percent for automatic model selection, which
  the store does not know. The ledger and the report therefore say AIU and hold no money.
- **The sessions are not work packages.** One session held WP1.1 and the start of WP1.2. Another held WP1.4, WP1.5, WP1.6, a plan amendment,
  and the start of two more sessions. The maintainer starts a session for one work package in some cases, and in others the session simply
  continues. Phase 0 was built in one session on one day.
- **Cost is not proportional to size.** On 2026-10-12 the back-fill found 25,761 AIU over 3,228 calls in six sessions. The completed S work
  packages cost 1,337 and 1,474 AIU, the five M packages 1,355 to 1,934, and the one L package 3,271. A session pays to read the repository
  and to rewrite its cache whatever the size.
- **The cache dominates.** The first call after an idle period rewrites the cache of the whole context, at about 100 AIU for a context of
  450,000 tokens, and the calls after it read the cache at about 5 AIU. Idle time and context size matter more than the number of calls.

## Decision drivers

- The numbers must come from the store and not from memory, and a person must be able to check them (`REQ-AI-005`).
- A work package that spans sessions, and a session that spans work packages, must both be expressible, with the honesty of the attribution
  recorded.
- The command must be deterministic and offline, as `governance session` is, so a pull request can be checked on any machine and in CI
  without the store.
- The ledger must hold no personal data: no prompt text, no file paths, no account names (the privacy rules of
  [ADR-0006](0006-commit-identity-and-privacy.md)).
- The estimate must say how weak it is. There are eight completed work packages, and a method that sounds more certain than that would
  mislead the plan.
- An old record must stay as it was, so the history cannot be tuned afterwards.

## Considered options

1. A ledger of session slices with a schema, a deterministic command that validates, renders, and estimates, and a skill that reads the
   store and writes the entries (chosen).
2. A command that reads the session store itself.
3. One row per model call in the repository.
4. One record for each session, or for each work package only.
5. A free-text table in the status page.

## Decision outcome

Chosen option: **a ledger of slices, a deterministic command, and a skill that reads the store**, because it is the only option that is
reproducible in CI, keeps the store (a per-machine file with every repository's sessions) out of the command, and lets a work package and a
session overlap.

The design:

- **The ledger.** `docs/project/ai-usage.yaml`, checked by `docs/project/ai-usage.schema.json`, which rejects an unknown key, so a field for
  text cannot slip in. It has `records`, `changes`, and `waivers`.
- **A record is a slice.** A slice is a window of one session that belongs to one phase or work package and one kind of activity. The kinds
  are `delivery` (the work of a work package), `fix` (a bug fix after a merge), `planning` (work on the plan), and `coordination` (a session that
  starts, monitors, or hands over other sessions). Only `delivery` feeds the estimate. A record holds the session ID, the scope, the
  activity, the confidence, the window (the first and the last call that it holds), the user turns, the compactions, an optional note, and
  the usage rows.
- **A usage row** is one model at one reasoning effort, as the main agent or as sub-agents, in the slice: the calls, the AIU, the fresh
  input, the cache reads, the cache writes, the output and reasoning tokens, the peak context (the largest `input_tokens`), the model
  seconds (the sum of the call durations), and the request multiplier when there is one. The row is where the model and the effort live, and
  the sub-agent share is the share of the rows with the agent `sub-agent`.
- **The size of the change belongs to the work package and not to a slice**, because the pull request is the work package's, and a work
  package can span slices and sessions. It is a `changes` entry with the pull request numbers, the files, the additions, and the deletions.
  A phase can have one too, which Phase 0 does.
- **Confidence.** A slice is `exact` when its session held that scope alone, so the window is the whole session or the whole of a part that
  no other scope touches. It is `windowed` when the session held more than one scope and the window was derived from the user turns, the
  times of the pull request, the commit times, the checkpoints, and the idle gaps, in that order of evidence. Sub-agent calls are attributed
  by time. The rules are in [the explanation page](../explanation/ai-usage-statistics.md).
- **Phase 0 is recorded at phase level.** One session built it on one day before the work packages were tracked one by one, so a waiver
  says that WP0.1 to WP0.7 have no records of their own, and the phase's delivery records have no work package.
- **Append-only.** A session writes only records of its own ID and never edits an older record. Calls after the record was written are
  recorded later as a slice of the same session with a later window.
- **The command.** `governance usage validate`, `render [--check]`, and `estimate [--phase N] [--json]`. It reads `status.yaml`, the ledger,
  and the schema, and it never reads the store, so the same files give the same output on every machine.
- **The consistency rules** that the schema cannot express: the phase and the work package exist, and the work package belongs to the phase
  and is not still planned; the window is ordered and the slices of a session do not overlap; the same slice is not recorded twice, and a row
  is not repeated; a part does not exceed its whole (reasoning tokens within the output, the average input within the peak context, the turns
  and compactions within the calls); an `exact` slice shares its session with no other scope; delivery has a work package unless its phase
  is waived; a change names a work package or phase that exists, with the pull request that `status.yaml` names; and every completed work
  package of a phase that has started has delivery usage, or a waiver with a reason. A completed work package with usage but no size of
  change is a warning.
- **The report.** `AI-USAGE.md` is generated by `usage render` and checked for drift in the `governance` workflow, like `STATUS.md`. It
  shows the totals, the phases, the work packages with their size of change, the AIU for each size class, the models and efforts, and the
  sessions. A session is shown by the first eight characters of its ID.
- **The estimate.** For each work package that remains it gives a range of AIU. A size class with completed work packages is estimated from
  their minimum, median, and maximum, and a size class with none is scaled from the median AIU for each day of weight, with the weights taken
  from the midpoints of plan section 5.7 (S 0.5, M 3, and L 6.5 days). A phase is the sum of its work packages. The command prints the method,
  says when a range is a single point, and lists the caveats: how few observations there are, the model mix, that the AIU does not grow
  with the days of a size class, that the request multiplier does not scale the AIU, how many observations are windowed, and the share of the
  AIU that planning, coordination, and fixes took. It uses no randomness and never blocks a merge, and the `governance` workflow does not
  run it.
- **The skill.** `/ai-usage-recorder` holds the canonical queries, writes the entry, and runs the command. `/session-handoff` runs it
  before the session boundary check, so every session records its own usage.
- **No money in the ledger.** The relationship above is a fact in this decision, and the ledger, the report, and the estimate stay in AIU.

### Consequences

- Good: a session records what it cost in a few minutes, and a reviewer can see the numbers and the attribution of each slice.
- Good: the estimate improves as the ledger grows, and its caveats are printed with it, so nobody reads a range as a promise.
- Good: the cost of a fresh session and of a long one can now be compared, which [ADR-0030](0030-session-boundaries-and-ai-paired-delivery.md)
  named as the reason to revisit its decision.
- Good: the ledger holds opaque IDs and numbers, so it can be public.
- Bad: the numbers depend on a store that belongs to one machine and that the Copilot app may change. A session on another machine cannot
  record. The ledger is what travels, and the skill stops when the store has no rows for the session.
- Bad: the back-filled slices are windowed, and some of their edges are judgments. Five of the eight observed work packages are windowed, and
  each says in its note which fact set its window.
- Bad: AIU is the cost at the time of the call. A price change moves the AIU of the same work, and a model that is not used here yet has no
  history.
- Bad: the estimate has one L and two S observations. It is a planning aid, and its caveats say so.
- Neutral: the skill is the only writer, but the schema, the validator, and the drift check do not trust it. A hand edit that breaks a rule
  fails in CI.

### Confirmation

- **WP1.15** delivers the ledger and its schema, the three commands, the skill, the back-fill, and the tests. `UsageValidationTests` runs
  each rule against a conforming and a violating ledger, `UsageRenderTests` and `UsageEstimateTests` compare the report and the estimate
  with reviewed files, and the self-check tests run the commands over the real ledger and check that it holds no personal data and that every
  completed work package of Phase 1 and the total of Phase 0 have usage.
- The skill tests check the skill's scope and commands, and that the record in the skill validates against the real schema.
- Every later session adds its own records, and `usage validate` fails in CI when a completed work package has none.

## Pros and cons of the options

### A ledger of slices, a command, and a skill

- Good: reproducible, deterministic, reviewable, and honest about attribution.
- Bad: three artifacts to keep correct, and a store that only one machine has.

### A command that reads the session store

- Good: nothing to write by hand.
- Bad: the store is a local file with the sessions of every repository, so the command would not run in CI, would read data that belongs
  to other projects, and would tie the repository to the internal format of one application.

### One row per model call

- Good: nothing is lost.
- Bad: 3,255 rows and growing, each with a timestamp, in a file that a reviewer must read, and the attribution to a work package is still
  needed.

### One record for each session, or for each work package only

- Good: small.
- Bad: a session spans work packages and a work package spans sessions, so either choice hides the thing that the estimate needs.

### A table in the status page

- Good: the cheapest option.
- Bad: nothing checks it, and the project teaches by showing that its rules can fail.

## Revisit when

- The ledger holds enough completed work packages that a size class has five or more observations, so the range can use quartiles.
- The estimate for a phase is more than half again as large as the cost of the last phase, or a work package costs more than twice the
  maximum of its size class (risk R23).
- The session store changes its columns, the Copilot app reports usage in another place, or an `agent_id` no longer marks a sub-agent.
- GitHub changes the price of a model or the meaning of an AI credit, or the maintainer wants money in the report. That is a decision about
  which plan's allowance and discounts to assume.
- The ledger passes a size that is hard to review, so records of finished phases should move to an archive file.

## Update on 2026-11-01: a cloud fallback and derived usage

This record adds to the decision above and does not rewrite it. The decision that the skill reads the local store and stops when it has no
rows held until the session of WP1.7. That session could not record its usage: the local store held no `assistant_usage_events` row after
2026-10-12, so WP1.7 stayed `in_progress`, because `usage validate` rejects a completed work package without usage. The maintainer
approved a fallback to the cloud store, and rejected a waiver for WP1.7, because a waiver loses the data point that the estimate needs.

Facts that shape the fallback (checked 2026-11-01 against both stores):

- **What the cloud store reports.** The `events` rows with `type = 'assistant.usage'` have the model, the tokens (input, output, cache
  read, and cache write), the duration, the initiator, and a request multiplier (`usage_cost`, equal to the local `request_multiplier` for
  every model that both stores hold). They have no AIU, no reasoning effort, and no reasoning tokens. Some events appear twice, so every
  count is by distinct `id`.
- **The AIU can be derived exactly.** The AIU of a call is its tokens times the price of each token type. The 61 calls of the session of the
  squash rule fix, which both stores hold, give 177.8 AIU derived from the cloud tokens and 177.8 AIU stored, with identical token counts.
  The prices of `claude-sonnet-5.5` were 200 (fresh input), 10 (cache read), 250 (cache write), and 1,000 (output) AIU for each million
  tokens on every local row from 2026-10-08 to 2026-10-12. The cache read price was 20 until 2026-10-07, so a price is read from the
  latest local row of the model and its date is recorded.
- **Sub-agents, compactions, and turns.** The cloud store marks the calls of sub-agents with `usage_initiator = 'sub-agent'`, and its counts
  equal the local counts of `agent_id IS NOT NULL` in the 12 sessions that both stores hold in full. A `session.compaction_complete` event
  matches each local compaction call (6 sessions compared). The user turns come from the `user.message` events that an agent did not send,
  and they matched in 4 of 5 sessions (the fifth counted 14 against 10, because system messages started turns that the local store does not
  count). So the cloud store can supply every field of a record except the reasoning effort, the reasoning tokens, and the AIU.
- **The cloud store is not always complete.** One session held 14 of its 196 calls, and the `session_usage` view lagged the raw events by one
  call. The WP1.7 session had 315 calls in the cloud store when it was first measured and 335 later, up to a `session.idle` event, with
  equal counts of its usage events and its successful model calls. A derived record is therefore a floor, and it says so.

Decisions:

- **A usage row has a `source`**, `store` or `derived`, and a row without one is read from the store, so no older record changes and none
  is edited. The schema stays at version 1, because every change is an optional key or a new value, and `usage validate` of the new tool
  accepts every older ledger.
- **A derived row carries the prices that it used and says what it does not know.** Its `reasoning-effort` is `unknown`, and it has no
  `reasoning-tokens`, because absent means unknown and zero would be a claim. `reasoning-tokens` becomes optional in the schema, and
  `usage validate` requires it of a row that is read from the store, so the rule is not loosened for the store. It also refuses prices,
  or an unknown effort, on such a row (`usage-derived-fields`), refuses a derived row without prices (`usage-derived-prices`), and computes
  the AIU again from the tokens and the prices, refusing a difference of more than a thousandth (`usage-derived-aiu`), which holds the
  rounding to three decimals.
- **There is no `agent: unknown`**, because the cloud store marks the calls of sub-agents.
- **Confidence is about the window and not about the numbers.** A session that holds one work package alone is `exact`, and its usage may
  still be `derived`, so the two words do not conflict. The report and the estimate show the derived share on their own.
- **The report and the estimate show the derived share.** `AI-USAGE.md` gets a line in the totals, a column for each phase and each work
  package, and a row with an unknown effort in the table of models. The estimate prints the share of each observation that is derived,
  adds a caveat that a derived AIU is a floor, and still uses it. It never blocks a merge.
- **The skill uses the cloud store only when the local store has no rows for the session**, takes the prices from the local rows of the same
  model, and stops when neither store has rows or when the local store has no row of the model (a price is never guessed). The sentence
  above that the skill stops when the store has no rows now means both stores.

Consequences:

- Good: a session whose local rows are gone can still record, the data point for the estimate is kept, and the record says what it is.
- Good: the validator checks the arithmetic, so a derived AIU cannot be tuned by hand.
- Bad: a derived record can count too few calls, so the estimate leans low for a work package that rests on it, and the reasoning effort of such a
  session stays unknown.
- Bad: the prices are those of the latest local row, so a price that changed after it would make a derived AIU wrong by that change, which
  the validator cannot see. The note carries the date of the prices.
- Neutral: the threat model does not gain a boundary. The skill reads a second copy of the same account's session history with the same
  read-only tool, selects only numbers and counts, writes only opaque IDs and numbers, and the ledger tests that forbid text and paths
  still hold. The row of the threat model that names the store now names both.

## Further reading

- [Plan section 5.8](../plans/implementation-plan.md): AI-paired delivery and the usage statistics.
- [AI usage statistics](../explanation/ai-usage-statistics.md): how to read the ledger, the attribution rules, the report, and the estimate.
- [ADR-0030](0030-session-boundaries-and-ai-paired-delivery.md): the session boundaries that the slices follow.
- [ADR-0021](0021-governance-auditor-cli.md): the governance command line that the command belongs to.
- [AI skills governance](../governance/ai-skills.md): why a skill reads and writes only what its scope allows.
