---
title: "Phase 0 retrospective"
description: "What Phase 0 built, what went wrong, what changed in the plan, and what carries into Phase 1."
type: retrospective
audience: [maintainers, contributors, learners]
last-verified: 2026-10-04
owner: "@RostomOhannessian"
---

# Phase 0 retrospective

Phase 0 made the project portable and publicly verifiable before any code existed. It started from an empty repository and ended on 2026-10-04 with a public repository, a reviewed plan, recorded decisions, and a quality gate. This page records what was built, what went wrong, and what changed. The plan for the phase is [Phase 0](../plans/phases/phase-0-foundation.md).

## What Phase 0 built

| Area | Result |
| --- | --- |
| Plan and decisions | [The implementation plan](../plans/implementation-plan.md) (version 2.0.2) with one plan per phase, [ADR-0001 to ADR-0019](../adr/README.md) (seven accepted, twelve proposed with evidence), and five dated [research records](../research/README.md) |
| Tracking | [status.yaml](../project/status.yaml) with a schema, a [status page](../project/STATUS.md) with a **Resume here** section, a [risk register](../project/risk-register.md) with 19 risks, and a [requirements register](../requirements/requirements.yaml) with 121 requirements that cover all 38 outcomes of the original brief |
| Tool knowledge | A [tool inventory](../reference/tools/index.md) of 129 entries: 96 adopt, 7 trial, 3 assess, and 23 hold, each with a verified license and version |
| Community and governance | License and notice, README with architecture diagrams, contributing guide, code of conduct, security policy, support guide, governance, changelog, Issue forms, pull request template, and CODEOWNERS |
| Documentation skeleton | Diataxis index pages, seven templates, a glossary of 59 terms, a first threat model, and the testing strategy |
| AI enablement | [AGENTS.md](../../AGENTS.md), [REVIEW.md](../../REVIEW.md), seven path-scoped instruction files, three repository skills, and a [review of 12 external skills](../governance/ai-skills.md), none installed |
| Quality gate | The `docs-quality` workflow and the same checks locally: Markdown style, offline link and anchor checks, and a full-history secret scan |
| Publication | A public repository with secret scanning, push protection, private vulnerability reporting, Dependabot, hardened Actions settings, two rulesets, labels, six milestones, and 14 work-package Issues for Phase 1 ([settings record](../governance/github-settings.md)) |

## Exit gate

