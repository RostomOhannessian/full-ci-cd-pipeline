---
title: "Phase 0: Foundation and early publication"
description: "Make the project portable and publicly verifiable before any code exists."
type: phase-plan
phase: 0
audience: [maintainers, learners]
last-verified: 2026-10-04
owner: "@RostomOhannessian"
---

# Phase 0: Foundation and early publication

Section numbers (§) in this file refer to [the implementation plan](../implementation-plan.md). Live progress is on [the status page](../../project/STATUS.md).

| Field | Value |
| --- | --- |
| Phase branch | `phase/0-foundation` |
| Work-package branches | None. A single documentation pull request, plus one closeout pull request that records the publication results |
| Milestone | Phase 0: Foundation |
| Release at exit | None |
| Depends on | None |

## Goal

make the project portable and publicly verifiable before any code exists.

## Learning outcomes

repository governance, ADR practice, documentation architecture, AI agent configuration, and GitHub's security settings.

## Scope and non-goals

In scope:

- Make the project portable: the approved plan, one plan per phase, machine-readable status, requirements and traceability, a risk register, and the research behind the plan, all in the repository.
- Record decisions: ADR-0001 to ADR-0007 accepted, ADR-0008 to ADR-0019 proposed with evidence.
- Add the public-project files: license, notice, contributing guide, code of conduct, security policy, support guide, governance, Issue forms, and a pull request template.
- Add the documentation skeleton: Diataxis sections, templates, glossary, test strategy, and a first threat model.
- Add agent instructions and repository skills so AI-assisted work is portable and safe.
- Add the first quality gate: Markdown style, offline link and anchor checks, and a full-history secret scan, identical in CI and locally.
- Publish the repository, enable its security settings, and protect the branches.

Non-goals:

- No application code, solution, tests, or build tooling (Phase 1).
- No workflow beyond the documentation gate, no image, no release, and no tag (Phase 2).
- No cluster, Terraform, or secrets store (Phase 3).
- No external skill installed. External skills are reviewed and recorded only.
- No GitHub Pages site (it starts at the end of Phase 1).

## Decisions introduced

| ADR | Decision | Status after this phase |
| --- | --- | --- |
| [0001](../../adr/0001-record-architecture-decisions.md) | Record decisions as ADRs | Accepted |
| [0002](../../adr/0002-repository-visibility-and-capability-strategy.md) | Public after Phase 0, capability-aware workflows | Accepted |
| [0003](../../adr/0003-branch-and-work-package-protocol.md) | Phase branches and work-package pull requests | Accepted |
| [0004](../../adr/0004-licensing-and-dependency-license-policy.md) | Apache-2.0 and the dependency license policy | Accepted |
| [0005](../../adr/0005-documentation-system.md) | Documentation as code with DocFX and Diataxis | Accepted |
| [0006](../../adr/0006-commit-identity-and-privacy.md) | Noreply commit identity and privacy rules | Accepted |
| [0007](../../adr/0007-solo-maintainer-governance.md) | Rulesets, CODEOWNERS, and documented bypass | Accepted |
| [0008](../../adr/0008-secrets-broker-vault-over-openbao.md) to [0019](../../adr/0019-resource-profiles-and-host-support.md) | Technology decisions, each with its research evidence | Proposed; each is accepted by the work package it names |

## Prerequisites and setup changes

- A GitHub account on the Free plan, the GitHub CLI authenticated with the `repo` and `workflow` scopes, and Git.
- Docker, to run the lint containers (no other local tooling is needed for this phase).
- Setup changes this phase introduces: a repository-local noreply Git identity, the branch protocol, line-ending normalization, and the lint tool definitions in `tools/lint/compose.yaml`.

## Pre-branch steps

these run on `master`, and each destructive step needs explicit confirmation when it runs.

1. Set the repo-local Git identity to `user.email = 260491873+RostomOhannessian@users.noreply.github.com`, keeping `user.name`.
2. Recreate the empty root commit under the noreply identity, then force-push `master`. This is the only planned force-push. It's safe because the remote has no other branches, PRs, or files.
3. Confirm the Copilot checkpoint refs stay local.

## Work packages

