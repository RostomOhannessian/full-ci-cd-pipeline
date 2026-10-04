---
title: "Phase 4: Contract testing, GitOps, and progressive delivery"
description: "Versioned, consumer-verified contracts and fully declarative delivery: one signed digest moves from dev to staging to prod through analyzed canaries, with rollback rehearsed."
type: phase-plan
phase: 4
audience: [maintainers, learners]
last-verified: 2026-10-04
owner: "@RostomOhannessian"
---

# Phase 4: Contract testing, GitOps, and progressive delivery

Section numbers (§) in this file refer to [the implementation plan](../implementation-plan.md). Live progress is on [the status page](../../project/STATUS.md).

| Field | Value |
| --- | --- |
| Phase branch | `phase/4-contracts-gitops` |
| Work-package branches | `wp/4.NN-<slug>` |
| Milestone | Phase 4: Contracts and GitOps |
| Release at exit | `v0.4.0` |
| Depends on | Phase 3 merged |

## Goal

contracts that are versioned and verified by consumers, plus fully declarative delivery. One signed digest moves from dev to staging to prod through analyzed canaries, with automated and manual rollback rehearsed.

## Learning outcomes

- API evolution and compatibility
- Consumer-driven contracts
- Property-based API testing
- GitOps reconciliation
- Progressive delivery and SLO-based analysis
- Promotion orchestration
- Rollback engineering

## Scope and non-goals

In scope:

- Contract governance: a versioning and deprecation policy, a breaking-change gate, a generated client, and property-based API testing.
- Consumer-driven contracts with Pact, verified against the real API.
- The GitOps control plane, progressive delivery with analyzed canaries, and Kargo promotion across dev, staging, and prod.
- Rollback and resilience drills, the final portfolio documentation, and the end-to-end audit.

Non-goals:

- No multi-cluster promotion. Environments are namespaces in one cluster, with overlays that stay portable to separate clusters.
- No hosted Pact Broker. A self-hosted broker is an optional lab in the `full` profile.
- No provider-specific cloud code. The extension seams are documented only.
- No shipped `v2` API. The breaking-change lab uses an unmerged branch so `v1` remains the only supported version.
- No service mesh. Canary traffic uses Gateway API weights.

## Decisions introduced

| ADR | Decision | Accepted in |
| --- | --- | --- |
| [0011](../../adr/0011-gitops-vs-traditional-cicd.md) | Pull-based GitOps with Argo CD | WP4.3 |
| [0010](../../adr/0010-promotion-engine-kargo.md) | Kargo as the promotion engine | WP4.5 |

New ADRs expected: the canary analysis design and its minimum-sample policy (WP4.4), and the emergency rollback procedure (WP4.6).

## Prerequisites and setup changes

- Phase 3 is merged and the platform runs. `lite` can exercise dev only; the full promotion path needs `full`.
- The `catalog-promoter` and `catalog-deploy-reporter` GitHub Apps, created from manifests kept in `governance/github/`, each limited to this repository and the minimum permissions.
- Rulesets tuned for bot-authored promotion pull requests: required checks apply, and a real owner approval is required for production paths.
- Argo CD reads the public repository anonymously, so no repository credential is stored.

## Work packages

