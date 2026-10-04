---
title: "Phase 1: API core, data layer, and engineering foundation"
description: "A production-quality Clean Architecture API that runs locally with one command, plus the governance, documentation, and testing machinery later phases rely on."
type: phase-plan
phase: 1
audience: [maintainers, learners]
last-verified: 2026-10-04
owner: "@RostomOhannessian"
---

# Phase 1: API core, data layer, and engineering foundation

Section numbers (§) in this file refer to [the implementation plan](../implementation-plan.md). Live progress is on [the status page](../../project/STATUS.md).

| Field | Value |
| --- | --- |
| Phase branch | `phase/1-api-core` |
| Work-package branches | `wp/1.NN-<slug>` |
| Milestone | Phase 1: API core |
| Release at exit | `v0.1.0` |
| Depends on | Phase 0 merged |

## Goal

a production-quality Clean Architecture API that runs locally with one command, with the governance, documentation, and testing machinery every later phase relies on.

## Learning outcomes

layering and dependency inversion, aggregates and value objects, CQRS with decorators, transactional outbox, cache consistency, HTTP correctness (ETags, idempotency, pagination), OIDC token validation, Testcontainers, telemetry, and docs-as-code.

## Scope and non-goals

In scope:

- The product catalog domain, application layer, persistence, transactional outbox, two-level cache, versioned HTTP API, identity integration, and telemetry.
- The governance and documentation machinery every later phase relies on: the two auditors, the status and traceability tooling, DocFX, and the Dev Container.
- A first-class developer experience: one command to run the API-only stack, tested onboarding, and a documented host support matrix.

Non-goals:

- No container image build, publishing, signing, or release automation beyond the first CI workflow (Phase 2).
- No Kubernetes, Terraform, Vault, dynamic credentials, or admission policy (Phase 3). The rotating-credential port is implemented and tested locally, but Compose and tests use static credentials.
- No Pact, generated client, GitOps, or canary delivery (Phase 4).
- No user interface, multi-tenancy, or claims of production readiness.

## Decisions introduced

| ADR | Decision | Accepted in |
| --- | --- | --- |
| [0018](../../adr/0018-test-stack-and-quality-gates.md) | Test stack and quality gates | WP1.1 |
| [0017](../../adr/0017-contract-first-api-design.md) | Contract-first API design | WP1.5 |
| [0012](../../adr/0012-domain-events-outbox-vs-direct-orchestration.md) | Domain events with a transactional outbox | WP1.8 |
| [0016](../../adr/0016-cache-implementation-fusioncache.md) | FusionCache behind an application port | WP1.9 |

New ADRs are written as decisions are made. Expected ones: the decorator order (WP1.7), the contract linter (WP1.5), and the tool installer (WP1.4).

## Prerequisites and setup changes

- Phase 0 is merged and the repository is public with its rulesets active.
- Git and Docker, plus either a Dev Container client (VS Code or Codespaces) or the .NET SDK 10.0.401 with PowerShell restored as a local tool.
- Docker allocation for the `api` profile: about 4 to 5 GB of memory and 2 or more CPUs. SQL Server containers are x86-64 only; Apple Silicon is best effort (see [ADR-0019](../../adr/0019-resource-profiles-and-host-support.md)).
- Setup changes this phase introduces: the solution and central package management, the `dev` command, the Dev Container, `compose.yaml`, and GitHub Pages (enabled at the exit gate).

## Work packages

