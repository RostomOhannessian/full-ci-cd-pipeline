---
title: "Risk register"
description: "The live register of the project's delivery, security, and operational risks, with mitigations, revisit triggers, and the phases that address them."
type: reference
audience: [maintainers, contributors]
last-verified: 2026-10-08
owner: "@RostomOhannessian"
---

# Risk register

This is the live copy of the initial risk list in [section 16 of the implementation plan](../plans/implementation-plan.md). The maintainer reviews it at the end of every phase and whenever a trigger fires. Each phase retrospective records what changed.

## How to read it

- **L** is likelihood and **I** is impact, each rated H (high), M (medium), or L (low).
- **Phases** names the phase plans that carry the mitigation: [0](../plans/phases/phase-0-foundation.md), [1](../plans/phases/phase-1-api-core.md), [2](../plans/phases/phase-2-secure-ci.md), [3](../plans/phases/phase-3-zero-trust-platform.md), [4](../plans/phases/phase-4-contracts-gitops.md), and the [launch gate](../plans/phases/launch-v1.md).
- **Status** is `open` (being managed), `mitigated` (the mitigation is in place and tested), `accepted` (the residual risk is understood and owned), or `closed` (no longer applies, with the reason recorded below the table).

## Register

| # | Risk | L | I | Mitigation | Trigger to revisit | Phases | Status |
| --- | --- | --- | --- | --- | --- | --- | --- |
| R1 | Scope and complexity overload (20+ components) | H | H | Phases and work packages; profiles; spikes; stretch items labeled; strict Definition of Done | WP overruns of more than 2× its size | all | open |
| R2 | Laptop resources insufficient | M | H | `api`/`lite` profiles; registry caches; measured budgets; Codespaces fallback | WP3.0 measurements | 3 | open |
| R3 | Apple Silicon/ARM contributors blocked by x86-only SQL Server | H | M | Support matrix; Codespaces; best-effort API-only path. [Spike 1.d](../research/spikes/1.d-testcontainers-mssql.md) verified Windows x86-64 hands-on only (2026-10-06); Apple Silicon and Windows on Arm remain untested and unsupported per Microsoft's documentation | Support requests | 1, 3 | open |
| R4 | Signature/attestation format incompatibility between producers and Kyverno | M | H | WP2.0 spike; Kyverno CLI contract test in CI; Cosign-native fallback | CLI test failure | 2 | open |
| R5 | Tool churn and deprecation (as with ingress-nginx, tfsec, Bitnami) | M | M | Inventory lifecycle; nightly freshness report; ADR supersession | Upstream announcements | 2 | open |
| R6 | Compromised third-party action or tool | M | H | SHA pinning enforced; zizmor; minimal third-party actions; verified CLIs; immutable releases | Advisories | 2 | open |
| R7 | Vault unseal and trust-anchor custody on laptops | M | M | Operator-custody runbook; disposable environments; `dev platform unseal`; rotation drills | Lost keys | 3 | open |
| R8 | False-positive or false-negative canary analysis | M | M | Synthetic traffic; minimum samples; inconclusive pauses; tests of the analysis templates | Drill results | 4 | open |
| R9 | Schema changes break rollback | M | H | Expand/contract; destructive-op detection; N-1 job | Auditor flags | 4 | open |
| R10 | PactNet dormancy or incompatibility | M | M | WP1.0 spike, which passed on 2026-10-06 ([spike 1.a](../research/spikes/1.a-pactnet-kestrel.md)); pinned version; Pact Rust CLI fallback for verification. Dormancy and the missing `win-arm64` and musl builds remain | Spike failure | 1, 4 | open |
| R11 | Cross-replica cache staleness | L | M | FusionCache backplane, proven against Valkey 8.1.10 and 9.1.2 on 2026-10-06 ([spike 1.b](../research/spikes/1.b-fusioncache-valkey.md)); staleness metrics; two-host tests in WP1.9 | Metric alerts | 1 | open |
| R12 | Kubernetes version skew across components | M | M | Compatibility matrix; pinned node image; scheduled check | Upgrade PRs | 3, 4 | open |
| R13 | Single maintainer: review blind spots and bus factor | H | M | Auditors; AI review; checklists; ADRs; retrospectives; portable status | Bypass frequency | 0 | open |
| R14 | License contamination through dependencies | M | H | Delivered in WP1.1 ([ADR-0020](../adr/0020-dependency-intake-controls.md)): a `nuget-license` allow-list gate over every direct and transitive package, a forbidden-package list checked from the lock files, locked restores, and required signatures. Dependency review follows in WP2.3. Known caveat: Dependabot can leave a downstream lock file stale, which fails the locked restore until `dotnet restore --force-evaluate` regenerates it | Gate failures; the first Dependabot NuGet pull request that fails with NU1004 | 1 | open |
| R15 | Documentation drift | M | M | Tested includes; freshness metadata; inventory discovery | Audit failures | 1 | open |
| R16 | GitHub policy and pricing changes (self-hosted runner fee; feature availability) | L | M | No self-hosted runners by default; capability detection | Changelog | 2 | open |
| R17 | Third-party DNS (`localtest.me`) unavailable | L | L | Documented hosts-file fallback | Lab failures | 3 | open |
| R18 | The replaced initial commit stays retrievable by its ID on GitHub after the force-push | H | M | A one-time rewrite before publication; the repository stays private until the publication checklist passes; the owner confirms the visibility change with the check result in front of them; deleting and recreating the repository or asking GitHub Support for a purge stay available until then ([ADR-0006](../adr/0006-commit-identity-and-privacy.md)) | Before every visibility change (WP0.7 and the launch gate) | 0, launch | closed |
| R19 | GitHub-created commits (merge, squash, web edit) use the account's primary email instead of the repository's noreply identity | H | M | The account setting "Keep my email addresses private" stays on and is verified through the account email API before the first merge; "Block command line pushes that expose my email" stays on; every merged range is checked for non-noreply author and committer emails; the `governance security` auditor gets the same rule in WP1.2 ([ADR-0006](../adr/0006-commit-identity-and-privacy.md)) | A merge or squash commit with a personal email | 0, 1 | open |

