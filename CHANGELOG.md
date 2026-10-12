# Changelog

All notable changes to this project are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and the project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html). From `v0.2.0` on, release notes are generated from
Conventional Commits and attached to each release together with its evidence bundle.

## [Unreleased]

### Added

- Phase 0 foundation: the approved implementation plan, one plan per phase, the project status tracker, requirements and traceability,
  and the risk register.
- Architecture decision records ADR-0001 to ADR-0019: accepted process decisions and proposed, evidence-backed technology decisions.
- Research records with dated, source-checked evidence behind the plan.
- Tool inventory with lifecycle, license, and version data for every planned and evaluated tool.
- Community and governance files: Apache-2.0 license, notice, contributing guide, code of conduct, security policy, support guide,
  governance, issue forms, and a pull request template.
- Documentation skeleton: Diataxis index pages, templates, a glossary, a first threat model, and the testing strategy.
- Agent instructions, a review checklist, and repository skills for portable AI-assisted work.
- Documentation quality gate: Markdown style, offline link and anchor checks, and a full-history secret scan, run identically
  in CI and locally.
- Dependabot configuration for GitHub Actions and the pinned lint tool images.
- Publication: the repository is public with secret scanning, push protection, private vulnerability reporting, hardened Actions settings, two rulesets, labels, milestones, and the 14 Phase 1 work-package Issues. The settings are recorded in `docs/governance/github-settings.md` and `governance/github/`.
- The Phase 0 retrospective.
- WP1.0 risk spikes: five reports under `docs/research/spikes/` covering PactNet on Kestrel, the FusionCache backplane on Valkey, Microsoft.Testing.Platform with coverage and Stryker, Testcontainers SQL Server, and file-rotated database credentials, with dated evidence added to ADR-0004, ADR-0016, ADR-0017, ADR-0018, and ADR-0019 and updated tool inventory notes and risk entries.
- WP1.1 solution and build foundation: `ProductCatalog.slnx` with six server projects under Clean Architecture boundaries and `Catalog.Architecture.Tests`, a `global.json` that pins SDK 10.0.401 and selects Microsoft.Testing.Platform, shared build settings with warnings as errors and analyzers at `latest-recommended`, central package versions with transitive pinning and committed lock files, and a `NuGet.config` with source mapping and required nuget.org repository signatures.
- 58 architecture, reference, and dependency tests. Each architecture rule runs against conforming and violating fixtures, so every rule is shown to be able to fail, and planted violations in production code were caught.
- The `ci` workflow: locked restore, Release build, unit and architecture tests with coverage and TRX, a coverage threshold gate with Pester tests, script analysis, a format check, a license gate, and a Conventional Commit pull-request title check. Every action is pinned by commit SHA.
- Policy files in `governance/policies/`: the license allow-list and its single tool-only exception, the forbidden-package list from ADR-0004, and the coverage thresholds from plan section 10.4.
- Dependabot ecosystems for NuGet, Docker, Dev Containers, and Docker Compose.
- ADR-0020 on dependency intake controls, and ADR-0018 accepted.
- WP1.2 governance core: the `governance` command line in `tools/Governance.Auditor` with `status validate` and `status render`, `trace`, `test-summary`, `github-sync`, `security`, and `blast-radius`. It runs without a shell, makes no network calls except through the GitHub CLI in `github-sync`, and refuses YAML anchors and unsupported schema keywords. Its schema validator is written in-house, because the common .NET validator carries a non-OSI license.
- 364 auditor tests tagged with requirement IDs, including goldens for the generated pages and for both skill fixtures, and one violating fixture for every security rule.
- `governance.yml`: a workflow that validates the status file, checks the generated pages for drift, scans the pull request with the security auditor, and publishes the blast-radius result as job outputs. Its permissions are explicit and minimal.
- `governance/policies/security-policy.yaml` and `blast-radius-map.yaml`: the secret patterns, allow-lists, denied license prefixes, commit-identity patterns, and the path-to-layer, job, document, and review map.
- The `/advanced-security-auditor` and `/blast-radius-evaluator` skills, which wrap the commands with read-only scopes.
- The evidence register, `docs/testing/evidence.yaml`, with its schema, for requirements that tests cannot prove, and the generated `docs/testing/traceability.md`.
- ADR-0021 on the governance command line and ADR-0022 on generated status and traceability, both accepted.
- Tool inventory entries for System.CommandLine, YamlDotNet, the governance auditor, and the GitHub CLI, and threat model rows for the auditor, the status generator, and `github-sync`.
- WP1.3 documentation toolchain: DocFX with the modern template and warnings as errors, a pull request preview artifact, the `docs.yml` workflow (renamed from `docs-quality.yml`) with spelling, Mermaid rendering, the site build, and the documentation auditor on every pull request, and an external link check every night, with digest-pinned `spelling`, `diagrams`, and `external-links` lint services.
- `Documentation.Auditor` in `tools/Documentation.Auditor`: a command line that discovers the tools that the repository uses and compares them with the tool inventory in both directions, requires a license note for a non-permissive tool, checks the front matter and the sections of each tool page by tier, warns about a stale `last-verified` date, requires tested or `illustrative` command blocks, verifies includes and diagram accessibility text, and lists the 18 tool pages that are owed until WP1.13. Its rules are data in `governance/policies/documentation-policy.yaml`, and it has 249 tests tagged with requirement IDs.
- The `/documentation-auditor` skill, which wraps the audit with a read-only scope, and ADR-0023 on the auditor's design.
- Six tool pages (DocFX, cspell, Mermaid CLI, lychee, markdownlint-cli2, and the documentation auditor), a catalog page for System.CommandLine and YamlDotNet, the tool inventory schema with a `detect` field, and `detect` rules for the tools in use.
- WP1.4 developer environment: the `dev` command (`doctor`, `bootstrap`, `build`, `test`, `docs`, `compose`, and `lab`) behind `dev` and `dev.cmd` shims, written in PowerShell with a restricted YAML reader and Pester tests, and a verified tool installer that pins each developer tool to a version and a SHA-256 in `tools/versions.yaml` ([ADR-0024](docs/adr/0024-verified-tool-installer.md), [ADR-0025](docs/adr/0025-dev-command-and-dev-container.md)).
- The Dev Container (a digest-pinned .NET base image, locked docker-in-docker and GitHub CLI features, and the `dev doctor` check on creation) and the `devcontainer` workflow, which builds it and runs the onboarding path on a hosted runner.
- The API-only Compose stack: SQL Server, Valkey with an ACL user, Keycloak with the imported catalog realm, and the Grafana LGTM telemetry stack, all digest-pinned, bound to `127.0.0.1`, and limited in memory, with a generated `.env`. `dev compose check` proves the logins, the ACL, the token claims, and the role matrix ([ADR-0026](docs/adr/0026-api-only-compose-stack-and-catalog-realm.md)).
- Documentation auditor scanners for the Dev Container (features and lock file) and `tools/versions.yaml`, with `feature` and `psmodule` detect kinds, and the unsupported-source entries for both removed.
- Lab 1 (quick start), the machine-migration runbook, the native setup tutorial, the host support matrix, and tool pages for the Dev Containers, PowerShell, the `dev` command, SQL Server, Valkey, Keycloak, and the telemetry stack.
- A narrow allowance in `tools/lint/gitleaks-worktree.toml` for the git-ignored `.env`, used only by the `secrets-worktree` working-tree scan. The full-history `secrets` scan is unchanged.
- WP1.5 contract v1 design: the authored OpenAPI 3.1 contract in `contracts/openapi/catalog/v1/catalog.openapi.yaml` with 10 operations on categories and products, Problem Details for 17 error types, strong ETags with `If-Match` and `If-None-Match`, an `Idempotency-Key` on every create, opaque keyset cursors, the version and deprecation headers, three OAuth scopes, and a valid example for every body.
- The contract linter: Spectral 6.17.0 from a digest-pinned container (`contract-lint` in `tools/lint/compose.yaml`) with a ruleset of 15 `catalog-` rules, a `Contract lint` job in `ci.yml`, and `tools/ci/ContractRules.Tests.ps1`, which breaks the contract once for each rule and expects the rule to fire.
- `tests/Catalog.Contract.Tests`: 86 tests tagged with requirement IDs that read the contract with Microsoft.OpenApi 2.12.2 and check it against the permission matrix, the error catalog, and the style guide, and that check the patterns of the headers against sample values.
- `tools/ci/Test-ContractConformance.ps1` and its tests: the comparison of a served OpenAPI document with the contract through oasdiff 1.33.0, which ignores descriptions, examples, and summaries. WP1.10 generates the served document.
- ADR-0017 accepted, ADR-0027 on the contract linter and the conformance tooling, ADR-0028 on the HTTP conventions of v1, the API contract page, the API style guide, the error catalog, and tool pages for OpenAPI, Spectral, and oasdiff.
- WP1.6 domain model: `src/Catalog.Domain` with the `Product` and `Category` aggregates, the value objects `Sku`, `ProductName`, `CategoryName`, `Description`, `Money`, `Currency`, `ProductId`, and `CategoryId`, a `DomainResult` that reports a refused rule as a value, and eight versioned, time-stamped domain events. It references the base class library only and takes the time from a `TimeProvider` ([ADR-0029](docs/adr/0029-domain-model-and-mutation-baseline.md), [the domain model page](docs/reference/domain-model.md)).
- `tests/Catalog.Domain.Tests`: 172 example and property tests tagged with requirement IDs, with AwesomeAssertions, CsCheck property tests against independent oracles, a model-based test of the product lifecycle, `FakeTimeProvider`, and identifiers built from a counter. They cover 99.6 percent of the lines and 98.4 percent of the branches of the Domain.
- `DomainAgreementTests` in `tests/Catalog.Contract.Tests`, which compares the bounds, the currency and status sets, the SKU pattern over every short string, the error fields, and the error codes of the model with the contract and the error catalog, and the page of the domain model with its events. `AssemblyReferenceTests` in `tests/Catalog.Architecture.Tests`, which reads the references of the built Domain from its metadata.
- The mutation baseline: Stryker.NET 5.0.0 in the tool manifest, a `mutation` service in `tools/lint/compose.yaml` that runs it in the digest-pinned Linux SDK image on a scratch copy of the repository, and an evidence record that scores the Domain at 94.62 percent and accounts for each survivor. The score is a report and not a gate.
- Tool pages for AwesomeAssertions, CsCheck, and Stryker.NET, and dependency catalog entries for `Microsoft.Extensions.TimeProvider.Testing` and the .NET SDK image. The documentation site has an API reference for the public types of the Domain.

