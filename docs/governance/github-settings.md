---
title: "GitHub settings record"
description: "The configuration of this repository's GitHub settings, why each value was chosen, and the evidence that it is applied."
type: reference
audience: [maintainers, contributors]
last-verified: 2026-10-04
owner: "@RostomOhannessian"
---

# GitHub settings record

This page records the GitHub settings that protect the repository, with the reason for each and the evidence that it is applied. It is the human-readable side of [ADR-0002](../adr/0002-repository-visibility-and-capability-strategy.md) and [ADR-0007](../adr/0007-solo-maintainer-governance.md). The machine-readable side is in [governance/github](../../governance/github/README.md): the inputs that create the settings, and a snapshot of the live values.

## State

Everything below marked `applied` was applied and verified on 2026-10-04 in WP0.7, after the owner confirmed each step that is hard to reverse. Several settings are unavailable while a repository is private on the Free plan, including rulesets, code scanning, and artifact attestations ([plan section 4.1](../plans/implementation-plan.md)). That is why the repository became public at the end of Phase 0.

## Account settings (owner)

GitHub creates merge, squash, and web-edit commits itself, and it uses the account's primary email for them unless the account keeps its email private. These settings are therefore prerequisites for every merge ([ADR-0006](../adr/0006-commit-identity-and-privacy.md)).

| Setting | Target value | Why | State |
| --- | --- | --- | --- |
| Keep my email addresses private | On | GitHub-created commits then use the noreply address | applied; verified through the account email API, and a merge and a squash in a scratch repository both used the noreply address |
| Block command line pushes that expose my email | On | A push whose commits carry a personal address is rejected | applied by the owner in account settings |

## Publication gate

| Step | Target | State |
| --- | --- | --- |
| Privacy scan | No personal email address, local path, or token in any tracked file | applied: no findings on 96 files |
| Secret scan | gitleaks finds nothing in the full history and in the working tree | applied: no leaks, on a fresh clone of the remote |
| Commit metadata | The author and committer email of every commit on every remote ref is the noreply address, including merge and squash commits that GitHub created | applied: only the noreply address appears, and the workflow run metadata matches |
| Replaced initial commit | The replaced commit must not be retrievable | applied: the repository was recreated, and GitHub answers HTTP 422 for the old commit ID ([ADR-0006](../adr/0006-commit-identity-and-privacy.md)) |
| Visibility | Public, with the work-in-progress banner in the README | applied after the owner's explicit confirmation |

## Repository

| Setting | Target value | Why | State |
| --- | --- | --- | --- |
| Description and topics | A one-sentence description and 16 topics | Helps learners and recruiters find the repository | applied |
| Issues | On | Work packages, bugs, and spikes are tracked as Issues | applied |
| Discussions | On | Questions and design talk have a place that is not an Issue ([SUPPORT.md](../../SUPPORT.md)) | applied |
| Wiki and Projects | Off | The plan, status, and Issues already cover them, and docs live in the repository | applied |
| GitHub Pages | Off | Pages starts at the end of Phase 1 | planned (Phase 1) |
| Merge methods | Merge commit and squash merge on, rebase merge off | Phase pull requests use merge commits and work-package pull requests are squashed ([ADR-0003](../adr/0003-branch-and-work-package-protocol.md)) | applied |
| Merge commit messages | Pull request title and body | Keeps Conventional Commit titles in history | applied |
| Auto-merge | On | Lets a green pull request merge without waiting | applied |
| Delete branch on merge | On | Keeps branches tidy | applied |
| Immutable releases | On | A published release can never be changed, so a correction is a new release | applied |

## Security features

| Setting | Target value | Why | State |
| --- | --- | --- | --- |
| Secret scanning | On | Detects committed credentials | applied |
| Push protection | On | Blocks a push that contains a detected secret | applied |
| Private vulnerability reporting | On | [SECURITY.md](../../SECURITY.md) tells reporters to use it | applied |
| Dependabot alerts | On | Flags vulnerable dependencies | applied |
| Dependabot security updates | On | Opens pull requests that fix vulnerable dependencies | applied |
| Code scanning | Off until WP2.3 | CodeQL arrives with the CI work in Phase 2 | planned (WP2.3) |

