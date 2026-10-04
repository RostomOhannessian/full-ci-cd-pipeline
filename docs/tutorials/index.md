---
title: "Tutorials"
description: "How the repository's guided learning paths and task-focused how-to guides are planned, and what a reader can use today."
audience: [learners, contributors, maintainers]
type: explanation
last-verified: 2026-10-04
owner: "@RostomOhannessian"
---

# Tutorials

Tutorials are guided, end-to-end lessons. They differ from [labs](../labs/index.md), which ask you to observe and break something on purpose; from [reference](../reference/tools/index.md), which is for lookup; and from how-to guides, which are shorter task recipes for a specific job.

## Learning paths

The seven learning paths in [plan section 11.4](../plans/implementation-plan.md) are the repository's tutorial backbone.

| Path | Audience | Ordered steps by phase | Delivered by |
| --- | --- | --- | --- |
| Quick start: run the API-only stack and call the API with a token, about 30 minutes | Developers who want the fastest successful first run | Phase 0: read the docs map and constraints. Phase 1: use the Dev Container or native setup, run the API-only stack, get a token, and call the API. | WP1.4, WP1.10, WP1.13 |
| Clean Architecture and DDD in practice | Developers who want to connect the domain model, handlers, persistence, and HTTP edges | Phase 1: learn the layers, invariants, CQRS flow, outbox, caching, and API surface through the main tutorial and labs. | WP1.6-WP1.10, WP1.13 |
| Testing deep dive: pyramid, Testcontainers, contracts, mutation, determinism | Developers who want to understand why each suite exists and what risk it covers | Phase 1: unit, architecture, integration, and determinism basics. Phase 4: add contract, Pact, Schemathesis, rollout, and rollback evidence. | WP1.1, WP1.8-WP1.10, WP1.13, WP4.1, WP4.2, WP4.6, WP4.7 |
| Secure software supply chain | Developers who want to see how one build becomes a signed, scanned, attestable artifact | Phase 2: build the image once, scan it, generate SBOMs, sign it, attest it, and verify it. | WP2.1-WP2.7 |
| Zero-trust platform on Kubernetes | Developers who want to run the platform with segmented traffic, identity, policy, and secrets brokering | Phase 3: create the cluster, apply the platform stacks, prove segmentation, identity, dynamic credentials, policy, and observability. | WP3.0-WP3.10 |
| GitOps and progressive delivery | Developers who want to learn reconciled delivery, canaries, and promotion pull requests | Phase 4: govern the contract, install the GitOps control plane, run analyzed canaries, and promote one digest through environments. | WP4.1-WP4.8 |
| Operating the platform: SLOs, incidents, rollback, recovery | Operators who want to run, recover, and prove the platform safely | Phase 3: learn backups, restore, profile limits, break-glass, and rebuild. Phase 4: add SLO-driven rollout analysis, incident drills, rollback, and reconciliation. | WP3.3, WP3.4, WP3.10, WP4.4, WP4.6, WP4.7 |

## Planned tutorials and how-to guides

| Phase | Page or set | Kind | Work package | Status |
| --- | --- | --- | --- | --- |
| 1 | 00-start-here | Tutorial | WP1.13 | planned |
| 1 | Quick start: run the API-only stack and call the API with a token | Tutorial | WP1.13 | planned |
| 1 | Clean Architecture and DDD in practice | Tutorial | WP1.13 | planned |
| 1 | Task recipes under `docs/tutorials/how-to` for local setup, the `dev` command, and API workflows | How-to guide set | WP1.13 | planned |
| 2 | Secure software supply chain | Tutorial | WP2.7 | planned |
| 2 | Task recipes under `docs/tutorials/how-to` for CI verification, SBOM review, and signature checks | How-to guide set | WP2.7 | planned |
| 3 | Zero-trust platform on Kubernetes | Tutorial | WP3.10 | planned |
| 3 | Resource profiles (`api`, `lite`, `full`, `ci`) | How-to guide | WP3.10 | planned |
| 3 | Task recipes under `docs/tutorials/how-to` for platform create, destroy, identity, and recovery tasks | How-to guide set | WP3.10 | planned |
| 4 | GitOps and progressive delivery | Tutorial | WP4.7 | planned |
| 4 | Operating the platform: SLOs, incidents, rollback, recovery | Tutorial | WP4.7 | planned |
| 4 | Task recipes under `docs/tutorials/how-to` for contract checks, promotions, and rollback workflows | How-to guide set | WP4.7 | planned |

The plan names the tutorial paths above, but not the final filenames for most how-to guides yet. Those pages stay plain text here until the owning work package writes them.

## How to read the docs

- **Prerequisites:** syntax-level comfort with Git, Docker, and the GitHub CLI, plus working knowledge of C#, YAML, HCL, PowerShell, and shell.
- **Start today with:** [the docs map](../index.md), [the implementation plan](../plans/implementation-plan.md), [the ADRs](../adr/README.md), [the threat model](../security/threat-model.md), and [the testing strategy](../testing/strategy.md).
- **What works today:** Phase 0 ships only the documentation quality gate, so the runnable path today is the lint gate: markdownlint, offline link and anchor checks, and full-history secret scanning from [the Phase 0 plan](../plans/phases/phase-0-foundation.md).
- **What comes later:** tutorials become executable once Phase 1 adds the Dev Container, the `dev` command, the API-only stack, and tested tutorial scripts.
