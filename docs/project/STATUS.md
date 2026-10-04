---
title: "Project status"
description: "Where the project is now, how to resume work on another machine, and the state of every phase."
type: status
audience: [maintainers, contributors, learners]
last-verified: 2026-10-04
owner: "@RostomOhannessian"
---

# Project status

This page is the readable view of [status.yaml](status.yaml), which is the source of truth. It is hand-maintained until WP1.2, which generates it. Last updated 2026-10-04.

## Resume here

| Item | Value |
| --- | --- |
| Active phase | [Phase 0: Foundation and early publication](../plans/phases/phase-0-foundation.md) |
| Active work package | WP0.6 (documentation quality gate), then WP0.7 (publication and GitHub configuration) |
| Branch | `phase/0-foundation` |
| Last completed | WP0.1 to WP0.5 are written and lint-clean, and the Phase 0 pull request is open. The docs-quality workflow is running on it for the first time. |
| Next action | Confirm the three docs-quality checks pass on pull request 1, record the run as WP0.6 evidence, and merge it with a merge commit after the owner confirms. Then start WP0.7: run the publication checklist and, with the owner's explicit confirmation, make the repository public and apply the settings in [the GitHub settings record](../governance/github-settings.md). |
| Open pull requests | [#1](https://github.com/RostomOhannessian/full-ci-cd-pipeline/pull/1): `phase/0-foundation` to `master` |

### Resume on another machine

1. Install Git, Docker, and the GitHub CLI. Phase 0 needs nothing else.
2. Clone the repository, run `git fetch origin`, and check out the branch named in the table above.
3. Read this page, then [AGENTS.md](../../AGENTS.md).
4. Run the quality gate from the repository root (the same commands CI runs, defined in [tools/lint/compose.yaml](../../tools/lint/compose.yaml)):

   ```text
   docker compose -f tools/lint/compose.yaml run --rm markdownlint
   docker compose -f tools/lint/compose.yaml run --rm links
   docker compose -f tools/lint/compose.yaml run --rm secrets-worktree
   ```

5. Continue with the next action in the table.

Local environments are disposable. Clusters, Vault keys, and generated passwords belong to one machine and are regenerated on a new one, never migrated. None exist yet.

## Phases

| Phase | Goal | State | Release | Plan |
| --- | --- | --- | --- | --- |
| 0 | Make the project portable and publicly verifiable before any code exists. | In progress | None | [Phase 0](../plans/phases/phase-0-foundation.md) |
| 1 | A production-quality Clean Architecture API that runs locally with one command, plus the governance, documentation, and testing machinery every later phase relies on. | Planned | `v0.1.0` | [Phase 1](../plans/phases/phase-1-api-core.md) |
| 2 | Every change is built once, then tested, analyzed, scanned, inventoried, signed, attested, and verifiable. | Planned | `v0.2.0` | [Phase 2](../plans/phases/phase-2-secure-ci.md) |
| 3 | A reproducible local Kubernetes platform where every hop is authenticated, encrypted, authorized, segmented, observable, and policy-checked. | Planned | `v0.3.0` | [Phase 3](../plans/phases/phase-3-zero-trust-platform.md) |
| 4 | Versioned, consumer-verified contracts and fully declarative delivery, with rollback rehearsed. | Planned | `v0.4.0` | [Phase 4](../plans/phases/phase-4-contracts-gitops.md) |
| Launch | Turn the public repository into a polished, versioned portfolio release. | Planned | `v1.0.0` | [Launch gate](../plans/phases/launch-v1.md) |

Work packages per phase: Phase 0 has 7, Phase 1 has 14, Phase 2 has 8, Phase 3 has 11, and Phase 4 has 9. Their order, dependencies, and evidence are in [status.yaml](status.yaml).

## Phase 0 work packages

| WP | Title | Size | State | Evidence |
| --- | --- | --- | --- | --- |
| WP0.1 | Identity and hygiene | S | Completed | [ADR-0006](../adr/0006-commit-identity-and-privacy.md) execution record |
| WP0.2 | Plans and tracking | M | Completed | [Plan](../plans/implementation-plan.md), phase plans, [status.yaml](status.yaml), [risk register](risk-register.md), [research records](../research/README.md) |
| WP0.3 | Governance and community files | S | Completed | [README](../../README.md), [CONTRIBUTING](../../CONTRIBUTING.md), [SECURITY](../../SECURITY.md), [GOVERNANCE](../../GOVERNANCE.md), Issue forms |
| WP0.4 | Decisions and docs skeleton | M | Completed | [ADR index](../adr/README.md), [templates](../templates/adr-template.md), [glossary](../glossary.md), [threat model](../security/threat-model.md) |
| WP0.5 | AI enablement | S | Completed | [AGENTS.md](../../AGENTS.md), [REVIEW.md](../../REVIEW.md), [AI skills record](../governance/ai-skills.md) |
| WP0.6 | Documentation quality gate | S | In progress | Verified by the checks on the Phase 0 pull request |
| WP0.7 | Publication and GitHub configuration | S | Planned | Starts after the Phase 0 pull request merges |

## What comes next

After WP0.7, Phase 1 starts with WP1.0, a set of one-day risk spikes (PactNet on .NET 10, the FusionCache backplane on Valkey, Microsoft Testing Platform with coverage, Testcontainers SQL Server on each host, and file-rotated database credentials). A failed spike amends the plan before dependent work begins.

## Decisions

Seven process decisions are accepted and twelve technology decisions are proposed, each accepted by the work package it names. See [the ADR index](../adr/README.md).

## Risks

The [risk register](risk-register.md) lists the open risks, their mitigations, and the triggers that reopen them.

## How to update this page

1. Edit [status.yaml](status.yaml) in the same commit as the change it records.
2. Mirror the change here, including the **Resume here** table, until WP1.2 generates the page.
3. Commit and push the branch, so another machine can resume. The `session-handoff` skill walks through these steps ([skill file](../../.github/skills/session-handoff/SKILL.md)).
