---
title: "ADR-0002: Make the repository public after Phase 0 and design for public-repo capabilities"
description: "Publish the repository at the end of Phase 0 so the plan can use the GitHub controls and licenses that are unavailable on a private Free-plan repository."
status: accepted
date: 2026-10-04
decision-makers: ["@RostomOhannessian"]
supersedes: []
superseded-by: []
tools: ["github-rulesets", "github-secret-scanning", "github-actions", "codeql", "github-attestations", "github-pages"]
related-adrs: ["0003", "0006", "0007", "0013"]
evidence:
  - docs/research/2026-10-04-github-supply-chain-verification.md
  - docs/research/2026-10-04-decision-critical-addendum.md
---

# ADR-0002: Make the repository public after Phase 0 and design for public-repo capabilities

## Context and problem statement

This repository is meant to become both a public teaching resource and a portfolio that demonstrates production
judgment. Plan finding F1 and plan section 4.1 show that those goals conflict with staying private on a personal
GitHub Free plan. While private, the repository cannot rely on public-repo controls such as rulesets, CODEOWNERS
enforcement, secret scanning, dependency review, GitHub Pages, or native artifact attestations, and the live rulesets
API already returns HTTP 403 for this repository.

Plan finding F2 adds a license constraint: CodeQL may not run on a private copy of this codebase. The decision-critical
addendum confirms that the restriction is about running the analysis at all, not only uploading SARIF results later.
That means a private teaching copy cannot truthfully claim the security posture that later phases require.

Plan section 2 says the repository has three equal purposes: learning, teaching, and portfolio evidence. A public
history of work-in-progress is acceptable if it enables the actual controls, workflows, and evidence that those goals
depend on. The polished v1.0 launch remains the end state, but the capability baseline must exist much earlier.

## Decision drivers

- Use rulesets, CODEOWNERS enforcement, secret scanning, dependency review, GitHub Pages, and native attestations as
  documented in plan section 4.1.
- Stay compliant with CodeQL's license terms.
- Make capability gaps explicit for learners who clone the repository privately.
- Preserve a truthful audit trail instead of simulating unavailable features.
- Publish portfolio evidence early enough that later phases can build on it.

## Considered options

1. Make the repository public after Phase 0, with a clear work-in-progress banner
2. Keep the repository private until v1.0
3. Make the repository public after Phase 1
4. Pay for a plan upgrade and keep the repository private

## Decision outcome

Chosen option: **Make the repository public after Phase 0, with a clear work-in-progress banner**, because it is the
first point where the repository can honestly use the GitHub capabilities and license posture the plan requires.

Phase 0 is the earliest acceptable publication point because it publishes the plan, the research record, the ADR set,
community files, and the governance baseline. That makes the repository readable on day one instead of exposing an
empty shell. It also unlocks the Free-plan public-repository feature set that plan section 4.1 depends on.

The repository remains explicitly work in progress until v1.0. The WIP state is communicated in `README.md`, the docs
site, and the publication checklist, but the repository history becomes public immediately after the Phase 0 merge.

Capability-aware workflows are mandatory. When a feature is unavailable, for example on a learner's private fork, the
job prints `SKIPPED: <capability> unavailable` and records the skip in evidence. It never passes silently. CodeQL never
runs on a private copy.

This ADR also sets two operational caution rules. First, switching the repository to public is treated as a one-way
publication step for this plan, because moving back to private would disable the public-repo controls the repository
depends on. Second, making the GHCR package public is treated as one-way operationally as well, because the GitHub
verification record confirmed independent package visibility but found only community-corroborated evidence for
reversal, not an official rollback path.

Keyless signing and public attestations become acceptable only after the repository is public. The GitHub verification
record shows that public transparency data includes workflow identity and repository visibility at signing time, so the
plan accepts that disclosure only in the public state.

### Consequences

- Good: the repository can use real rulesets, CODEOWNERS enforcement, Pages, secret scanning, dependency review, and
  public-repo attestations on the Free plan.
- Good: the portfolio value starts in Phase 0 instead of waiting for a final polish pass.
- Bad: unfinished history, mistakes, and revisions become public; the mitigation is a WIP banner, clear status pages,
  and ADR-backed explanations of why changes happened.
- Bad: learners with private copies cannot run the whole workflow set; the mitigation is capability-aware skips that
  state the missing feature or license explicitly.
- Neutral: the polished v1.0 launch still matters, but it becomes a quality gate on a public repository rather than a
  publication event from scratch.

### Confirmation

WP0.7 confirms this decision through the publication checklist, the settings record, the ruleset activation, secret
scanning enablement, and a full-history gitleaks scan before publication.

WP2.3 confirms the CodeQL and dependency-review consequences by running them only in the public repository and by
making private copies skip explicitly.

WP2.5 confirms the attestation and keyless-signing consequences by publishing evidence only after the repository is
public.

## Pros and cons of the options

### Make the repository public after Phase 0, with a clear work-in-progress banner

- Good: unlocks the required Free-plan public-repo capabilities immediately after the governance and documentation
  baseline exists.
- Good: keeps later phases honest because they run against the same public capability set the portfolio will show.
- Bad: public work-in-progress history is permanent and can be judged out of context.

### Keep the repository private until v1.0

- Good: hides unfinished work and reduces early scrutiny.
- Bad: blocks or weakens the GitHub controls, license posture, and evidence model the plan requires.
- Bad: forces later phases either to simulate missing features or to redesign the plan around private-repo limits.

### Make the repository public after Phase 1

- Good: delays public exposure until there is working application code.
- Bad: still blocks the Phase 0 governance baseline and pushes public-capability validation too late.
- Bad: delays public pages, rulesets, and secret-scanning evidence by an entire phase.

### Pay for a plan upgrade and keep the repository private

- Good: could unlock some private-repository controls earlier.
- Bad: still does not solve every gap. The GitHub verification record shows that private artifact attestations require
  Enterprise Cloud, and private required reviewers and wait timers also remain Enterprise Cloud-only.
- Bad: changes the teaching story from "design within public-repo limits" to "design around paid-plan exceptions."

## Revisit when

- GitHub changes the public-versus-private capability matrix for personal-account repositories.
- CodeQL changes its terms for private repositories.
- The repository changes owner, plan, or hosting model in a way that makes a different capability baseline more honest.
- GitHub publishes an official, reversible path for GHCR visibility that changes the current operational caution.

## Further reading

- Implementation plan v2: [../plans/implementation-plan.md](../plans/implementation-plan.md) (checked 2026-10-04)
- GitHub and supply-chain verification: [../research/2026-10-04-github-supply-chain-verification.md](../research/2026-10-04-github-supply-chain-verification.md) (checked 2026-10-04)
- Decision-critical addendum: [../research/2026-10-04-decision-critical-addendum.md](../research/2026-10-04-decision-critical-addendum.md) (checked 2026-10-04)
- Commit identity and privacy ADR: [./0006-commit-identity-and-privacy.md](./0006-commit-identity-and-privacy.md) (checked 2026-10-04)
