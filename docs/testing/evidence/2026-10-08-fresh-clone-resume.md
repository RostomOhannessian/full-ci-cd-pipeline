---
title: "Evidence record: fresh-clone resume (2026-10-08)"
description: "A dated record that the resume protocol works from a fresh clone of the WP1.2 branch, with the commands that ran and what they reported."
type: evidence-record
date: 2026-10-08
status: verified
audience: [maintainers, contributors]
last-verified: 2026-10-08
owner: "@RostomOhannessian"
---

# Evidence record: fresh-clone resume (2026-10-08)

This record is the manual evidence for `REQ-GOV-003`: a fresh clone can resume work by reading the status page, checking out the active branch, and continuing with the next action. It also shows that the end-of-session protocol leaves files that a clean checkout can validate.

## Scope and limits

- The clone came from the committed WP1.2 branch, `wp/1.2-governance-core`, at the commit that delivers the governance auditor. It was made on the same machine, into an empty temporary directory, so no build output, restored package, or local setting carried over.
- The Dev Container and `dev doctor` do not exist yet. The Dev Container and `dev doctor` are planned in WP1.4, so step 4 of the resume protocol was replaced by the commands under "Commands that run today" in [AGENTS.md](../../../AGENTS.md), as that page allows.
- This record covers one machine, as the plan's fresh-clone resume test does (a clone into a new directory, [plan section 5](../../plans/implementation-plan.md) and [Phase 0](../../plans/phases/phase-0-foundation.md)). A run on a second machine would be stronger evidence, and the launch plan's stranger test covers the human side.
- Requirement `REQ-GOV-003` stays `in_progress` in [the requirements register](../../requirements/requirements.yaml) until `dev doctor` arrives in WP1.4.

## Steps and results

| Step | Command or action | Result |
| --- | --- | --- |
| 1 | `git clone --branch wp/1.2-governance-core <source> <empty directory>` | The clone succeeded at the WP1.2 commit. |
| 2 | Read [STATUS.md](../../project/STATUS.md), **Resume here** | The page names the active phase, the branch, the last completed work package, and the next action. It is generated from `status.yaml`. |
| 3 | `git config --local user.email` before setting anything | Empty, so a fresh clone carries no personal address. The repository noreply address was then set, as [ADR-0006](../../adr/0006-commit-identity-and-privacy.md) requires. |
| 4 | `dotnet tool restore`, `dotnet restore ProductCatalog.slnx`, `dotnet build ProductCatalog.slnx --configuration Release --no-restore` | The tools and packages restored, and the build reported 0 warnings and 0 errors. |
| 5 | `dotnet run --project tools/Governance.Auditor -- status validate` | Passed with no warnings. |
| 6 | `status render --check` | Passed, so `STATUS.md` matches what `status.yaml` renders. |
| 7 | `trace --check` | Passed, so the traceability report is current and every requirement that is due has proof. |
| 8 | `docker compose -f tools/lint/compose.yaml run --rm markdownlint`, `links`, `secrets`, and `secrets-worktree` | All four passed: 0 Markdown issues, 0 link errors, and no leaks in the history or the working tree. |

The `secrets` service scans the full Git history, so it needs a regular clone. It scans nothing inside a linked worktree, because the worktree's `.git` file points at a path that exists only on the host. The clone is therefore also the place where that gate can be run locally.

## What this does not prove

- It does not prove that the plan is understandable to a person who has never seen the repository. The Phase 1 retrospective should ask someone other than the author to follow the page.
- It does not exercise `dev doctor`, the Dev Container, or the local stack, which do not exist yet.