## Closed risks

- **R18**, closed 2026-10-04: the repository was recreated before publication, so neither the replaced initial commit nor the merge commit that carried a personal email exists on GitHub ([ADR-0006](../adr/0006-commit-identity-and-privacy.md)).

## Adding, changing, and closing risks

1. Open an Issue with the `risk` label, then add or edit the row in the same pull request that changes the plan or the mitigation.
2. Never reuse a risk ID. A closed risk stays in the table with `closed` and a one-line reason below it.
3. When a trigger fires, record the date and the outcome in the review log and update the mitigation if it did not hold.
4. Describe a risk without including secrets, personal data, or exploit details. Report vulnerabilities privately as described in [SECURITY.md](../../SECURITY.md).

## Review log

| Date | Change |
| --- | --- |
| 2026-10-04 | Initial register from plan version 2.0.1 (R1 to R17), plus R18 recorded during WP0.1 when the replaced initial commit was found to be retrievable by its ID. |
| 2026-10-04 | R18 closed when the repository was recreated before publication. R19 added after the commit-metadata check found that a merge created by GitHub used the account's primary email. |
| 2026-10-06 | WP1.0 spikes completed with no failure. R3, R10, and R11 mitigations updated with the spike evidence. Statuses stay open: R3 and R10 because their causes remain, R11 until WP1.9's two-host tests. |
| 2026-10-08 | WP1.1 delivered the R14 mitigation and found the Dependabot lock-file caveat, which is recorded in R14. R14 stays open until dependency review (WP2.3) and the first Dependabot NuGet pull request. |
