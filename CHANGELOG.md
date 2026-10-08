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

### Fixed

- The tool inventory listed Stryker.NET as MIT. The package's own license file is Apache-2.0, and the correction is recorded in the decision-critical addendum.

[Unreleased]: https://github.com/RostomOhannessian/full-ci-cd-pipeline/commits/master