## GitHub Actions

| Setting | Target value | Why | State |
| --- | --- | --- | --- |
| Default token permissions | Read repository contents only | Workflows ask for more per job, never by default | applied |
| Actions may create and approve pull requests | Off | A workflow must not approve its own change | applied |
| Fork pull request workflows | Require approval for all outside contributors | A fork cannot run workflows unreviewed | applied |
| SHA pinning | Required | A tag can be moved, as the 2026 trivy-action incident showed ([plan section 1.1](../plans/implementation-plan.md)) | applied |
| Allowed actions | GitHub-owned and verified creators, plus an explicit allowlist that starts empty | Keeps third-party code to a minimum | applied |
| Environments `publish` and `release` | Created with the release workflow | They gate publishing and signing ([plan section 5.2](../plans/implementation-plan.md)) | planned (WP2.2 to WP2.6) |

## Rulesets

Both rulesets let the repository admin role bypass them for pull requests only, because GitHub does not let an author approve their own pull request ([ADR-0007](../adr/0007-solo-maintainer-governance.md)). GitHub records every bypass. A bypass is a policy exception that the owner uses only when code owner review is the sole unmet rule, because GitHub cannot limit a bypass to one rule.

| Ruleset | Applies to | Rules | State |
| --- | --- | --- | --- |
| `master protection` | The default branch | A pull request is required. The checks **Markdown style**, **Internal links**, and **Secret scan (full history)** must pass, and the branch must be up to date. Conversations must be resolved. Code owners must review changes under the paths in [CODEOWNERS](../../.github/CODEOWNERS). Force pushes and deletion are blocked. | applied |
| `phase branch protection` | `phase/**` | A pull request is required, the same checks must pass, and force pushes are blocked. | applied |

Later work packages extend the required checks: the build, test, governance, and docs checks arrive with WP1.1 to WP1.3, and code scanning results arrive with WP2.3. The inputs are in [governance/github/rulesets](../../governance/github/rulesets/master.json).

### Smoke test (2026-10-04)

| Check | Result |
| --- | --- |
| A direct push to `master` | Rejected: "Changes must be made through a pull request" and "3 of 3 required status checks are expected" (push declined due to repository rule violations) |
| A pull request whose checks have not passed | `mergeStateStatus` is `BLOCKED` until the three checks pass |
| An owner-authored pull request that changes a CODEOWNERS path, after the checks pass | `mergeStateStatus` becomes `CLEAN`, so no bypass is needed ([ADR-0007](../adr/0007-solo-maintainer-governance.md)) |
| The owner's bypass | `current_user_can_bypass` reports `pull_requests_only` on both rulesets |

## Labels, milestones, and Issues

| Item | Target | State |
| --- | --- | --- |
| Type labels | `bug`, `documentation`, `work-package`, `spike`, `risk`, `security`, `privacy`, `flaky`, `triage`, `blocked`, `dependencies` | applied |
| Phase labels | `phase:0` to `phase:4` | applied |
| Size labels | `size:S`, `size:M`, `size:L`, matching the sizes in [plan section 5.7](../plans/implementation-plan.md) | applied |
| Milestones | Phase 0: Foundation, Phase 1: API core, Phase 2: Secure CI, Phase 3: Zero-trust platform, Phase 4: Contracts and GitOps, v1.0 launch | applied |
| Phase 1 Issues | One Issue per work package, WP1.0 to WP1.13 (Issues 2 to 15), each with its ID in the first line of the body | applied |

Issues for Phases 2 to 4 are created when each phase starts. From WP1.2, `governance github-sync` keeps labels, milestones, and Issues aligned with [status.yaml](../project/status.yaml).

## How a change to a setting is made

1. Change the setting through the GitHub CLI or the web interface.
2. Update the input file and re-export the snapshot under `governance/github/`, and update the row on this page, in the same pull request.
3. Note the reason in the pull request, and record the date in the **State** cell.

A setting that a pull request cannot represent, such as a visibility change, is recorded here with the date and the owner's confirmation.
