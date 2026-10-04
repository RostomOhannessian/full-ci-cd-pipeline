---
title: "ADR-0001: Record architecture decisions in repository ADR files"
description: "Record every significant architecture, tool, trust-boundary, and process decision as an ADR in the repository."
status: accepted
date: 2026-10-04
decision-makers: ["@RostomOhannessian"]
supersedes: []
superseded-by: []
tools: []
related-adrs: ["0003", "0005", "0007"]
evidence: []
---

# ADR-0001: Record architecture decisions in repository ADR files

## Context and problem statement

Phase 0 publishes the plan, research, and governance for an empty repository that is meant to teach, to document
engineering judgment, and to stay portable across machines and sessions. Plan sections 2, 5, 6, and 11 make the
documentation itself a deliverable, not a cleanup step, and plan gap G10 closes the v1 weakness where ADRs appeared
late instead of when decisions were made.

This repository will make decisions that affect architecture, trust boundaries, dependency policy, tool choice, and
working agreements. Those decisions need durable context, clear status, and links to the evidence that justified them.
Without a repository-local record, later work packages would inherit conclusions without the tradeoffs that produced
them.

The repository also needs a consistent rule for when a decision becomes binding. Plan section 5.4 makes an ADR a
Definition of Done item for any pull request that makes or changes a significant decision. Proposed technology choices
must carry their acceptance point, and accepted process decisions must remain stable enough to anchor later phases.

## Decision drivers

- Keep architectural reasoning versioned with the code, plan, and tests.
- Make decisions reviewable in pull requests and readable in `git log --first-parent`.
- Distinguish between proposed, accepted, rejected, deprecated, and superseded decisions.
- Prevent silent edits to accepted decisions that would rewrite history instead of documenting change.
- Give AI-assisted work a repeatable way to notice when a pull request needs an ADR.

## Considered options

1. ADR files in `docs/adr/`, using the repository template
2. Wiki pages outside the repository history
3. Issues and pull-request descriptions only

## Decision outcome

Chosen option: **ADR files in `docs/adr/`, using the repository template**, because they satisfy the drivers with the
least ambiguity and the best audit trail.

Every significant decision is recorded as one file under `docs/adr/` with a four-digit number and a stable,
kebab-case filename. An ADR is required whenever a pull request changes architecture, a trust boundary, a dependency
policy, a tool selection, or a project process.

ADR statuses are `proposed`, `accepted`, `rejected`, `deprecated`, and `superseded`. Proposed technology ADRs name the
work package that can validate and accept them through front matter `accepted-in`. Accepted ADRs are not rewritten for
substance later. They may only change status metadata or gain cross-links, and any new direction is recorded in a new
ADR that supersedes the old one.

The numbering rule is sequential within `docs/adr/`. Filenames match the decision topic so links remain stable after
publication. Pull requests that make a qualifying decision must create a new ADR or update the relevant proposed ADR as
part of the same change, which implements Definition of Done item 6 from plan section 5.4.

The repository also adopts the `adr-assistant` skill as the preferred reminder and checklist for AI-assisted pull
requests that might need an ADR. The skill does not replace human judgment; it reduces missed records.

### Consequences

- Good: decision context stays close to the work and ships with the repository.
- Good: the ADR status model gives later phases a clear way to distinguish hypotheses from validated choices.
- Bad: writing ADRs adds friction to small pull requests; the mitigation is a narrow trigger rule that limits ADRs to
  significant decisions, not routine edits.
- Bad: accepted ADRs can become stale if people edit them in place; the mitigation is the rule that accepted ADRs are
  never substantively rewritten and must instead be superseded.
- Neutral: some decisions will appear first as `proposed` and stay that way until a later work package proves them.

### Confirmation

Plan section 5.4 confirms this decision through Definition of Done item 6: every pull request that makes a significant
decision must create or update an ADR.

Phase 0 confirms the repository mechanics by publishing this accepted ADR set and by using the common template from
`docs/templates/adr-template.md`.

WP1.2 confirms the operational rule by auditing pull requests for missing governance artifacts, and later work packages
confirm it by accepting or superseding the proposed technology ADRs they validate.

## Pros and cons of the options

### ADR files in `docs/adr/`, using the repository template

- Good: versioned with the repository, easy to review in pull requests, and easy to cite from code, docs, and tests.
- Good: supports stable status transitions, evidence links, and supersession without losing history.
- Bad: adds a small authoring burden and requires discipline about numbering and scope.

### Wiki pages outside the repository history

- Good: low setup cost and easy casual editing.
- Bad: weaker change history, weaker review path, and easy drift from the code and plan.
- Bad: harder to snapshot for a portfolio repository and harder to keep portable across machines.

### Issues and pull-request descriptions only

- Good: decisions stay close to the discussion that produced them.
- Bad: no durable lifecycle model, poor discoverability, and no stable canonical location after merges.
- Bad: reasoning fragments across threads instead of building a coherent decision record.

## Revisit when

- The repository replaces the ADR template with a materially different decision-record format.
- Pull-request audits show that the trigger rule is too broad or too narrow to apply consistently.
- A later governance ADR finds that accepted ADRs need a different lifecycle or numbering rule.

## Further reading

- Implementation plan v2: [../plans/implementation-plan.md](../plans/implementation-plan.md) (checked 2026-10-04)
- Documentation system ADR: [./0005-documentation-system.md](./0005-documentation-system.md) (checked 2026-10-04)
- Solo-maintainer governance ADR: [./0007-solo-maintainer-governance.md](./0007-solo-maintainer-governance.md) (checked 2026-10-04)