| WP | Title | Size | Key deliverables |
| --- | --- | --- | --- |
| 4.0 | Risk spikes | S | Argo Rollouts Gateway API plugin with Envoy Gateway 1.9 on the pinned Kubernetes version. Kustomize image transformer configuration for Rollouts. Kargo with the promoter App under real rulesets (`git-merge-pr wait` respects required checks; `git-wait-for-pr` for prod). Argo CD 3.x defaults (`logs` RBAC, resource exclusions) with Kyverno reports. |
| 4.1 | Contract governance | M | Versioning and deprecation policy (`Deprecation`/`Sunset` headers, support window). oasdiff breaking-change gate against the last released contract tag. API changelog. Deterministic Kiota generation of `Catalog.Client`, with drift check. Schemathesis property-based tests against the running API (Compose/Testcontainers) in CI. A lab that walks through a controlled breaking change via a `v2` operation on a lab branch, kept unmerged so v1 stays the only supported version. |
| 4.2 | Consumer and Pact | M | `Catalog.Client` facade. `Catalog.SyntheticShopper`: a read-heavy, realistic mixed workload; client credentials from Vault; configurable rate. Pact consumer tests: create, get, list/paginate, update with `If-Match`, conflict, precondition failures, unauthorized/forbidden, not found, discontinue, idempotent replay. Provider verification uses `WebApplicationFactory.UseKestrel()` with Testcontainers SQL/Valkey, and a provider-state middleware registered only by the test host and absent from production builds. Pact files versioned by Git SHA and branch. Verification gates image publication. Optional self-hosted Pact Broker in the `full` profile with a `can-i-deploy` lab. |
| 4.3 | GitOps control plane | M | Terraform `gitops` stack installs Argo CD 3.x (Keycloak SSO, local admin disabled, RBAC including `logs, get`), Argo Rollouts plus the Gateway API plugin, and Kargo (SSO). Root Application → `/k8s` (`k8s/bootstrap`): AppProjects per environment (restricted repository, destinations, and resource kinds), ApplicationSet over `k8s/apps/*/overlays/*`, and an application for `k8s/promotion`. Automated sync with self-heal and prune. Argo CD notifications report GitHub Deployments through `catalog-deploy-reporter`. The temporary Phase 3 deploy script is removed. *GitOps vs. traditional CI/CD* ADR accepted. |
| 4.4 | Progressive delivery | L | Deployment converted to a Rollout. Canary steps (5% → 25% → 50% → 100%) routed by Gateway API weights. AnalysisTemplates: 5xx rate, p95 latency, cache hit ratio, outbox age; minimum-sample guards; inconclusive results pause the rollout; a k6 job runs in each analysis window. PreSync migration job under the expand/contract policy. PostSync smoke tests. Synthetic shopper deployed per environment. N-1 schema compatibility job. SLO documentation. |
| 4.5 | Promotion pipeline | L | `catalog-promoter` GitHub App created from a manifest (permissions as code). Its key goes into Vault KV and is synced by Vault Secrets Operator into the Kargo project namespace only. Kargo Project, Warehouse (GHCR digests from `master` builds), and Stages: `dev` (auto), `staging` (auto after dev verification), `prod` (manual promotion; PR requires CODEOWNERS approval). Promotion steps: `git-clone` → `kustomize-set-image` → `git-commit` → `git-push` → `git-open-pr` → `git-merge-pr` (`wait: true`) for dev/staging, or `git-wait-for-pr` for prod → `argocd-update`, then a verification AnalysisTemplate. Rulesets and CODEOWNERS tuned for bot PRs. Audit evidence capture. |
| 4.6 | Rollback and resilience drills | M | Automated rollback (bad canary aborts). Manual rollback (re-promote previous Freight; revert PR). Emergency rollback (disable auto-sync → `argocd app rollback` → reconciliation PR → re-enable). Failure drills: failed migration, signature rejection, Argo CD and Kargo outages, Vault sealed, Valkey down. Each drill automated in Chainsaw or scripts where feasible. Every drill backed by a runbook and evidence. |
| 4.7 | Portfolio and documentation completion | L | Final README: narrative; capability → evidence table; Mermaid diagrams covering Clean Architecture boundaries, runtime topology, trust boundaries, supply chain, and GitOps promotion, with links to the API, MS SQL, CI, and GitOps. All ADRs finalized, including the four brief ADRs. Final threat model and controls matrix. Walkthrough and demo script. Every tool page complete. Every learning path complete. Doc covering extension seams for managed Kubernetes, managed SQL, and cloud IAM (no provider code). Retrospective. |
| 4.8 | Final end-to-end audit | M | Full validation matrix (§15) run from fresh environments (Codespaces, Linux, Windows). Rollback drills. Evidence bundle. Phase PR. `v0.4.0`. |