| WP | Title | Size | Key deliverables |
| --- | --- | --- | --- |
| 1.0 | Risk spikes | S | PactNet 5 on .NET 10 with `UseKestrel()` provider smoke test; FusionCache backplane on Valkey 8.1/9.1; MTP plus Microsoft Code Coverage plus the Stryker preview runner; Testcontainers SQL Server 2022 on each supported host (Apple Silicon findings recorded); Vault-style file-rotated credentials with SqlClient pool clearing (local simulation) |
| 1.1 | Solution and build foundation | M | `global.json` (SDK `10.0.401`, `latestPatch`, MTP runner); `Directory.Build.props` (nullable, warnings as errors, deterministic CI builds, analyzers at `latest-recommended`, XML docs for public APIs); Central Package Management with lock files; `NuGet.config` with package source mapping and required repository signatures; `.slnx` with every project and reference; ArchUnitNET rules; `dotnet format` verification; nuget-license gate with a forbidden list; initial `ci.yml` (restore, build, unit and architecture tests, format, license) on PRs to `master` and `phase/**`; Dependabot for nuget, docker, devcontainers, and docker-compose; Conventional Commit PR-title check |
| 1.2 | Governance core | M | `Governance.Auditor` CLI with `status validate/render`, `trace`, `test-summary` (TRX → job summary), `github-sync` (dry-run diff first), `security` (core rules: secrets patterns, workflow permissions, action pinning, `secrets.*` usage, license policy), and `blast-radius` (path → layer and area → required jobs, docs, and reviews). Also `governance.yml` (always-run checks plus outputs for dynamic job selection), `STATUS.md` generation and drift check, and skill wrappers `/advanced-security-auditor` and `/blast-radius-evaluator`. From this merge on, both auditors are required on every PR. |
| 1.3 | Documentation toolchain | M | DocFX (modern template, API metadata, warnings as errors); `docs.yml` (markdownlint-cli2, lychee internal per PR and external nightly, cspell, mermaid-cli, include verification); `Documentation.Auditor` (inventory discovery and enforcement, including the license-note rule from ADR-0004; required sections by tier, front-matter freshness, illustrative-block policy); `/documentation-auditor` skill; PR docs preview artifact |
| 1.4 | Developer environment | L | Dev Container (pinned base digest, features lockfile, docker-in-docker); `tools/versions.yaml` and a verified installer (ADR: `mise` or custom); `dev.ps1` with Pester and PSScriptAnalyzer tests; `doctor` (versions, Docker resources, ports, architecture warnings); `.env.example`; `compose.yaml` (SQL Server 2022, Valkey with ACL user, Keycloak importing `catalog-realm.json`, `grafana/otel-lgtm`); first version of the realm files; host support matrix; the machine-migration runbook (`docs/runbooks/machine-migration.md`, extended for the platform in WP3.10); devcontainer CI smoke test (`devcontainers/ci` runs doctor, build, unit tests, and the quick-start script) |
| 1.5 | Contract v1 design | M | Authored OpenAPI 3.1 contract: operations, schemas, Problem Details types, error catalog, headers (`ETag`, `If-Match`, `Idempotency-Key`, `Retry-After`, pagination `Link`), security schemes and scopes, and examples for every response; contract linter (ADR); API style guide; error catalog page |
| 1.6 | Domain model | M | Aggregates, value objects, IDs, events, and invariants (§8.3); unit tests; CsCheck property tests (Money arithmetic and precision, SKU normalization round-trips, name bounds); mutation baseline report |
| 1.7 | Application layer | L | Handler interfaces; commands and queries; FluentValidation validators; authorization requirements; decorator pipeline (§8.2) with an ordering ADR; `Result` and error model; ports; unit tests for every decorator and handler |
| 1.8 | Persistence and outbox | L | EF Core mappings; repositories; `IUnitOfWork`; initial migration, bundle, and idempotent script; outbox interceptor and processor; idempotency store; `ICredentialSource` (static for Compose, rotating-file for Kubernetes) with a connection interceptor and pool clearing; Testcontainers suites using assembly fixtures; accepts the *domain events vs. orchestration* ADR |
| 1.9 | Caching | M | FusionCache over IMemoryCache, IDistributedCache (Valkey), and the backplane, behind `ICatalogCache`; key and tag strategy; consistency model doc; immediate plus outbox-guaranteed invalidation; two-host invalidation test; Valkey-down degradation test; cache metrics |
| 1.10 | HTTP API | L | Minimal API endpoints per the contract; Asp.Versioning; ETag, `If-Match`, and `If-None-Match`; Idempotency-Key; cursor pagination; Problem Details mapping; JWT bearer with policies; rate and request limits; security headers; forwarded-headers configuration (`KnownNetworks`); health endpoints on the management port; contract conformance test (oasdiff); Verify snapshots; authorization-matrix tests; Keycloak realm contract tests; dev-only Scalar |
| 1.11 | Observability | M | OpenTelemetry traces, metrics, and logs (§8.11); redaction; custom metrics; otel-lgtm dashboards provisioned for Compose; telemetry assertion tests using in-memory exporters |
| 1.12 | Auditor depth and AI skills | M | Security rules for Dockerfile, IaC, and manifest checks (ready for later phases); blast-radius rules for destructive-migration detection, contract-change detection, and docs and test-plan requirements; `/test-plan-evaluator`; fixtures and tests for every rule; policy files under `governance/policies` |
| 1.13 | Learning content and phase exit | M | Tool and concept pages for Phase 1; labs; Phase 1 test plan and traceability report; threat model update; GitHub Pages publishing from `master` (WIP banner); retrospective; phase PR; `v0.1.0` with git-cliff notes |

## Test plan

| Suite | What it proves | Gate |
| --- | --- | --- |
| Unit and property tests | Every domain invariant, event, validator, handler, and decorator | Blocking; coverage thresholds in [plan section 10.4](../implementation-plan.md) |
| Architecture tests | The dependency rules and naming rules | Blocking |
| Snapshot tests | The served OpenAPI document, Problem Details, and event and log shapes do not change by accident | Blocking |
| Infrastructure integration | EF mappings, migrations, outbox, idempotency, credential rotation, and cross-node cache invalidation against real SQL Server and Valkey | Blocking for relevant paths |
| API integration | HTTP semantics, the authorization matrix, ETags, idempotency, rate limits, health, and forwarded headers | Blocking |
| Identity contract | The real Keycloak realm file yields the expected claims and policies | Blocking when relevant |
| Contract conformance | The authored OpenAPI contract and the served document agree | Blocking |
| Mutation baseline | The strength of domain and application tests | Reported, not gated |
| Script tests | The `dev` command and `doctor` behave as documented | Blocking |
| Documentation checks | DocFX warnings, links, Mermaid, tool inventory, and sections | Blocking |
| Onboarding | A fresh Dev Container completes the quick start | Weekly and at release |

