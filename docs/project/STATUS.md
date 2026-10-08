---
title: "Project status"
description: "Where the project is now, how to resume work on another machine, and the state of every phase."
type: status
audience: [maintainers, contributors, learners]
last-verified: 2026-10-06
owner: "@RostomOhannessian"
---

# Project status

This page is the readable view of [status.yaml](status.yaml), which is the source of truth. It is hand-maintained until WP1.2, which generates it. Last updated 2026-10-06.

## Resume here

| Item | Value |
| --- | --- |
| Active phase | [Phase 1: API core](../plans/phases/phase-1-api-core.md), in progress |
| Active work package | None. WP1.0 is complete, and WP1.1 is next ([Issue 3](https://github.com/RostomOhannessian/full-ci-cd-pipeline/issues/3)). |
| Branch | `phase/1-api-core`. Create each work-package branch from it as `wp/1.NN-<slug>`. |
| Last completed | WP1.0 is complete: five risk spikes ran on 2026-10-06 and none failed. Their reports are in [docs/research/spikes/](../research/spikes/README.md), and the affected ADRs, tool inventory, and risk register carry dated evidence. The work landed through pull request 17 into `phase/1-api-core`. |
| Next action | Start WP1.1, solution and build foundation: create `wp/1.1-solution-and-build` from `phase/1-api-core`. Take its settings from the spike reports: create the .NET tool manifest under `.config`, run the first Linux CI job to supply the Linux Testcontainers evidence the spike left open, and decide whether to move to Microsoft Code Coverage 18.12.0. Separately, the owner decides whether to correct the cache ACL patterns in plan section 8.5, as [spike 1.b](../research/spikes/1.b-fusioncache-valkey.md) proposes. |
| Open pull requests | None |

### Resume on another machine

1. Install Git, Docker, the GitHub CLI, and the .NET SDK 10.0.401. The Dev Container (WP1.4) will replace this step.
2. Clone the repository, run `git fetch origin`, and check out the branch named in the table above.
3. Set the repository-local noreply Git identity and turn on "Keep my email addresses private" in your GitHub account settings ([ADR-0006](../adr/0006-commit-identity-and-privacy.md)). Without the setting, GitHub uses your personal email for the merges it creates.
4. Read this page, then [AGENTS.md](../../AGENTS.md).
5. Run the quality gate from the repository root (the same commands CI runs, defined in [tools/lint/compose.yaml](../../tools/lint/compose.yaml)):

   ```text
   docker compose -f tools/lint/compose.yaml run --rm markdownlint
   docker compose -f tools/lint/compose.yaml run --rm links
   docker compose -f tools/lint/compose.yaml run --rm secrets-worktree
   ```

6. Continue with the next action in the table.

Local environments are disposable. Clusters, Vault keys, and generated passwords belong to one machine and are regenerated on a new one, never migrated. None exist yet. The five local spike branches are never pushed, and the spike reports carry what is needed to repeat them.

## Phases

| Phase | Goal | State | Release | Plan | Milestone |
| --- | --- | --- | --- | --- | --- |
| 0 | Make the project portable and publicly verifiable before any code exists. | Complete (2026-10-04) | None | [Phase 0](../plans/phases/phase-0-foundation.md) | [Phase 0: Foundation](https://github.com/RostomOhannessian/full-ci-cd-pipeline/milestone/1) |
| 1 | A production-quality Clean Architecture API that runs locally with one command, plus the governance, documentation, and testing machinery every later phase relies on. | In progress (started 2026-10-06) | `v0.1.0` | [Phase 1](../plans/phases/phase-1-api-core.md) | [Phase 1: API core](https://github.com/RostomOhannessian/full-ci-cd-pipeline/milestone/2) |
| 2 | Every change is built once, then tested, analyzed, scanned, inventoried, signed, attested, and verifiable. | Planned | `v0.2.0` | [Phase 2](../plans/phases/phase-2-secure-ci.md) | [Phase 2: Secure CI](https://github.com/RostomOhannessian/full-ci-cd-pipeline/milestone/3) |
| 3 | A reproducible local Kubernetes platform where every hop is authenticated, encrypted, authorized, segmented, observable, and policy-checked. | Planned | `v0.3.0` | [Phase 3](../plans/phases/phase-3-zero-trust-platform.md) | [Phase 3: Zero-trust platform](https://github.com/RostomOhannessian/full-ci-cd-pipeline/milestone/4) |
| 4 | Versioned, consumer-verified contracts and fully declarative delivery, with rollback rehearsed. | Planned | `v0.4.0` | [Phase 4](../plans/phases/phase-4-contracts-gitops.md) | [Phase 4: Contracts and GitOps](https://github.com/RostomOhannessian/full-ci-cd-pipeline/milestone/5) |
| Launch | Turn the public repository into a polished, versioned portfolio release. | Planned | `v1.0.0` | [Launch gate](../plans/phases/launch-v1.md) | [v1.0 launch](https://github.com/RostomOhannessian/full-ci-cd-pipeline/milestone/6) |

Work packages per phase: Phase 0 has 7, Phase 1 has 14, Phase 2 has 8, Phase 3 has 11, and Phase 4 has 9. Their order, dependencies, Issues, and evidence are in [status.yaml](status.yaml).

## Phase 0 work packages

All seven are complete. The [retrospective](../journal/phase-0-retrospective.md) says what was built, what went wrong, and what changed.

| WP | Title | Size | State | Evidence |
| --- | --- | --- | --- | --- |
| WP0.1 | Identity and hygiene | S | Completed | [ADR-0006](../adr/0006-commit-identity-and-privacy.md) execution records |
| WP0.2 | Plans and tracking | M | Completed | [Plan](../plans/implementation-plan.md), phase plans, [status.yaml](status.yaml), [risk register](risk-register.md), [research records](../research/README.md) |
| WP0.3 | Governance and community files | S | Completed | [README](../../README.md), [CONTRIBUTING](../../CONTRIBUTING.md), [SECURITY](../../SECURITY.md), [GOVERNANCE](../../GOVERNANCE.md), Issue forms |
| WP0.4 | Decisions and docs skeleton | M | Completed | [ADR index](../adr/README.md), [templates](../templates/adr-template.md), [glossary](../glossary.md), [threat model](../security/threat-model.md) |
| WP0.5 | AI enablement | S | Completed | [AGENTS.md](../../AGENTS.md), [REVIEW.md](../../REVIEW.md), [AI skills record](../governance/ai-skills.md) |
| WP0.6 | Documentation quality gate | S | Completed | [docs-quality run on the Phase 0 pull request](https://github.com/RostomOhannessian/full-ci-cd-pipeline/actions/runs/37243822276): Markdown style, Internal links, and Secret scan (full history) all pass |
| WP0.7 | Publication and GitHub configuration | S | Completed | [GitHub settings record](../governance/github-settings.md), [settings as code](../../governance/github/README.md), [retrospective](../journal/phase-0-retrospective.md) |

## Phase 1 work packages

WP1.0 is complete. The other thirteen are planned, and their order and dependencies are in [status.yaml](status.yaml).

| WP | Title | Size | State | Issue or evidence |
| --- | --- | --- | --- | --- |
| WP1.0 | Risk spikes | S | Completed | [Spike reports](../research/spikes/README.md), [Issue 2](https://github.com/RostomOhannessian/full-ci-cd-pipeline/issues/2) |
| WP1.1 | Solution and build foundation | M | Planned | [Issue 3](https://github.com/RostomOhannessian/full-ci-cd-pipeline/issues/3) |
| WP1.2 | Governance core | M | Planned | [Issue 4](https://github.com/RostomOhannessian/full-ci-cd-pipeline/issues/4) |
| WP1.3 | Documentation toolchain | M | Planned | [Issue 5](https://github.com/RostomOhannessian/full-ci-cd-pipeline/issues/5) |
| WP1.4 | Developer environment | L | Planned | [Issue 6](https://github.com/RostomOhannessian/full-ci-cd-pipeline/issues/6) |
| WP1.5 | Contract v1 design | M | Planned | [Issue 7](https://github.com/RostomOhannessian/full-ci-cd-pipeline/issues/7) |
| WP1.6 | Domain model | M | Planned | [Issue 8](https://github.com/RostomOhannessian/full-ci-cd-pipeline/issues/8) |
| WP1.7 | Application layer | L | Planned | [Issue 9](https://github.com/RostomOhannessian/full-ci-cd-pipeline/issues/9) |
| WP1.8 | Persistence and outbox | L | Planned | [Issue 10](https://github.com/RostomOhannessian/full-ci-cd-pipeline/issues/10) |
| WP1.9 | Caching | M | Planned | [Issue 11](https://github.com/RostomOhannessian/full-ci-cd-pipeline/issues/11) |
| WP1.10 | HTTP API | L | Planned | [Issue 12](https://github.com/RostomOhannessian/full-ci-cd-pipeline/issues/12) |
| WP1.11 | Observability | M | Planned | [Issue 13](https://github.com/RostomOhannessian/full-ci-cd-pipeline/issues/13) |
| WP1.12 | Auditor depth and AI skills | M | Planned | [Issue 14](https://github.com/RostomOhannessian/full-ci-cd-pipeline/issues/14) |
| WP1.13 | Learning content and phase exit | M | Planned | [Issue 15](https://github.com/RostomOhannessian/full-ci-cd-pipeline/issues/15) |

## What comes next

WP1.0 found no failing spike, so no plan amendment is needed and WP1.1 can start. Three results carry forward. Linux and Codespaces evidence for Testcontainers SQL Server is still open and comes from the first Linux CI run in WP1.1. The cache ACL patterns in plan section 8.5 do not match what FusionCache writes to Valkey, and the owner decides whether to amend them. Stryker.NET cannot start natively on a Windows host that enforces Smart App Control, so mutation testing runs on Linux.

## Decisions

Seven process decisions are accepted and twelve technology decisions are proposed, each accepted by the work package it names. See [the ADR index](../adr/README.md).

## Risks

The [risk register](risk-register.md) lists the open risks, their mitigations, and the triggers that reopen them.

## How to update this page

1. Edit [status.yaml](status.yaml) in the same commit as the change it records.
2. Mirror the change here, including the **Resume here** table, until WP1.2 generates the page.
3. Commit and push the branch, so another machine can resume. The `session-handoff` skill walks through these steps ([skill file](../../.github/skills/session-handoff/SKILL.md)).
