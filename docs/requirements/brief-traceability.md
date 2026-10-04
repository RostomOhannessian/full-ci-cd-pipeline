---
title: "Brief traceability"
description: "Maps every outcome in the original brief and the follow-up requests to the requirements that capture it, the work packages that deliver it, and the evidence that proves it."
audience: [maintainers, contributors, reviewers]
last-verified: 2026-10-04
owner: "@RostomOhannessian"
---

# Brief traceability

These are the outcomes that must not change. Every requirement in the original brief, the documentation request, and the planning request maps to a delivery point and to evidence that can be checked. Removing or weakening a row needs the maintainer's explicit approval.

## How to read this page

- **Original requirement, Delivered in, and Evidence** are copied from [section 2.1 of the implementation plan](../plans/implementation-plan.md), which holds the authoritative wording.
- **Requirements** lists the IDs in [requirements.yaml](requirements.yaml) that capture the row. Each ID names the work packages that deliver it and the kinds of test or evidence that prove it. The [requirements schema](requirements.schema.json) defines the fields.
- A row is satisfied only when every requirement listed for it is `verified`. Until WP1.2 adds `governance trace`, this page is maintained by hand. From then on, the tool regenerates the traceability report and fails when an in-scope requirement has no test or evidence.

## Outcomes

| # | Original requirement | Delivered in | Evidence | Requirements |
| --- | --- | --- | --- | --- |
| 1 | Modular .NET Web API using Clean Architecture; strict interface segregation; DI everywhere | WP1.1, WP1.6–1.10 | Architecture tests; project graph doc; composition-root review | `REQ-ARC-001`, `REQ-ARC-003` |
| 2 | Domain layer (entities, value objects, domain events) | WP1.6 | Domain unit and property tests | `REQ-DOM-001`, `REQ-DOM-002`, `REQ-DOM-003`, `REQ-DOM-005` |
| 3 | Application layer (CQRS, validators, policies, event handlers) | WP1.7 | Application tests; decorator pipeline doc | `REQ-APP-001`, `REQ-APP-005` |
| 4 | Infrastructure (EF Core, repository, Unit of Work, migrations) | WP1.8 | Testcontainers integration and migration tests | `REQ-DATA-001`, `REQ-DATA-002`, `REQ-DATA-006` |
| 5 | Multi-level caching (IMemoryCache + distributed cache abstraction); caching service implementation | WP1.9 | Cross-replica invalidation and degradation tests | `REQ-CACHE-001`, `REQ-CACHE-003` |
| 6 | Cross-cutting concerns (logging, metrics, tracing) | WP1.11 | Telemetry assertion tests | `REQ-OBS-001`, `REQ-OBS-002`, `REQ-OBS-003` |
| 7 | Policy-based authorization | WP1.7, WP1.10 | Claims-to-policy and denied-path tests | `REQ-APP-005`, `REQ-IDN-002` |
| 8 | Example endpoint using CQRS, caching, and domain events | WP1.10, Labs 1-3 | End-to-end integration test and lab | `REQ-API-001`, `REQ-API-008` |
| 9 | Multi-stage Dockerfile with a minimal production footprint | WP2.1 | Image tests; size budget | `REQ-SUP-001` |
| 10 | `ci.yml` triggered on PRs to the default branch (`master`) | WP1.1 (initial), WP2.2 | Workflow runs | `REQ-CI-001` |
| 11 | `dotnet test` with Testcontainers | WP1.8, WP2.2 | CI test reports | `REQ-QUA-001` |
| 12 | CodeQL static analysis | WP2.3 | Code scanning results; ruleset gate | `REQ-SUP-002` |
| 13 | Trivy image scan; fail on critical vulnerabilities | WP2.4 | Failing-gate demonstration; SARIF | `REQ-SUP-004` |
| 14 | SBOM generation (Syft) | WP2.4 | SPDX and CycloneDX artifacts and attestations | `REQ-SUP-005` |
| 15 | SLSA provenance and Sigstore signing | WP2.5 | `gh attestation verify` and `cosign verify` output | `REQ-SUP-006` |
| 16 | Terraform: Kubernetes hosting environment | WP3.1, 3.2, 3.8 | Clean apply; smoke tests | `REQ-INF-001`, `REQ-INF-004` |
| 17 | MS SQL in a private tier with no public exposure | WP3.4, WP3.6 | Network probes; Kyverno denial tests | `REQ-INF-004`, `REQ-NET-003` |
| 18 | Network segmentation: public → private → data | WP3.2 | Chainsaw probe matrix | `REQ-NET-001`, `REQ-NET-002` |
| 19 | Workload identity (GitHub OIDC → secret broker; Kubernetes service account → secret broker) | WP3.3 | CI OIDC login test (positive and negative); Kubernetes auth tests | `REQ-IDN-006` |
| 20 | Least-privilege IAM roles | WP3.2–3.5 | Vault policy, RBAC, and SQL role tests | `REQ-SEC-007` |
| 21 | Secret-store integration; no secrets in GitHub Actions | WP3.3; WP1.2 auditor rule | Auditor evidence: no `secrets.*` beyond the platform token | `REQ-SEC-001` |
| 22 | OPA Gatekeeper or Kyverno runtime policies | WP3.6 | `kyverno test` and admission tests | `REQ-POL-001` |
| 23 | GitHub Action that runs tfsec | WP3.9 via tfsec's official successor, Trivy IaC (user-approved) | SARIF; mapping ADR | `REQ-INF-001` |
| 24 | GitHub Action that posts the Terraform plan diff as a formatted PR comment | WP3.9 | Sticky PR comment | `REQ-INF-006` |
| 25 | Argo CD Application manifest pointing to `/k8s` | WP4.3 | Argo CD sync status | `REQ-GIT-001` |
| 26 | Versioned API contract definitions | WP1.5 (v1 authored); WP4.1 (governance) | oasdiff conformance and breaking-change gates | `REQ-API-001`, `REQ-CON-001` |
| 27 | Pact consumer-driven contract tests for the primary endpoints | WP4.2 | Pact files; provider verification | `REQ-CON-005` |
| 28 | GitOps promotion pipeline (dev → staging → prod) | WP4.5 | Kargo Freight history; promotion PRs | `REQ-GIT-003` |
| 29 | Rollback strategy (automated and manual) | WP4.4, WP4.6 | Drill evidence | `REQ-GIT-005` |
| 30 | README with Mermaid architecture, Clean Architecture boundaries, and a diagram linking the API, MS SQL, CI, and GitOps | WP0.3 (seed), WP4.7 | Rendered README and docs site | `REQ-DOC-007` |
| 31 | ADRs: OIDC choice; shift-left tools; GitOps vs. traditional CI/CD; domain events vs. direct orchestration | WP0.4 (Proposed); accepted in WP1.8 (domain events), WP2.5 (shift-left), WP3.3 (OIDC), WP4.3 (GitOps) | `docs/adr/` | `REQ-GOV-005` |
| 32 | All PRs run `/advanced-security-auditor` and `/blast-radius-evaluator`, with integration stubs | WP1.2 (core), WP1.12 (depth) | Required status checks | `REQ-CI-002` |
| 33 | Every tool documented with an overview, decision rationale, setup tutorial, and external sources | All phases; enforced from WP1.3 | Documentation.Auditor report | `REQ-DOC-002`, `REQ-DOC-003`, `REQ-DOC-004` |
| 34 | Plan, phase plans, status, and progress in the repo; seamless continuation on another machine | Phase 0; WP1.2 | Fresh-clone resume test | `REQ-GOV-001`, `REQ-GOV-002`, `REQ-GOV-003` |
| 35 | Heavy on documentation, test plans, and automated tests | §10, §11, every phase | Traceability report | `REQ-CI-005`, `REQ-DOC-001`, `REQ-DOC-005`, `REQ-DOC-006`, `REQ-QUA-003` |
| 36 | Easy setup for intermediate and advanced coders | §12; WP1.4; WP3.0 | Onboarding CI; profile measurements | `REQ-CI-005`, `REQ-DX-001`, `REQ-DX-002`, `REQ-DX-004` |
| 37 | Useful AI skills | §13; WP0.5; WP1.12 | Skill fixtures; governance record | `REQ-AI-001`, `REQ-AI-002` |
| 38 | Public GitHub repo that attracts recruiters | Phase 0 exit (public, WIP); v1.0 launch gate | Public repo; docs site; signed release | `REQ-DOC-007`, `REQ-GOV-006` |

## Changing a row

1. Ask the maintainer, and record the answer in the pull request that changes the row.
2. Change section 2.1 of the plan, this page, and the affected requirements in one pull request.
3. Add the reason to the plan's revision history.