Negative and failure cases are part of the plan: denied authorization paths, stale and missing concurrency headers, a replayed and a conflicting idempotency key, Valkey down, the backplane down, a crash between commit and dispatch, and a rotated credential during use.

## Tool pages introduced

- **Build and SDK:** .NET SDK and `global.json`; Central Package Management and lock files; NuGet package source mapping and signatures; analyzers and `dotnet format`.
- **Testing:** xUnit v3 and Microsoft.Testing.Platform; Microsoft Code Coverage and ReportGenerator; ArchUnitNET; Verify; CsCheck; AwesomeAssertions; NSubstitute; Stryker.NET; Testcontainers.
- **Data and caching:** EF Core and migration bundles; SQL Server 2022 in containers; FluentValidation; FusionCache; Valkey.
- **Observability and identity:** OpenTelemetry .NET; `grafana/otel-lgtm`; Keycloak (dev mode and realm import).
- **API and contracts:** ASP.NET Core minimal APIs and OpenAPI 3.1; Asp.Versioning; Problem Details; the OpenAPI linter; oasdiff; Scalar.
- **Documentation:** DocFX; markdownlint, lychee, cspell, and mermaid-cli.
- **Developer environment:** Dev Containers and Codespaces; Docker Compose; PowerShell 7, Pester, and PSScriptAnalyzer; the tool installer.
- **Governance and CI:** nuget-license; gitleaks; Dependabot; GitHub Actions fundamentals; Conventional Commits and git-cliff.

## Concept pages introduced

- Clean Architecture boundaries
- Aggregates and invariants
- CQRS with decorators
- Transactional outbox
- Cache consistency
- HTTP correctness
- OIDC and JWT validation
- Observability signals
- Testing pyramid and determinism
- Requirement traceability

## Labs

1. Quick start: the API-only stack
2. Walk the boundaries: break an architecture rule and watch the tests fail
3. Create a product through CQRS and watch the outbox deliver
4. Prove L1/L2 caching and cross-node invalidation
5. Trigger 428 and 412 concurrency responses
6. Replay an idempotent request
7. Trace a request end to end
8. Add a destructive migration and watch the blast-radius evaluator flag it

## Evidence required

- Test reports and coverage summaries, with the traceability report showing every in-scope requirement covered.
- The devcontainer onboarding run and the native `doctor` smoke tests on Windows, Linux, and macOS.
- The DocFX build, the Pages URL, and the tool inventory audit.
- Spike reports from WP1.0 and the ADR updates they caused.
- The retrospective and the `v0.1.0` release notes.

## Risks and rollback

| Risk | Mitigation |
| --- | --- |
| Scope and complexity (R1) | Work packages, spikes first, strict Definition of Done |
| Apple Silicon contributors blocked by SQL Server (R3) | Support matrix, Codespaces, best-effort `api` path |
| PactNet dormancy (R10) | WP1.0 spike; the provider-verification harness is validated early even though Pact ships in Phase 4 |
| Cross-replica cache staleness (R11) | FusionCache backplane, staleness metrics, two-host tests |
| License contamination (R14) | `nuget-license` gate from WP1.1 |
| Documentation drift (R15) | Tested includes, freshness metadata, inventory discovery |

Rollback: revert the work-package pull request. During Phase 1 the local database is disposable (`dev compose reset`), and the initial migration may be edited until the phase exits.

## Exit gate

- Build and tests:
  - Locked restore and build complete with zero warnings.
  - Unit, architecture, snapshot, integration, identity-contract, and conformance suites pass.
  - Thresholds in §10.4 are met; mutation score is reported but not enforced.
- Representative flow:
  - A clean database migrates.
  - The representative flow proves commit, outbox delivery, cache fill, and invalidation across two hosts.
- Onboarding:
  - The devcontainer CI completes the quick start from a fresh environment.
  - The native Windows, Linux, and macOS doctor and build smoke tests pass. Hosted macOS and Windows runners can't run Linux containers, so Testcontainers runs on Linux only.
- Docs:
  - DocFX builds clean.
  - Every new tool passes the inventory and section audit.
  - Pages is live with a WIP banner.
- Governance:
  - Both auditors are required and green.
  - Status, Issues, the Milestone, and the retrospective are complete.
  - `v0.1.0` is tagged.
