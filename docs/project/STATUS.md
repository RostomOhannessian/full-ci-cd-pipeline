---
title: "Project status"
description: "Where the project is now, how to resume work on another machine, and the state of every phase."
type: status
audience: [maintainers, contributors, learners]
last-verified: 2026-10-11
owner: "@RostomOhannessian"
---

# Project status

This page is generated from [status.yaml](status.yaml), which is the source of truth, by `governance status render`. Do not edit it by hand. Change `status.yaml`, run the command, and commit both files. CI fails when this page differs from the command's output. Last updated 2026-10-11.

## Resume here

| Item | Value |
| --- | --- |
| Active phase | [Phase 1: API core, data layer, and engineering foundation](../plans/phases/phase-1-api-core.md), in progress |
| Active work package | None |
| Branch | `phase/1-api-core` |
| Last completed | WP1.5 is complete: the contract v1 design. The authored OpenAPI 3.1.0 contract (contracts/openapi/catalog/v1/catalog.openapi.yaml) defines the 10 operations on categories and products, Problem Details for 17 error types, strong ETags with If-Match and If-None-Match, an Idempotency-Key on every create, opaque keyset cursors, the version and deprecation headers, the three OAuth scopes, and a valid example for every body (ADR-0028). Spectral 6.17.0 lints it from a digest-pinned container with 15 catalog rules (ADR-0027), a Contract lint job runs it in ci.yml, and a Pester test breaks the contract once for each rule and expects the rule to fire. 86 contract tests read the contract with Microsoft.OpenApi 2.12.2 and check it against the permission matrix, the error catalog, and the style guide. tools/ci/Test-ContractConformance.ps1 compares a served document with the contract through oasdiff 1.33.0, and its tests show wording changes passing and seven kinds of structural change failing. The API contract page, the API style guide, the error catalog, three tool pages, and ADR-0017 (accepted), ADR-0027, and ADR-0028 are new. 775 .NET tests and 337 Pester tests pass, the build has zero warnings, and the format, license, coverage-threshold, and governance gates pass. Each new gate was shown to fail on a planted defect. The work landed through pull request 22 into phase/1-api-core. WP1.0 to WP1.4 landed earlier through pull requests 17 to 21. |
| Next action | Start WP1.6, Domain model (Issue 8): create wp/1.6-domain-model from phase/1-api-core. It builds the aggregates, value objects, IDs, events, and invariants of plan section 8.3 and the unit and property tests, and it must follow the contract: the field names, bounds, and money format are in ADR-0028 and the contract, and WP1.6 owns the final currency list. The owner has decisions waiting. First, close Issues 2, 3, and 4, which github-sync shows as the only drift (run it as a dry run, and pass --apply only with the maintainer's approval). Second, whether to add the ci jobs (Build and test, Script tests, Format verification, License gate, Conventional Commit title, Contract lint), the Governance auditors job, the four documentation jobs (Spelling, Diagrams, Documentation site, Documentation auditor), and the Dev Container onboarding job as required checks in the master and phase rulesets, which is a settings change that needs the owner's approval. Third, whether to add tools/Governance.Auditor/ and tools/Documentation.Auditor/ to CODEOWNERS, because each auditor is built from the pull request it audits (risk R20). Fourth, whether to correct the cache ACL patterns in plan section 8.5, as proposed in spike 1.b. Fifth, whether to amend plan section 8.7, because Keycloak 26.8.0 does not withhold a requested scope from a user without the mapped role, so the API must authorize on the roles claim as well as the scope (ADR-0026, risk R21). Sixth, the Testcontainers evidence for Linux is expected from WP1.8, because no work package before it has container tests. |
| Open pull requests | None |

### Environment notes

- Open the Dev Container, or install Git, Docker (running), and the .NET SDK 10.0.401 and follow docs/tutorials/native-setup.md. Run `./dev doctor` first (on Windows, `./dev` in PowerShell or `.\dev` in cmd.exe), then `./dev bootstrap` to install the pinned script tools and create .env, and `./dev test scripts` for the Pester tests. The documentation gate runs through the docker compose commands (markdownlint, links, spelling, diagrams, secrets, secrets-worktree), the documentation auditor through `dotnet run --project tools/Documentation.Auditor -- audit`, the site build through `pwsh tools/ci/Invoke-DocFx.ps1 -Command build`, the .NET gate through the dotnet commands in AGENTS.md, and the governance checks through `dotnet run --project tools/Governance.Auditor -- <command>`.
- The local stack is `./dev compose up`, `./dev compose check`, and `./dev compose down`. It needs about 3 GB of Docker memory, binds 127.0.0.1 only, and keeps its secrets in the git-ignored .env, which never moves between machines. Delete _site and .docfx before the working-tree secret scan, because the site build output triggers false findings.
- The API contract is authored first, in contracts/openapi/catalog/v1/catalog.openapi.yaml. Change it, run `docker compose -f tools/lint/compose.yaml run --rm contract-lint`, run `dotnet test`, which includes tests/Catalog.Contract.Tests, and update docs/reference/api-contract.md and error-catalog.md in the same change. A new lint rule needs a break in tools/ci/ContractRules.Tests.ps1, and the script tests need Docker.
- A tool that you add to the repository needs a tool-inventory entry with a `detect` rule, and the documentation auditor fails when the files and the inventory disagree. Use the tool-doc-author skill, then run the audit.
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
- **WP0.6:** [.github/workflows/docs.yml](../../.github/workflows/docs.yml), [tools/lint/compose.yaml](../../tools/lint/compose.yaml), [.github/dependabot.yml](../../.github/dependabot.yml), [external evidence](https://github.com/RostomOhannessian/full-ci-cd-pipeline/actions/runs/37243822276)
- **WP0.7:** [docs/governance/github-settings.md](../governance/github-settings.md), `governance/github/`, [docs/journal/phase-0-retrospective.md](../journal/phase-0-retrospective.md)

## Phase 1 work packages

| WP | Title | Size | State | Issue | Pull request | Depends on |
| --- | --- | --- | --- | --- | --- | --- |
| WP1.0 | Risk spikes | S | Completed | [Issue 2](https://github.com/RostomOhannessian/full-ci-cd-pipeline/issues/2) | [Pull request 17](https://github.com/RostomOhannessian/full-ci-cd-pipeline/pull/17) | WP0.7 |
| WP1.1 | Solution and build foundation | M | Completed | [Issue 3](https://github.com/RostomOhannessian/full-ci-cd-pipeline/issues/3) | [Pull request 18](https://github.com/RostomOhannessian/full-ci-cd-pipeline/pull/18) | WP0.7 |
| WP1.2 | Governance core | M | Completed | [Issue 4](https://github.com/RostomOhannessian/full-ci-cd-pipeline/issues/4) | [Pull request 19](https://github.com/RostomOhannessian/full-ci-cd-pipeline/pull/19) | WP1.1 |
| WP1.3 | Documentation toolchain | M | Completed | [Issue 5](https://github.com/RostomOhannessian/full-ci-cd-pipeline/issues/5) | [Pull request 20](https://github.com/RostomOhannessian/full-ci-cd-pipeline/pull/20) | WP1.1 |
| WP1.4 | Developer environment | L | Completed | [Issue 6](https://github.com/RostomOhannessian/full-ci-cd-pipeline/issues/6) | [Pull request 21](https://github.com/RostomOhannessian/full-ci-cd-pipeline/pull/21) | WP1.1 |
| WP1.5 | Contract v1 design | M | Completed | [Issue 7](https://github.com/RostomOhannessian/full-ci-cd-pipeline/issues/7) | [Pull request 22](https://github.com/RostomOhannessian/full-ci-cd-pipeline/pull/22) | WP1.1 |
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
- **WP1.2:** `tools/Governance.Auditor/`, `tests/Governance.Auditor.Tests/`, [governance/policies/security-policy.yaml](../../governance/policies/security-policy.yaml), [governance/policies/blast-radius-map.yaml](../../governance/policies/blast-radius-map.yaml), [.github/workflows/governance.yml](../../.github/workflows/governance.yml), [.github/skills/advanced-security-auditor/SKILL.md](../../.github/skills/advanced-security-auditor/SKILL.md), [.github/skills/blast-radius-evaluator/SKILL.md](../../.github/skills/blast-radius-evaluator/SKILL.md), [docs/testing/evidence.yaml](../testing/evidence.yaml), [docs/testing/traceability.md](../testing/traceability.md), [docs/testing/evidence/2026-10-08-fresh-clone-resume.md](../testing/evidence/2026-10-08-fresh-clone-resume.md), [docs/adr/0021-governance-auditor-cli.md](../adr/0021-governance-auditor-cli.md), [docs/adr/0022-generated-status-and-traceability.md](../adr/0022-generated-status-and-traceability.md)
- **WP1.3:** `tools/Documentation.Auditor/`, `tests/Documentation.Auditor.Tests/`, [governance/policies/documentation-policy.yaml](../../governance/policies/documentation-policy.yaml), [docs/reference/tools/inventory.schema.json](../reference/tools/inventory.schema.json), `docs/reference/tools/`, [.github/workflows/docs.yml](../../.github/workflows/docs.yml), [docfx.json](../../docfx.json), [cspell.json](../../cspell.json), [lychee-external.toml](../../lychee-external.toml), [tools/lint/compose.yaml](../../tools/lint/compose.yaml), [tools/lint/check-diagrams.sh](../../tools/lint/check-diagrams.sh), [tools/ci/Invoke-DocFx.ps1](../../tools/ci/Invoke-DocFx.ps1), [.github/skills/documentation-auditor/SKILL.md](../../.github/skills/documentation-auditor/SKILL.md), [docs/adr/0023-documentation-auditor.md](../adr/0023-documentation-auditor.md)
- **WP1.4:** `.devcontainer/`, [dev](../../dev), [dev.cmd](../../dev.cmd), `scripts/`, [tools/versions.yaml](../../tools/versions.yaml), [compose.yaml](../../compose.yaml), [.env.example](../../.env.example), [identity/keycloak/realms/catalog-realm.json](../../identity/keycloak/realms/catalog-realm.json), [.github/workflows/devcontainer.yml](../../.github/workflows/devcontainer.yml), [tools/lint/gitleaks-worktree.toml](../../tools/lint/gitleaks-worktree.toml), [tools/Documentation.Auditor/Discovery/Scanners.cs](../../tools/Documentation.Auditor/Discovery/Scanners.cs), [docs/testing/evidence/2026-10-10-dev-container-quick-start.md](../testing/evidence/2026-10-10-dev-container-quick-start.md), [docs/labs/quick-start-api-stack.md](../labs/quick-start-api-stack.md), [docs/runbooks/machine-migration.md](../runbooks/machine-migration.md), [docs/tutorials/native-setup.md](../tutorials/native-setup.md), [docs/reference/host-support.md](../reference/host-support.md), [docs/adr/0024-verified-tool-installer.md](../adr/0024-verified-tool-installer.md), [docs/adr/0025-dev-command-and-dev-container.md](../adr/0025-dev-command-and-dev-container.md), [docs/adr/0026-api-only-compose-stack-and-catalog-realm.md](../adr/0026-api-only-compose-stack-and-catalog-realm.md)
- **WP1.5:** [contracts/openapi/catalog/v1/catalog.openapi.yaml](../../contracts/openapi/catalog/v1/catalog.openapi.yaml), [contracts/openapi/spectral.yaml](../../contracts/openapi/spectral.yaml), `tests/Catalog.Contract.Tests/`, [tools/ci/ContractRules.Tests.ps1](../../tools/ci/ContractRules.Tests.ps1), [tools/ci/Test-ContractConformance.ps1](../../tools/ci/Test-ContractConformance.ps1), [tools/ci/ContractConformance.Tests.ps1](../../tools/ci/ContractConformance.Tests.ps1), [tools/lint/compose.yaml](../../tools/lint/compose.yaml), [.github/workflows/ci.yml](../../.github/workflows/ci.yml), [docs/reference/api-contract.md](../reference/api-contract.md), [docs/reference/api-style-guide.md](../reference/api-style-guide.md), [docs/reference/error-catalog.md](../reference/error-catalog.md), [docs/adr/0017-contract-first-api-design.md](../adr/0017-contract-first-api-design.md), [docs/adr/0027-contract-linter-and-conformance-tooling.md](../adr/0027-contract-linter-and-conformance-tooling.md), [docs/adr/0028-http-api-conventions-of-v1.md](../adr/0028-http-api-conventions-of-v1.md)

## Phases that have not started

On the phases that have not started, phase 2 has 8 work packages; phase 3 has 11 work packages; phase 4 has 9 work packages. Every one of these work packages is planned, and its order and dependencies are in [status.yaml](status.yaml).

## Decisions and risks

[The ADR index](../adr/README.md) lists every decision with its status and the work package that accepts it. The [risk register](risk-register.md) lists the open risks, their mitigations, and the triggers that reopen them.

## How to update this page

1. Edit [status.yaml](status.yaml) in the same commit as the change it records.
2. Run `dotnet run --project tools/Governance.Auditor -- status validate`, then `dotnet run --project tools/Governance.Auditor -- status render`.
3. Commit and push the branch, so another machine can resume. The `session-handoff` skill walks through these steps ([skill file](../../.github/skills/session-handoff/SKILL.md)).

The wording that does not come from `status.yaml` lives in `tools/Governance.Auditor/Status/StatusPage.template`.
