---
title: "ADR-{{NNNN}}: {{Decision title in the imperative}}"
description: "{{One sentence: the decision and the main reason for it.}}"
status: proposed # proposed | accepted | rejected | deprecated | superseded
date: {{YYYY-MM-DD}}
decision-makers: ["@RostomOhannessian"]
accepted-in: "{{Work package that validates and accepts this decision, for example WP3.3. Omit when already accepted.}}"
supersedes: []
superseded-by: []
tools: [{{inventory-ids}}]
related-adrs: []
evidence:
  - docs/research/{{record}}.md
---

# ADR-{{NNNN}}: {{Decision title in the imperative}}

<!--
Write for an intermediate or advanced developer who knows the language syntax but not this tool or pattern.
Keep each section short and concrete. Every claim that could go stale needs a dated source.
Do not link to tool pages (docs/reference/tools/*): they are created when a tool is first used.
-->

## Context and problem statement

{{What problem are we solving, in this repository, right now? Include the constraints that make the problem hard.
Name the requirements this decision serves.}}

## Decision drivers

- {{Driver 1: a testable need, not a preference.}}
- {{Driver 2.}}

## Considered options

1. {{Option A (chosen)}}
2. {{Option B}}
3. {{Option C}}

## Decision outcome

Chosen option: **{{Option A}}**, because {{the decisive reasons, tied to the drivers}}.

### Consequences

- Good: {{benefit}}
- Bad: {{cost or risk, and its mitigation}}
- Neutral: {{effect that is neither}}

### Confirmation

{{How we will know the decision holds: the tests, spikes, policies, or CI checks that verify it, with work-package IDs.}}

## Pros and cons of the options

### {{Option A}}

- Good: {{...}}
- Bad: {{...}}

### {{Option B}}

- Good: {{...}}
- Bad: {{...}}

## Revisit when

- {{A concrete trigger, for example an upstream release, a license change, or a failed spike.}}

## Further reading

- {{Title}}: {{URL}} (checked {{YYYY-MM-DD}})
