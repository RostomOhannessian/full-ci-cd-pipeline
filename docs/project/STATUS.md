---
title: "Project status"
description: "Where the project is now, how to resume work on another machine, and the state of every phase."
type: status
audience: [maintainers, contributors, learners]
last-verified: 2026-10-08
owner: "@RostomOhannessian"
---

# Project status

This page is generated from [status.yaml](status.yaml), which is the source of truth, by `governance status render`. Do not edit it by hand. Change `status.yaml`, run the command, and commit both files. CI fails when this page differs from the command's output. Last updated 2026-10-08.

## Resume here

| Item | Value |
| --- | --- |
| Active phase | [Phase 1: API core, data layer, and engineering foundation](../plans/phases/phase-1-api-core.md), in progress |
| Active work package | None |
| Branch | `phase/1-api-core` |
| Last completed | WP1.2 is complete: the governance command line in tools/Governance.Auditor (status validate and render, trace, test-summary, github-sync, security, and blast-radius), its security and blast-radius policy files, the governance workflow, and the advanced-security-auditor and blast-radius-evaluator skills. STATUS.md and the traceability report are now generated from data, with drift checks, and every requirement that is due has a test or an evidence entry. 422 tests pass on Windows and in a Linux x86-64 container, the build has zero warnings, and the format, license, and coverage-threshold gates pass. Each security rule was shown to fail against a violating fixture. ADR-0021 and ADR-0022 are new. The work landed through pull request 19 into phase/1-api-core. WP1.0 and WP1.1 landed earlier through pull requests 17 and 18. |
| Next action | Start WP1.3, Documentation toolchain (Issue 5): create wp/1.3-documentation-toolchain from phase/1-api-core. First close Issues 2, 3, and 4, which github-sync shows as the only drift (run it as a dry run, and pass --apply only with the maintainer's approval). The owner has four decisions waiting. First, whether to add the ci jobs (Build and test, Script tests, Format verification, License gate, Conventional Commit title) and the Governance auditors job as required checks in the master and phase rulesets, which is a settings change that needs the owner's approval, and which the plan expects from WP1.2 on. Second, whether to add tools/Governance.Auditor/ to CODEOWNERS, because the auditor is built from the pull request it audits (risk R20). Third, whether to correct the cache ACL patterns in plan section 8.5, as proposed in spike 1.b. Fourth, the Testcontainers evidence for Linux is now expected from WP1.8, because no work package before it has container tests. |
| Open pull requests | None |

### Environment notes

- Until the Dev Container arrives in WP1.4, Phase 1 work needs Git, Docker (running), the GitHub CLI, the .NET SDK 10.0.401, and PowerShell 7 with Pester 5 for the script tests. Run the documentation gate with the docker compose commands, the .NET gate with the dotnet commands in AGENTS.md, and the governance checks with `dotnet run --project tools/Governance.Auditor -- <command>`.
- `STATUS.md` and `docs/testing/traceability.md` are generated. After you change `status.yaml`, run `status validate` and `status render`, and after you change tests or evidence, run `trace`. Never edit the pages by hand.
- Use the repository-local noreply Git identity described in ADR-0006 and never set a personal email in this repository.
- Keep the GitHub account setting Keep my email addresses private turned on. After every merge, check the author and committer emails of the merged range (see AGENTS.md).
- The five spike branches (spike/1.a-pactnet-kestrel through spike/1.e-rotating-credentials) exist only on the machine that ran them and are never pushed. The spike reports carry the versions, image digests, commands, and key snippets needed to repeat them.
- Local clusters, Vault keys, and generated passwords belong to one machine and are never migrated. They do not exist yet.

### Resume on another machine

1. Install Git, Docker, the GitHub CLI, the .NET SDK 10.0.401, and PowerShell 7 with Pester 5 for the script tests. The Dev Container (WP1.4) will replace this step.
2. Clone the repository, run `git fetch origin`, and check out the branch named in the table above.
3. Set the repository-local noreply Git identity and turn on "Keep my email addresses private" in your GitHub account settings ([ADR-0006](../adr/0006-commit-identity-and-privacy.md)). Without the setting, GitHub uses your personal email for the merges it creates.
4. Read this page, then [AGENTS.md](../../AGENTS.md).
5. Check that the status files agree, then run the documentation gate from the repository root (the same commands CI runs, defined in [tools/lint/compose.yaml](../../tools/lint/compose.yaml)):

   ```text
   dotnet run --project tools/Governance.Auditor -- status validate
   dotnet run --project tools/Governance.Auditor -- status render --check
   docker compose -f tools/lint/compose.yaml run --rm markdownlint
   docker compose -f tools/lint/compose.yaml run --rm links
   docker compose -f tools/lint/compose.yaml run --rm secrets-worktree
   ```

6. Continue with the next action in the table.

Local environments are disposable. Clusters, Vault keys, and generated passwords belong to one machine and are regenerated on a new one, never migrated.

## Phases

| Phase | Name | State | Release | Plan | Milestone |
| --- | --- | --- | --- | --- | --- |
| 0 | Foundation and early publication | Completed | None | [Phase 0](../plans/phases/phase-0-foundation.md) | [Phase 0: Foundation](https://github.com/RostomOhannessian/full-ci-cd-pipeline/milestone/1) |
| 1 | API core, data layer, and engineering foundation | In progress | `v0.1.0` | [Phase 1](../plans/phases/phase-1-api-core.md) | [Phase 1: API core](https://github.com/RostomOhannessian/full-ci-cd-pipeline/milestone/2) |
| 2 | CI pipeline and shift-left security | Planned | `v0.2.0` | [Phase 2](../plans/phases/phase-2-secure-ci.md) | [Phase 2: Secure CI](https://github.com/RostomOhannessian/full-ci-cd-pipeline/milestone/3) |
| 3 | Zero-trust platform with Terraform | Planned | `v0.3.0` | [Phase 3](../plans/phases/phase-3-zero-trust-platform.md) | [Phase 3: Zero-trust platform](https://github.com/RostomOhannessian/full-ci-cd-pipeline/milestone/4) |
| 4 | Contract testing, GitOps, and progressive delivery | Planned | `v0.4.0` | [Phase 4](../plans/phases/phase-4-contracts-gitops.md) | [Phase 4: Contracts and GitOps](https://github.com/RostomOhannessian/full-ci-cd-pipeline/milestone/5) |
| launch | v1.0 launch gate | Planned | `v1.0.0` | [Launch gate](../plans/phases/launch-v1.md) | [v1.0 launch](https://github.com/RostomOhannessian/full-ci-cd-pipeline/milestone/6) |

## Phase 0 work packages

| WP | Title | Size | State | Issue | Pull request | Depends on |
| --- | --- | --- | --- | --- | --- | --- |
| WP0.1 | Identity and hygiene | S | Completed | None | [Pull request 1](https://github.com/RostomOhannessian/full-ci-cd-pipeline/pull/1) | None |
| WP0.2 | Plans and tracking | M | Completed | None | [Pull request 1](https://github.com/RostomOhannessian/full-ci-cd-pipeline/pull/1) | WP0.1 |
| WP0.3 | Governance and community files | S | Completed | None | [Pull request 1](https://github.com/RostomOhannessian/full-ci-cd-pipeline/pull/1) | WP0.1 |
| WP0.4 | Decisions and docs skeleton | M | Completed | None | [Pull request 1](https://github.com/RostomOhannessian/full-ci-cd-pipeline/pull/1) | WP0.1 |
| WP0.5 | AI enablement | S | Completed | None | [Pull request 1](https://github.com/RostomOhannessian/full-ci-cd-pipeline/pull/1) | WP0.1 |
| WP0.6 | Documentation quality gate | S | Completed | None | [Pull request 1](https://github.com/RostomOhannessian/full-ci-cd-pipeline/pull/1) | WP0.1 |
| WP0.7 | Publication and GitHub configuration | S | Completed | None | [Pull request 16](https://github.com/RostomOhannessian/full-ci-cd-pipeline/pull/16) | WP0.2, WP0.3, WP0.4, WP0.5, WP0.6 |

Evidence for the work packages that have it:

- **WP0.1:** [docs/adr/0006-commit-identity-and-privacy.md](../adr/0006-commit-identity-and-privacy.md), [.gitattributes](../../.gitattributes), [.gitignore](../../.gitignore), [.editorconfig](../../.editorconfig)
- **WP0.2:** [docs/plans/implementation-plan.md](../plans/implementation-plan.md), `docs/plans/phases/`, [docs/requirements/requirements.yaml](../requirements/requirements.yaml), [docs/requirements/brief-traceability.md](../requirements/brief-traceability.md), [docs/project/status.yaml](status.yaml), [docs/project/risk-register.md](risk-register.md), `docs/research/`, [docs/reference/tools/inventory.yaml](../reference/tools/inventory.yaml)
- **WP0.3:** [README.md](../../README.md), [LICENSE](../../LICENSE), [NOTICE](../../NOTICE), [CONTRIBUTING.md](../../CONTRIBUTING.md), [CODE_OF_CONDUCT.md](../../CODE_OF_CONDUCT.md), [SECURITY.md](../../SECURITY.md), [SUPPORT.md](../../SUPPORT.md), [GOVERNANCE.md](../../GOVERNANCE.md), [CHANGELOG.md](../../CHANGELOG.md), `.github/ISSUE_TEMPLATE/`, [.github/PULL_REQUEST_TEMPLATE.md](../../.github/PULL_REQUEST_TEMPLATE.md), [.github/CODEOWNERS](../../.github/CODEOWNERS)
- **WP0.4:** `docs/adr/`, `docs/templates/`, [docs/glossary.md](../glossary.md), [docs/security/threat-model.md](../security/threat-model.md), [docs/testing/strategy.md](../testing/strategy.md)
- **WP0.5:** [AGENTS.md](../../AGENTS.md), [REVIEW.md](../../REVIEW.md), [.github/copilot-instructions.md](../../.github/copilot-instructions.md), `.github/instructions/`, `.github/skills/`, [docs/governance/ai-skills.md](../governance/ai-skills.md)
- **WP0.6:** [.github/workflows/docs-quality.yml](../../.github/workflows/docs-quality.yml), [tools/lint/compose.yaml](../../tools/lint/compose.yaml), [.github/dependabot.yml](../../.github/dependabot.yml), [external evidence](https://github.com/RostomOhannessian/full-ci-cd-pipeline/actions/runs/37243822276)
- **WP0.7:** [docs/governance/github-settings.md](../governance/github-settings.md), `governance/github/`, [docs/journal/phase-0-retrospective.md](../journal/phase-0-retrospective.md)

## Phase 1 work packages

| WP | Title | Size | State | Issue | Pull request | Depends on |
| --- | --- | --- | --- | --- | --- | --- |
| WP1.0 | Risk spikes | S | Completed | [Issue 2](https://github.com/RostomOhannessian/full-ci-cd-pipeline/issues/2) | [Pull request 17](https://github.com/RostomOhannessian/full-ci-cd-pipeline/pull/17) | WP0.7 |
| WP1.1 | Solution and build foundation | M | Completed | [Issue 3](https://github.com/RostomOhannessian/full-ci-cd-pipeline/issues/3) | [Pull request 18](https://github.com/RostomOhannessian/full-ci-cd-pipeline/pull/18) | WP0.7 |
| WP1.2 | Governance core | M | Completed | [Issue 4](https://github.com/RostomOhannessian/full-ci-cd-pipeline/issues/4) | [Pull request 19](https://github.com/RostomOhannessian/full-ci-cd-pipeline/pull/19) | WP1.1 |
| WP1.3 | Documentation toolchain | M | Planned | [Issue 5](https://github.com/RostomOhannessian/full-ci-cd-pipeline/issues/5) | None | WP1.1 |
| WP1.4 | Developer environment | L | Planned | [Issue 6](https://github.com/RostomOhannessian/full-ci-cd-pipeline/issues/6) | None | WP1.1 |
| WP1.5 | Contract v1 design | M | Planned | [Issue 7](https://github.com/RostomOhannessian/full-ci-cd-pipeline/issues/7) | None | WP1.1 |
| WP1.6 | Domain model | M | Planned | [Issue 8](https://github.com/RostomOhannessian/full-ci-cd-pipeline/issues/8) | None | WP1.1 |
| WP1.7 | Application layer | L | Planned | [Issue 9](https://github.com/RostomOhannessian/full-ci-cd-pipeline/issues/9) | None | WP1.6 |
| WP1.8 | Persistence and outbox | L | Planned | [Issue 10](https://github.com/RostomOhannessian/full-ci-cd-pipeline/issues/10) | None | WP1.0, WP1.7 |
| WP1.9 | Caching | M | Planned | [Issue 11](https://github.com/RostomOhannessian/full-ci-cd-pipeline/issues/11) | None | WP1.0, WP1.7 |
| WP1.10 | HTTP API | L | Planned | [Issue 12](https://github.com/RostomOhannessian/full-ci-cd-pipeline/issues/12) | None | WP1.4, WP1.5, WP1.8, WP1.9 |
| WP1.11 | Observability | M | Planned | [Issue 13](https://github.com/RostomOhannessian/full-ci-cd-pipeline/issues/13) | None | WP1.10 |
| WP1.12 | Auditor depth and AI skills | M | Planned | [Issue 14](https://github.com/RostomOhannessian/full-ci-cd-pipeline/issues/14) | None | WP1.2, WP1.3 |
| WP1.13 | Learning content and phase exit | M | Planned | [Issue 15](https://github.com/RostomOhannessian/full-ci-cd-pipeline/issues/15) | None | WP1.11, WP1.12 |

Evidence for the work packages that have it:

- **WP1.0:** [docs/research/spikes/README.md](../research/spikes/README.md), [docs/research/spikes/1.a-pactnet-kestrel.md](../research/spikes/1.a-pactnet-kestrel.md), [docs/research/spikes/1.b-fusioncache-valkey.md](../research/spikes/1.b-fusioncache-valkey.md), [docs/research/spikes/1.c-mtp-coverage-stryker.md](../research/spikes/1.c-mtp-coverage-stryker.md), [docs/research/spikes/1.d-testcontainers-mssql.md](../research/spikes/1.d-testcontainers-mssql.md), [docs/research/spikes/1.e-rotating-credentials.md](../research/spikes/1.e-rotating-credentials.md), [docs/adr/0016-cache-implementation-fusioncache.md](../adr/0016-cache-implementation-fusioncache.md), [docs/adr/0017-contract-first-api-design.md](../adr/0017-contract-first-api-design.md), [docs/adr/0018-test-stack-and-quality-gates.md](../adr/0018-test-stack-and-quality-gates.md), [docs/adr/0019-resource-profiles-and-host-support.md](../adr/0019-resource-profiles-and-host-support.md), [docs/adr/0004-licensing-and-dependency-license-policy.md](../adr/0004-licensing-and-dependency-license-policy.md)
- **WP1.1:** [ProductCatalog.slnx](../../ProductCatalog.slnx), [global.json](../../global.json), [Directory.Build.props](../../Directory.Build.props), [Directory.Packages.props](../../Directory.Packages.props), [NuGet.config](../../NuGet.config), [.github/workflows/ci.yml](../../.github/workflows/ci.yml), `tests/Catalog.Architecture.Tests/`, `governance/policies/`, `tools/ci/`, [docs/adr/0018-test-stack-and-quality-gates.md](../adr/0018-test-stack-and-quality-gates.md), [docs/adr/0020-dependency-intake-controls.md](../adr/0020-dependency-intake-controls.md)
- **WP1.2:** `tools/Governance.Auditor/`, `tests/Governance.Auditor.Tests/`, [governance/policies/security-policy.yaml](../../governance/policies/security-policy.yaml), [governance/policies/blast-radius-map.yaml](../../governance/policies/blast-radius-map.yaml), [.github/workflows/governance.yml](../../.github/workflows/governance.yml), [.github/skills/advanced-security-auditor/SKILL.md](../../.github/skills/advanced-security-auditor/SKILL.md), [.github/skills/blast-radius-evaluator/SKILL.md](../../.github/skills/blast-radius-evaluator/SKILL.md), [docs/testing/evidence.yaml](../testing/evidence.yaml), [docs/testing/traceability.md](../testing/traceability.md), [docs/adr/0021-governance-auditor-cli.md](../adr/0021-governance-auditor-cli.md), [docs/adr/0022-generated-status-and-traceability.md](../adr/0022-generated-status-and-traceability.md)

## Phases that have not started

On the phases that have not started, phase 2 has 8 work packages; phase 3 has 11 work packages; phase 4 has 9 work packages. Every one of these work packages is planned, and its order and dependencies are in [status.yaml](status.yaml).

## Decisions and risks

[The ADR index](../adr/README.md) lists every decision with its status and the work package that accepts it. The [risk register](risk-register.md) lists the open risks, their mitigations, and the triggers that reopen them.

## How to update this page

1. Edit [status.yaml](status.yaml) in the same commit as the change it records.
2. Run `dotnet run --project tools/Governance.Auditor -- status validate`, then `dotnet run --project tools/Governance.Auditor -- status render`.
3. Commit and push the branch, so another machine can resume. The `session-handoff` skill walks through these steps ([skill file](../../.github/skills/session-handoff/SKILL.md)).

The wording that does not come from `status.yaml` lives in `tools/Governance.Auditor/Status/StatusPage.template`.
