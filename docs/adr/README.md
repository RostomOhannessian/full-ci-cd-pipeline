---
title: "Architecture decision records"
description: "Index of every architecture, tool, and process decision, with its status and the work package that accepts it."
audience: [learners, contributors, maintainers]
last-verified: 2026-10-04
owner: "@RostomOhannessian"
---

# Architecture decision records

An architecture decision record (ADR) captures one significant decision: the problem, the options that were weighed, the choice, its consequences,
and the evidence. Reading the ADRs in order is the fastest way to understand why this repository looks the way it does.

The rules for writing them are in [ADR-0001](0001-record-architecture-decisions.md). The short version:

- **When to write one.** Any decision that changes the architecture, a trust boundary, a dependency policy, a tool, or a process.
- **Statuses.** `proposed`, `accepted`, `rejected`, `deprecated`, or `superseded`.
- **Process ADRs are accepted.** They record how the project is run and were decided with the plan on 2026-10-04.
- **Technology ADRs start as proposed.** Each carries its evidence and names the work package that validates it. That work package accepts the ADR,
  changes it, or replaces it, so a proposal is a hypothesis with a test, not a promise.
- **Accepted ADRs are not rewritten.** They can change status or gain links. A new direction is a new ADR that supersedes the old one.
- **Format.** Copy [the template](../templates/adr-template.md). The `adr-assistant` skill can scaffold one.

## Decisions

| ADR | Decision | Status | Accepted in |
| --- | --- | --- | --- |
| [0001](0001-record-architecture-decisions.md) | Record architecture decisions in repository ADR files | accepted | Accepted 2026-10-04 |
| [0002](0002-repository-visibility-and-capability-strategy.md) | Make the repository public after Phase 0 and design for public-repo capabilities | accepted | Accepted 2026-10-04 |
| [0003](0003-branch-and-work-package-protocol.md) | Use phase branches and work-package branches with master as the default | accepted | Accepted 2026-10-04 |
| [0004](0004-licensing-and-dependency-license-policy.md) | Adopt Apache-2.0 and enforce an open-source dependency license policy | accepted | Accepted 2026-10-04 |
| [0005](0005-documentation-system.md) | Treat documentation as code with DocFX, Diataxis, and audited tool inventory | accepted | Accepted 2026-10-04 |
| [0006](0006-commit-identity-and-privacy.md) | Use the GitHub noreply commit identity and limit privacy exposure | accepted | Accepted 2026-10-04 |
| [0007](0007-solo-maintainer-governance.md) | Govern the repository with rulesets, CODEOWNERS, and documented bypass | accepted | Accepted 2026-10-04 |
| [0008](0008-secrets-broker-vault-over-openbao.md) | Use Vault instead of OpenBao for the secrets broker | proposed | WP3.3 |
| [0009](0009-workload-identity-oidc-federation.md) | Use Vault-backed OIDC federation for workloads and automation | proposed | WP3.3 |
| [0010](0010-promotion-engine-kargo.md) | Use Kargo for promotion pull requests | proposed | WP4.5 |
| [0011](0011-gitops-vs-traditional-cicd.md) | Adopt pull-based GitOps with Argo CD | proposed | WP4.3 |
| [0012](0012-domain-events-outbox-vs-direct-orchestration.md) | Use a transactional outbox for domain events | proposed | WP1.8 |
| [0013](0013-shift-left-security-toolchain.md) | Adopt a layered shift-left security toolchain | proposed | WP2.5 |
| [0014](0014-iac-scanning-trivy-as-tfsec-successor.md) | Use Trivy for infrastructure-as-code scanning | proposed | WP3.1 |
| [0015](0015-gateway-api-with-envoy-gateway.md) | Use Gateway API with Envoy Gateway as the only entry point | proposed | WP3.2 |
| [0016](0016-cache-implementation-fusioncache.md) | Implement caching with FusionCache behind ICatalogCache | proposed | WP1.9 |
| [0017](0017-contract-first-api-design.md) | Design the API contract first and enforce conformance | proposed | WP1.5 |
| [0018](0018-test-stack-and-quality-gates.md) | Standardize on the Phase 1 test stack and quality gates | proposed | WP1.1 |
| [0019](0019-resource-profiles-and-host-support.md) | Define resource profiles and an explicit host support matrix | proposed | WP3.0 |

The evidence behind these decisions is in the [research records](../research/README.md). The plan that ties them together is
[the implementation plan](../plans/implementation-plan.md).