## Test plan

| Check | What it proves | Gate |
| --- | --- | --- |
| Contract conformance and breaking-change gate | The served API matches the authored contract, and a v1 change cannot break consumers | Blocking |
| Schemathesis | The API handles generated valid and invalid inputs per the contract | Blocking on new failures |
| Pact consumer and provider | The generated client's expectations hold against the real API, with test-only provider states | Blocking; gates publication |
| Kustomize render | Every overlay renders and validates | Blocking |
| Argo CD sync | Every environment syncs and self-heals drift | Verified in drills |
| Analysis runs | A healthy canary passes, a bad one aborts, and too few samples pause the rollout as inconclusive | Verified in drills |
| N-1 schema compatibility | The previous release's image runs against the new schema | Blocking |
| Rollback drills | Automatic, manual, and emergency rollback each end with Git authoritative | Release gate |
| Failure drills | Failed migration, signature rejection, Argo CD and Kargo outages, Vault sealed, and Valkey down behave as documented | Rehearsed |
| Performance | k6 meets the SLO thresholds | Release gate |
| Learning paths | Every path succeeds from a fresh environment | Release gate |
| Final validation matrix | Every row of the validation matrix has evidence | Phase exit |

## Tool pages introduced

- API versioning and deprecation headers
- oasdiff (breaking changes)
- Kiota
- Schemathesis
- Pact, PactNet, and Pact Broker
- Kustomize
- Argo CD
- Argo Rollouts and the Gateway API plugin
- Kargo
- k6
- GitHub Apps (least privilege)
- GitHub Deployments

## Labs

1. Generate the client and break the contract.
2. Read a failing Pact verification.
3. Fuzz the API with Schemathesis.
4. Watch Argo CD reconcile drift.
5. Run a canary and read an AnalysisRun.
6. Promote one Freight from dev to prod.
7. Bad canary → automatic abort.
8. Manual and emergency rollback with Git reconciliation.
9. Ship an expand/contract migration across two releases.

## Evidence required

- Kargo Freight history and the three promotion pull requests for one digest, with the analysis results that gated each.
- Transcripts for each rollback and failure drill, and the reconciliation pull request that follows an emergency rollback.
- Pact files and verification results, the contract changelog, and the generated client drift check.
- The inventory audit showing no gaps, the controls matrix, the final threat model, and the retrospective.
- The `v0.4.0` release with its evidence bundle.

## Risks and rollback

| Risk | Mitigation |
| --- | --- |
| False positive or false negative canary analysis (R8) | Synthetic traffic, minimum samples, inconclusive results pause, tests of the analysis templates |
| Schema changes break rollback (R9) | Expand/contract policy, destructive-operation detection, N-1 job |
| PactNet dormancy or incompatibility (R10) | Early spike, pinned version, Pact command-line verification as a fallback |
| Kubernetes version skew (R12) | Compatibility matrix and scheduled drift check |

Rollback: promote the previous verified Freight or revert the promotion pull request. In an emergency, disable auto-sync, run `argocd app rollback`, and open a reconciliation pull request so Git matches the cluster again. The schema is never rolled back by an application rollback.

## Exit gate

- Contract conformance, breaking-change checks, Pact consumer/provider tests, and Schemathesis all pass.
- Every overlay renders. Argo CD syncs every environment.
- One signed, attested digest advances dev → staging → prod through Kargo, with analysis and verification evidence.
- Automated, manual, and emergency rollback drills pass and leave Git authoritative.
- The N-1 schema job passes.
- All learning paths succeed from a fresh environment.
- The inventory audit reports zero gaps.
- README, ADRs, runbooks, status, Milestone, and evidence describe implemented behavior.
