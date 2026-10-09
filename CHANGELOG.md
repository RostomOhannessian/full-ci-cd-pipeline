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

### Fixed

- Two dead source links in the tool inventory (the Aspire overview and the Grafana docker-lgtm page).
- The tool inventory listed Stryker.NET as MIT. The package's own license file is Apache-2.0, and the correction is recorded in the decision-critical addendum.

[Unreleased]: https://github.com/RostomOhannessian/full-ci-cd-pipeline/commits/master
