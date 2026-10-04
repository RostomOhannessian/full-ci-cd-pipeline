---
title: "ADR-0003: Use phase branches and work-package branches with master as the default"
description: "Develop each phase on its own integration branch, merge work packages into that branch, and merge phases into master with a merge commit."
status: accepted
date: 2026-10-04
decision-makers: ["@RostomOhannessian"]
supersedes: []
superseded-by: []
tools: ["github-actions", "git-cliff"]
related-adrs: ["0001", "0002", "0007"]
evidence: []
---

# ADR-0003: Use phase branches and work-package branches with master as the default

## Context and problem statement

Plan sections 5.1 to 5.4 define a delivery model for a long, multi-phase build-out of an empty repository. The scope is
too large for one long-lived branch and too interconnected for unrelated, short-lived feature branches with no phase
structure. Plan gaps G1 and G2 show why: the work must stay portable across machines, and one pull request per phase
would be too large to review or resume safely.

The repository is also a teaching artifact. Its history needs to tell a readable story to learners who inspect the
commit graph, the phase milestones, and the generated changelog. A clean `git log --first-parent master` matters here
because it doubles as documentation.

The maintainer also requires each phase to develop on its own branch and to merge back to `master` only when the phase
exit gate passes. That requirement rules out a pure trunk model for the main body of work.

## Decision drivers

- Keep work packages small enough to review, test, and resume.
- Preserve one integration line per phase without losing a simple default branch.
- Produce a readable phase narrative on `master`.
- Support hotfixes, dependency updates, and spike work without mixing them into unfinished phase history.
- Keep release tagging and changelog generation straightforward.

## Considered options

1. Phase branches with work-package branches and structured merges
2. Trunk-based development with feature flags
3. One pull request per phase
4. GitFlow

## Decision outcome

Chosen option: **Phase branches with work-package branches and structured merges**, because it balances reviewability,
teaching value, and solo-maintainer control better than the alternatives.

The default branch remains `master`. Phase 0 is docs-only. Its branch, `phase/0-foundation`, is merged into `master` through a single pull request with a
merge commit. Each later phase has one integration branch:
`phase/1-api-core`, `phase/2-secure-ci`, `phase/3-zero-trust-platform`, and `phase/4-contracts-gitops`.

Each work package in phases 1 to 4 uses its own branch named `wp/<phase>.<nn>-<slug>`. A work-package pull request
targets the active phase branch and is squash-merged with a Conventional Commit title. The phase branch then merges to
`master` with a merge commit after the exit gate and retrospective are complete.

Spike work uses `spike/<phase>.<x>-<slug>` and is never merged directly. Its findings land through the owning work
package as docs, tests, or ADR updates. Hotfixes use `fix/<slug>` directly against `master` and are then merged back
into the active phase branch.

`master` is merged into the active phase branch by pull request at least weekly and before the phase pull request. That
rule keeps Dependabot updates, publication changes, and urgent fixes from drifting too far from in-flight work.

### Consequences

- Good: work packages stay small, phase integration stays deliberate, and `master` tells a clean phase-by-phase story.
- Good: release notes and changelog generation align with merge boundaries instead of reconstructing history later.
- Bad: branch count is higher than in trunk-based development; the mitigation is strict naming, sizing, and branch roles.
- Bad: active phase branches can drift from `master`; the mitigation is the weekly merge-back rule and the pre-phase-PR
  sync requirement.
- Neutral: spike work leaves evidence in the repository, but not in the main commit graph as merged code.

### Confirmation

Plan section 5.1 confirms the branch names, merge style, and release points.

`CONTRIBUTING.md`, the pull-request template, and the rulesets described in plan sections 5.2 and 5.3 confirm the
workflow expectations for work-package, phase, hotfix, and promotion pull requests.

WP1.1 confirms the Conventional Commit title rule through a pull-request title check, and WP1.2 confirms governance
through the branch and workflow auditors.

## Pros and cons of the options

### Phase branches with work-package branches and structured merges

- Good: matches the repository's phase-based plan and keeps each work package independently reviewable.
- Good: supports a readable first-parent history on `master`.
- Bad: needs disciplined merge cadence and naming rules to avoid drift and clutter.

### Trunk-based development with feature flags

- Good: simpler branch model and faster integration in teams with high merge frequency.
- Bad: does not fit the maintainer's requirement that each phase develop on its own branch and merge to `master` only at
  an exit gate.
- Bad: makes the phase narrative harder to teach from the commit graph alone.

### One pull request per phase

- Good: the branch model is easy to explain.
- Bad: the review unit becomes far too large for this repository's scope.
- Bad: failures late in a phase would block too much work and reduce portability across sessions.

### GitFlow

- Good: mature naming conventions and explicit release branches.
- Bad: heavier than needed for a single maintained product with one main integration line.
- Bad: adds lifecycle branches that do not improve the teaching or portfolio goals here.

## Revisit when

- Phase work packages routinely exceed the intended S, M, and L size bands.
- The repository gains more maintainers and a different review cadence makes a simpler branch model practical.
- Release management needs patch or support branches that the current phase model does not cover well.

## Further reading

- Implementation plan v2: [../plans/implementation-plan.md](../plans/implementation-plan.md) (checked 2026-10-04)
- Repository visibility ADR: [./0002-repository-visibility-and-capability-strategy.md](./0002-repository-visibility-and-capability-strategy.md) (checked 2026-10-04)
- Solo-maintainer governance ADR: [./0007-solo-maintainer-governance.md](./0007-solo-maintainer-governance.md) (checked 2026-10-04)