| Criterion | Evidence |
| --- | --- |
| `master` contains the plan, phase plans, status, decisions, research, and agent instructions | Pull request 1, merged with a merge commit |
| Every commit uses the noreply identity, and the gitleaks full-history scan is clean | Checked on a fresh clone of the remote before publication, and again after the merge ([settings record](../governance/github-settings.md)) |
| The repository is public with protections active, and the Phase 1 Milestone and Issues exist | [Settings record](../governance/github-settings.md) and [snapshot](../../governance/github/snapshot.json); [Phase 1 Milestone](https://github.com/RostomOhannessian/full-ci-cd-pipeline/milestone/2) with Issues 2 to 15 |
| A fresh clone can resume work from `AGENTS.md` and `STATUS.md` with no session state | Passed on 2026-10-04; see the fresh-clone resume test below |

## Fresh-clone resume test

The test cloned the repository into a new directory with no session state, and used only the documented files. It passed on 2026-10-04:

| Step | Result |
| --- | --- |
| Read the **Resume here** section of `STATUS.md` | The active phase (Phase 1, ready to start), the branch (`master`), the last completed step, the next action, and the open pull requests (none) were all present and unambiguous |
| Find the resume protocol in `AGENTS.md` | Present |
| Run the quality gate with the documented commands | `markdownlint`, `links`, and `secrets-worktree` all exited 0 |
| Compare `status.yaml` with `STATUS.md` | They agree: phase 1 is current, Phase 0 is completed, and there are no open pull requests |
| Check that the next action can be taken | Issue 2 (WP1.0) is open, and `phase/1-api-core` does not exist yet, as expected until Phase 1 starts |

## What went well
- **Verify before deciding.** The research found facts that changed the plan before any code existed. GitHub Free private repositories lack rulesets, code scanning, and artifact attestations. CodeQL's terms forbid private repositories. OpenBao has no SQL Server plugin. ingress-nginx is retired, and tfsec is frozen. The trivy-action tags were hijacked in 2026. ([Plan section 1.1](../plans/implementation-plan.md) lists every finding.)
- **One quality gate, identical everywhere.** The same three commands run locally and in CI, and the negative controls proved that a broken link, a broken heading anchor, and a seeded fake secret each fail the gate.
- **Drafting in parallel, then reviewing.** Background agents drafted ADRs, the inventory, the AI configuration, the glossary, and the requirements register while the maintainer reviewed. The review found real problems, listed next.

## What went wrong

1. **A personal email reached a merge commit.** Merging the first Phase 0 pull request through GitHub created a merge commit authored with the account's primary email, because "Keep my email addresses private" was off. The repository-local noreply identity does not cover commits that GitHub creates itself. The commit-metadata check in the publication checklist caught it while the repository was still private. A force-push could not remove it, since GitHub keeps a pull request's merge commit and the run metadata, so the repository was deleted and recreated. That also removed the replaced initial commit. **Lesson:** a platform-created commit is outside the repository's Git configuration, so verify the account setting first and check commit metadata after every merge. **Changes:** [ADR-0006](../adr/0006-commit-identity-and-privacy.md) records it, the risk register closes R18 and adds R19, [AGENTS.md](../../AGENTS.md) and [REVIEW.md](../../REVIEW.md) carry the rule, WP1.2 adds a commit-identity check to the governance auditor, and the merge and squash behavior was confirmed in a scratch repository before the next merge.
2. **An unsupported claim.** An earlier statement that Trivy keeps tfsec's rule IDs was not supported by Aqua's migration guide. The plan, ADR-0014, and the inventory now say it is unverified, and WP3.1 verifies it with a fixture.
3. **Inventory errors found in review.** Vault Agent had been attributed to the Kubernetes injector, with the wrong license and version, and the AGPL observability services had no license note. The inventory was corrected, [ADR-0004](../adr/0004-licensing-and-dependency-license-policy.md) now covers them as tool-only exceptions, and the Documentation.Auditor will enforce a note for every non-permissive license from WP1.3.
4. **Generated instruction files needed fixing.** One referenced a working document that is not in the repository, one gave a Kubernetes rule that contradicted the ownership matrix (default-deny policies belong to Terraform), and several repeated the same bullet. **Lesson:** review generated configuration against the architecture and ownership rules, and keep instruction files short and non-repetitive.
5. **Tooling quirks.** The file-creation tool wrote CRLF line endings, and a table normalizer dropped the indentation of tables inside lists, which a scan found. Both are fixed, and line-ending normalization is now part of the pre-commit routine. markdownlint's table-style rule also needs consistent delimiter rows.
6. **Small drift.** A link to the settings record pointed at a page that did not exist yet, and a comment promised a scheduled external link check that is only planned for WP1.3. The link check and review caught both.
7. **An orphaned deliverable.** The plan named a machine-migration runbook but no work package. It now belongs to WP1.4 and is extended in WP3.10.

## What changed in the plan

- The plan is now version 2.0.2 ([revision history](../plans/implementation-plan.md)).
- Five tools moved from adopt to trial because a named spike must prove them: FusionCache, Kargo, the Argo Rollouts Gateway API plugin, kindnet, and Chainsaw.
- WP1.2's security auditor also checks commit identity, and WP1.4 owns the machine-migration runbook.
- Deviations from the Phase 0 plan: a Phase 0 Milestone was created in addition to Phase 1 to 4 and v1.0; a closeout pull request records the publication results; questions go to GitHub Discussions instead of an Issue form; the first Phase 0 merge was redone after the repository was recreated, with the merge commit built locally under the noreply identity.

## Evidence

- The merged Phase 0 pull request and its [passing docs-quality run](https://github.com/RostomOhannessian/full-ci-cd-pipeline/actions/runs/37243822276).
- The negative-control results and the other checks, in the description of that pull request.
- The [settings record](../governance/github-settings.md), the [settings as code](../../governance/github/README.md), and the ruleset smoke test in the record.

## Carry-over to Phase 1

- Start with WP1.0, the risk spikes ([Issue 2](https://github.com/RostomOhannessian/full-ci-cd-pipeline/issues/2)). A failed spike amends the plan before dependent work begins.
- Whether Vault's Redis plugin works with Valkey ACLs is still open and belongs to WP3.0.
- The code of conduct names the private security-advisory channel as its contact. Add a dedicated address before the v1.0 launch.
- The required checks grow with WP1.1 to WP1.3, and code scanning results become required with WP2.3.
- Every merge needs the commit-metadata check until WP1.2 automates it.
