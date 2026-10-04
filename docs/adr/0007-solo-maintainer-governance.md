---
title: "ADR-0007: Govern the repository with rulesets, CODEOWNERS, and documented bypass"
description: "Use public-repository rulesets, CODEOWNERS, advisory AI review, and narrowly documented bypass for the solo-maintainer case."
status: accepted
date: 2026-10-04
decision-makers: ["@RostomOhannessian"]
supersedes: []
superseded-by: []
tools: ["github-rulesets", "copilot-code-review", "dependabot"]
related-adrs: ["0002", "0003", "0006", "0013"]
evidence:
  - docs/research/2026-10-04-github-supply-chain-verification.md
  - docs/research/2026-10-04-decision-critical-addendum.md
---

# ADR-0007: Govern the repository with rulesets, CODEOWNERS, and documented bypass

## Context and problem statement

This repository is maintained by one person, but it still needs meaningful change control for architecture, supply
chain, and production-path files. Plan risk R13 calls out single-maintainer risk, and plan section 5.3 defines the
governance baseline that later phases depend on.

The GitHub verification record and the decision-critical addendum make the enabling constraint explicit: on a personal
Free plan, the required rulesets and CODEOWNERS enforcement are not available while the repository is private, and the
live rulesets API already returns HTTP 403 there. ADR-0002 therefore makes the repository public after Phase 0 so these
controls can exist at all.

Governance here has to do two things at once. It must protect sensitive paths and release flows, and it must still let
a solo maintainer ship when no independent approver exists. A policy that assumes a two-person team would be honest in
the abstract but unworkable in practice.

## Decision drivers

- Enforce pull-request review and required checks on the public repository.
- Protect sensitive paths with CODEOWNERS review where that feature is available.
- Keep the solo-maintainer exception narrow, auditable, and documented.
- Avoid fake review theater such as reviewing with a second personal account.
- Preserve automation for routine updates without giving bots unrestricted authority over production paths.

## Considered options

1. Public-repository rulesets, CODEOWNERS, and documented bypass for the solo-maintainer case
2. No branch protection or rulesets
3. Mandatory second approver for all sensitive changes
4. A second personal account acting as the reviewer

## Decision outcome

Chosen option: **Public-repository rulesets, CODEOWNERS, and documented bypass for the solo-maintainer case**, because
it provides real control on a public Free-plan repository without pretending that a second human reviewer exists.

The governance baseline starts once the repository is public. The GitHub verification record supports the feature
availability that this ADR depends on: public repositories on the current plan can use rulesets and CODEOWNERS
enforcement, while the private repository cannot. The addendum records the live HTTP 403 result that justified the
visibility change.

The `master` ruleset requires pull requests, required status checks, resolved conversations, and no force-pushes or
deletions. It also requires CODEOWNERS review for `.github/**`, `governance/**`, `policies/**`,
`infra/**`, `k8s/apps/*/overlays/prod/**`, `contracts/**`, and `src/**/Migrations/**`.

The `phase/**` ruleset is lighter: pull requests, required status checks, and no force-pushes. That keeps phase work
reviewable without making every integration branch carry the full production-path policy.

The bypass rule is narrow. The owner may bypass a pull-request rule only when CODEOWNERS self-review is the only unmet
requirement. The repository treats every such bypass as a governance event that must stay visible in the settings
record and related evidence. Bot-authored promotion pull requests that touch production overlays do not get that
exception; they need a real owner approval.

Signed commits are recommended for human commits but not required by the ruleset, because release and promotion flows
include bot-authored commits. Copilot code review remains advisory and is guided by `REVIEW.md`; findings are fixed or
answered before merge, but Copilot review does not replace the required checks. Issues and Discussions are welcome
immediately, while external pull requests are accepted after v1.0.

### Consequences

- Good: the repository gets real, enforceable governance instead of informal promises.
- Good: the bypass path is constrained to the one case a solo maintainer cannot avoid honestly.
- Bad: the owner still has exceptional power; the mitigation is to narrow the condition, document it, and preserve the
  evidence trail around each use.
- Bad: CODEOWNERS protection applies only after the repository is public; the mitigation is Phase 0 publication before
  the repository starts carrying sensitive implementation work.
- Neutral: AI review improves feedback quality, but it remains advisory rather than a merge authority.

### Confirmation

WP0.7 confirms this decision by publishing the ruleset JSON under `governance/github/rulesets/`, enabling the settings,
and recording the repository configuration.

WP1.2 confirms it continuously through the governance auditor and the pull-request checks that enforce branch, review,
and workflow policy.

Later phases confirm the production-path rule whenever promotion pull requests and migration changes follow the
CODEOWNERS paths defined here.

Execution record (WP0.7, 2026-10-04): the two rulesets were created from governance/github/rulesets/, and GitHub
reports the owner's bypass as pull_requests_only on both. A direct push to master was rejected. The owner's own
pull request that changed governance/ became mergeable (mergeStateStatus CLEAN) as soon as the three required
checks passed, so the code owner rule did not block it and no bypass was needed. Pull requests from other authors,
such as Dependabot and the promotion bot, still need the owner's review. GitHub cannot limit a bypass to a single rule,
so using it for anything other than a code owner review remains a policy exception that GitHub records.

## Pros and cons of the options

### Public-repository rulesets, CODEOWNERS, and documented bypass for the solo-maintainer case

- Good: uses real platform controls that match the repository's public capability set.
- Good: acknowledges the solo-maintainer reality without inventing a fake reviewer.
- Bad: still depends on disciplined evidence capture around the exceptional bypass path.

### No branch protection or rulesets

- Good: zero operational friction.
- Bad: no enforceable review, no required checks, and no protected sensitive paths.
- Bad: directly conflicts with the repository's supply-chain and trust-boundary goals.

### Mandatory second approver for all sensitive changes

- Good: strongest theoretical review gate in a multi-maintainer team.
- Bad: impossible to satisfy honestly with one maintainer.
- Bad: would block the repository or push work into off-platform workarounds.

### A second personal account acting as the reviewer

- Good: superficially satisfies a "second approval" checkbox.
- Bad: not a real review and therefore rejected as governance theater.
- Bad: increases account-management risk without adding independent judgment.

## Revisit when

- The repository gains another active maintainer who can provide real review on sensitive changes.
- GitHub changes the ruleset or CODEOWNERS feature set for public personal-account repositories.
- The documented bypass path is used more often than intended or proves too broad in retrospectives.

## Further reading

- Implementation plan v2: [../plans/implementation-plan.md](../plans/implementation-plan.md) (checked 2026-10-04)
- GitHub and supply-chain verification: [../research/2026-10-04-github-supply-chain-verification.md](../research/2026-10-04-github-supply-chain-verification.md) (checked 2026-10-04)
- Decision-critical addendum: [../research/2026-10-04-decision-critical-addendum.md](../research/2026-10-04-decision-critical-addendum.md) (checked 2026-10-04)
- Repository visibility ADR: [./0002-repository-visibility-and-capability-strategy.md](./0002-repository-visibility-and-capability-strategy.md) (checked 2026-10-04)
