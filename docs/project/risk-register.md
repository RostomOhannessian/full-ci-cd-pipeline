---
title: "Risk register"
description: "The live register of the project's delivery, security, and operational risks, with mitigations, revisit triggers, and the phases that address them."
type: reference
audience: [maintainers, contributors]
last-verified: 2026-10-12
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
| R15 | Documentation drift | M | M | Tested includes; freshness metadata; inventory discovery. From WP1.3 the documentation auditor enforces the front matter, the sections of each tool page, the command-block policy, and a two-way comparison of the files with the tool inventory ([ADR-0023](../adr/0023-documentation-auditor.md)), and the site build treats a DocFX warning as an error | Audit failures | 1 | open |
| R16 | GitHub policy and pricing changes (self-hosted runner fee; feature availability) | L | M | No self-hosted runners by default; capability detection | Changelog | 2 | open |
| R17 | Third-party DNS (`localtest.me`) unavailable | L | L | Documented hosts-file fallback | Lab failures | 3 | open |
| R18 | The replaced initial commit stays retrievable by its ID on GitHub after the force-push | H | M | A one-time rewrite before publication; the repository stays private until the publication checklist passes; the owner confirms the visibility change with the check result in front of them; deleting and recreating the repository or asking GitHub Support for a purge stay available until then ([ADR-0006](../adr/0006-commit-identity-and-privacy.md)) | Before every visibility change (WP0.7 and the launch gate) | 0, launch | closed |
| R19 | GitHub-created commits (merge, squash, web edit) use the account's primary email instead of the repository's noreply identity | H | M | The account setting "Keep my email addresses private" stays on and is verified through the account email API before the first merge; "Block command line pushes that expose my email" stays on; every merged range is checked for non-noreply author and committer emails; the `commit-identity` rule of `governance security` checks the commits of every pull request from WP1.2 ([ADR-0006](../adr/0006-commit-identity-and-privacy.md)). The merge or squash commit exists only after the merge, so the check after every merge stays | A merge or squash commit with a personal email | 0, 1 | open |
| R20 | A pull request weakens the governance auditors that audit it, because the auditor is built from the pull request's own source | M | H | The blast-radius map flags a change under `tools/Governance.Auditor/` for a close human read; the rules and policies are covered by tests that run each rule against a conforming and a violating fixture; the policy files sit under `governance/`, which CODEOWNERS covers; the maintainer decides whether to add `tools/Governance.Auditor/` to CODEOWNERS ([ADR-0021](../adr/0021-governance-auditor-cli.md)). A later option is to run the base branch's copy of the tool | A change under `tools/Governance.Auditor/` or `governance/policies/` that removes or relaxes a rule | 1, 2 | open |
| R21 | An API that trusts the `scope` claim lets a user escalate, because Keycloak 26.8.0 grants a requested scope to a user who lacks the role that the scope maps to | M | H | The catalog realm puts `realm_access.roles` in the token, limited to the user's own roles and the requested scopes; `dev compose check` proves it for the three demo users; the API must require the role as well as the scope (WP1.7 and WP1.10), and `Catalog.Identity.ContractTests` prove it against the real realm file (WP1.10). The maintainer decides whether to amend plan section 8.7 ([ADR-0026](../adr/0026-api-only-compose-stack-and-catalog-realm.md)) | An authorization test that passes on the scope alone; a Keycloak release that changes scope gating | 1, 3 | open |
| R22 | The Dev Container runs privileged, because docker-in-docker needs it, so code in it could reach the host's Docker VM | L | M | The daemon is private to the container and no host socket is mounted; the stack binds only to loopback; Codespaces runs the container in a throwaway virtual machine; the threat model lists the boundary; the base image, the features, and the action are pinned and reviewed ([ADR-0025](../adr/0025-dev-command-and-dev-container.md)) | A feature release that changes its privilege model; a move to rootless Docker | 1 | accepted |
| R23 | The AI credits that the remaining phases need are unknown, so the delivery could outrun its budget or stop in the middle of a phase, and a long session could lose detail when its context is compacted | M | M | Every session records its AI units, model, effort, and context in the ledger, a generated report and an estimate cover each planned work package, and a deterministic check starts a fresh session at each work-package and phase boundary, which keeps the context small (plan section 5.8, WP1.14, WP1.15) | An estimate for a phase that is more than half again as large as the last phase; a work package whose cost is more than twice its size class | 1 | open |

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
| 2026-10-08 | R19 mitigation updated: WP1.2 delivered the `commit-identity` rule. R20 added: the auditors are built from the pull request's own source. |
| 2026-10-06 | WP1.0 spikes completed with no failure. R3, R10, and R11 mitigations updated with the spike evidence. Statuses stay open: R3 and R10 because their causes remain, R11 until WP1.9's two-host tests. |
| 2026-10-08 | WP1.1 delivered the R14 mitigation and found the Dependabot lock-file caveat, which is recorded in R14. R14 stays open until dependency review (WP2.3) and the first Dependabot NuGet pull request. |
| 2026-10-10 | WP1.4 found that Keycloak 26.8.0 grants a requested scope to a user without the mapped role (R21, open), and accepted the privileged Dev Container as a residual risk (R22). R2 and R3 gained the first measured `api` figures and the host check in `dev doctor`, and stay open until WP3.0. |