### Changed

- The threat model is version 1: dependency intake, trusted signers, and pull-request code on CI runners are delivered controls, and the Testcontainers reaper is a planned one.
- Twelve tools in the inventory are marked `in-use`, with notes from this work package.
- AGENTS.md, CONTRIBUTING.md, and the testing strategy list the .NET commands that run today.
- The Linux evidence for Testcontainers moves from WP1.1 to WP1.8, because WP1.1 has no container tests.
- `docs/project/STATUS.md` is now generated from `docs/project/status.yaml`, and CI fails when it is out of date. Edit the YAML, never the page.
- `docs/testing/traceability.md` is generated, and CI fails when a requirement that is due has no test and no evidence.
- AGENTS.md, CONTRIBUTING.md, the repository instructions, the testing strategy, and the AI skills page describe the governance commands that run today. The planned-commands table lists only what is still to come.
- The `/session-handoff` skill renders `STATUS.md` instead of asking for a hand edit.
- `.gitattributes` marks the two generated pages as generated, and `.gitignore` ignores the Pester result file.
- The architecture tests treat `tools/` as a project root.
- Requirements GOV-002 and GOV-004 are verified, and GOV-003 is in progress until `dev doctor` arrives in WP1.4.
- The tool inventory marks markdownlint-cli2, lychee, gitleaks, Docker Compose, cspell, DocFX, Mermaid CLI, and the repository instruction files as `in-use`, and the documentation workflow is `docs.yml`. Requirement DOC-003 is verified, and CI-005, DOC-002, DOC-004, and DOC-005 are in progress until their later work packages.
- AGENTS.md, CONTRIBUTING.md, the README, the testing strategy, and the threat model describe the `dev` command and the local stack. The planned-commands table lists only the Phase 3 commands.
- The `ci` workflow runs the script tests through `scripts/Invoke-ScriptTests.ps1`, which installs the pinned and checksum-verified Pester and PSScriptAnalyzer instead of whatever the runner image carries. Dependabot also watches `/.devcontainer`.
- The blast-radius map has a `devcontainer` job and a `tool-manifest` rule, and `governance.yml` publishes `run-devcontainer`. The tool inventory marks the Dev Containers, PowerShell, Pester, PSScriptAnalyzer, SQL Server, Valkey, Keycloak, and the telemetry stack as `in-use`, mise as `hold`, and Codespaces stays planned.
- The threat model has a developer-environment boundary (TB2a), and the risk register has R21 (the Keycloak scope and role finding) and R22 (the privileged Dev Container).
- The threat model's API edge (TB4) lists the contract controls: a scope on every operation, the preconditions, the idempotency rule, the linter that holds them, and the conformance check that holds the running API to them.
- The blast-radius map has a `contract` job, and a change under `contracts/` selects it with the build, the script tests, and the documentation site. `governance.yml` publishes `run-contract`.
- The tool inventory marks OpenAPI, Spectral, oasdiff, and Microsoft.OpenApi as `in-use` and replaces the `openapi-linter` placeholder with Spectral. Requirement CON-001 is verified, and API-001 to API-005 and CON-002 are in progress until WP1.7, WP1.8, and WP1.10.
- The `dev` command's unit suite includes `Catalog.Contract.Tests` without a change, because it finds test projects by name.
- The documentation site build leaves out `.env` and `.dev/`, because it copies every repository file that it does not exclude (the working-tree secret scan found a copy of a generated `.env` in `_site` before the exclusion). The link, spelling, and Markdown checks skip `.dev/`, where `dev bootstrap` installs downloaded tools.
- The contract and ADR-0028 say that a product is refused a discontinued category only when it moves there, so a product that stays in a category that was discontinued after the assignment can still change. The wording of `updateProduct` and `ProductUpdate` changed, and no field did.
- The DocFX metadata exception, which accepted the warning "No .NET API detected", is removed now that the Domain has public types. Both DocFX steps run with `--warningsAsErrors`, and the list of accepted warnings is empty.
- The architecture rule against static mutable fields skips constants and the members of an enum, which ArchUnitNET reports as writable. The metadata of the assemblies identifies them, and the violating fixture still fails the rule.
- The architecture tests no longer let the coverage extension instrument the production assemblies. On Linux it adds a tracker type to every assembly that has code, and the layer rules then judged a type that nobody wrote (the first hosted build of WP1.6 failed on it, and a Windows run does not show it). `coverage.settings.xml` excludes the assemblies, and a guard test fails when an assembly holds a type outside its own namespace.
- Requirement DOM-002 is verified, and ARC-001, DOM-001, DOM-003, DOM-004, DOM-005, QUA-004, QUA-005, and QUA-006 are in progress until WP1.7, WP1.8, and WP1.10. The tool inventory marks AwesomeAssertions, CsCheck, and Stryker.NET as `in-use`.

### Fixed

- The DocFX log check accepted every warning when the list of accepted warnings was empty, because an empty list that a function returns became a null pattern when it was passed on. A null or blank pattern now accepts nothing, and a test fails without the fix.
- Two dead source links in the tool inventory (the Aspire overview and the Grafana docker-lgtm page).
- The tool inventory listed Stryker.NET as MIT. The package's own license file is Apache-2.0, and the correction is recorded in the decision-critical addendum.

[Unreleased]: https://github.com/RostomOhannessian/full-ci-cd-pipeline/commits/master