| WP | Title | Size | Deliverables |
| --- | --- | --- | --- |
| 0.1 | Identity and hygiene | S | Noreply configuration; root-commit rewrite; `.gitattributes`, `.gitignore`, `.editorconfig` |
| 0.2 | Plans and tracking | M | This plan, split into `implementation-plan.md` plus `phases/phase-0..4` and `launch`; `requirements.yaml` and brief traceability; `status.yaml` and its schema; a hand-maintained `STATUS.md` (generated from WP1.2 onward); risk register; research records; tool inventory seeded with planned tools and their lifecycle |
| 0.3 | Governance and community files | S | README (WIP banner, vision, architecture sketch, roadmap, status, how to follow along); Apache-2.0 `LICENSE` and `NOTICE`; CONTRIBUTING (protocol, Conventional Commits, Definitions of Ready and Done, contribution policy: Issues and Discussions welcome, external PRs accepted after v1.0); CODE_OF_CONDUCT (Contributor Covenant); SECURITY (private vulnerability reporting); SUPPORT; GOVERNANCE (solo maintainer, decisions by ADR); CHANGELOG (Keep a Changelog); Issue forms; PR template; CODEOWNERS |
| 0.4 | Decisions and docs skeleton | M | ADR template; ADR-0001 (record architecture decisions); Accepted process ADRs; Proposed technology ADRs with research evidence (see below); Diataxis skeleton; templates for tool pages, labs, test plans, runbooks, spike reports, and the threat model; glossary seed; threat model v0 (context, assets, trust boundaries) |
| 0.5 | AI enablement | S | `AGENTS.md`; `.github/copilot-instructions.md`; path-specific instructions; `REVIEW.md`; the `adr-assistant`, `tool-doc-author`, and `session-handoff` skills; `docs/governance/ai-skills.md` with the external candidate reviews (no installation) |
| 0.6 | Documentation quality gate | S | `docs-quality.yml` (markdownlint-cli2, lychee internal links, gitleaks full-history scan); Dependabot for `github-actions`; SHA-pinned actions; read-only permissions |
| 0.7 | Publication and GitHub configuration | S | See below |

### ADRs recorded in WP0.4

- **Accepted** (process decisions):
  - Visibility and capability strategy
  - Branch and work-package protocol
  - Licensing and dependency license policy
  - Documentation system
  - Commit identity
  - Solo-maintainer governance
- **Proposed** (technology decisions, with research evidence):
  - Vault over OpenBao
  - Kargo
  - Gateway API with Envoy Gateway
  - Trivy IaC as the successor to tfsec
  - FusionCache
  - Contract-first approach
  - Test stack
  - Resource profiles
  - The four brief ADRs (OIDC, shift-left tools, GitOps, domain events)

### Publication and configuration steps (WP0.7)

1. Run the publication checklist.
2. Get explicit confirmation, then make the repository public.
3. Enable:
   - Secret scanning and push protection
   - Private vulnerability reporting
   - Dependabot alerts and security updates
4. Configure Actions:
   - Default token read-only
   - Approval required for fork workflows from all outside contributors
   - SHA pinning enforced
   - Allowlisted actions only
5. Create rulesets for `master` and `phase/**`, set merge options, and turn on auto-merge, delete-branch-on-merge, and immutable releases.
6. Create the labels, the Milestones (Phase 0 to Phase 4 and v1.0), and the Phase 1 work-package Issues.
7. Record every setting in `docs/governance/github-settings.md`.
8. Run the fresh-clone resume test.

## Test plan

Phase 0 is documentation and configuration, so its tests are checks on content, history, and settings.

| Check | How | Where it runs | Evidence |
| --- | --- | --- | --- |
| Markdown style | markdownlint-cli2 through `tools/lint/compose.yaml` | CI (`docs-quality`) and locally | Job log |
| Relative links and heading anchors | lychee in offline mode | CI and locally | Job log |
| Secrets in history | gitleaks over every commit | CI and locally | Job log |
| Secrets in the working tree | gitleaks over uncommitted files | Locally, before every commit | PR description |
| Negative controls | A broken link, a broken anchor, and a seeded fake secret must each fail the check | Locally, once | Transcript in the PR |
| YAML and JSON validity | Parse every YAML and JSON file | Locally | PR description |
| Privacy scan | Search all files for email addresses, local paths, and the original commit ID | Locally, as part of the publication checklist | Checklist record |
| Publication checklist | Manual review against the list in the retrospective | Before the visibility change | [GitHub settings record](../../governance/github-settings.md) |
| Ruleset smoke test | A direct push to `master` is rejected, and a pull request without passing checks cannot merge | After the rulesets exist | Settings record |
| Fresh-clone resume test | Clone into a new directory and follow `AGENTS.md` and `STATUS.md` to the next action without session state | After publication | Retrospective |

## Evidence required

- The pull request with green `docs-quality` runs.
- The negative-control transcript.
- JSON snapshots of the repository settings and rulesets, kept under `governance/github/` and described in the settings record.
- The publication checklist, the ruleset smoke-test result, and the fresh-clone resume transcript.

## Risks and rollback

| Risk | Mitigation |
| --- | --- |
| Accidental disclosure at publication (R13 and the privacy rules in [ADR-0006](../../adr/0006-commit-identity-and-privacy.md)) | Privacy scan, secret scans, and a confirmation before the one-way visibility change |
| The replaced initial commit can still be retrieved by its ID | Recorded in ADR-0006; the repository is private until the checklist passes; stronger options remain available before publication |
| Misconfigured rulesets lock the owner out | The owner keeps a pull-request-only bypass; settings are stored as JSON and can be re-applied |
| Dependabot noise | Weekly schedule, seven-day cooldown, grouped minor and patch updates |

Rollback: revert the pull request. The repository can be made private again, although anything already cloned, forked, or cached cannot be recalled.

## Exit gate

- `master` contains the plan, phase plans, status, decisions, research, and agent instructions.
- Every commit uses the noreply identity, and the gitleaks full-history scan is clean.
- The repository is public with protections active, and the Phase 1 Milestone and Issues exist.
- A fresh clone in a new directory can resume work from `AGENTS.md` and `STATUS.md`, with no session state.
