---
title: "Explanation"
description: "What the repository's concept pages will explain, and which architecture and governance concepts you can already study."
audience: [learners, contributors, maintainers]
type: reference
last-verified: 2026-10-04
owner: "@RostomOhannessian"
---

# Explanation

Explanation pages answer "why does this work this way?" They connect decisions, tradeoffs, and mental models, instead of giving a checklist or a command reference.

## Planned concept pages

| Concept page | Work package | Status |
| --- | --- | --- |
| Clean Architecture boundaries | WP1.13 | planned |
| Aggregates and invariants | WP1.13 | planned |
| CQRS with decorators | WP1.13 | planned |
| Domain events and the outbox | WP1.13 | planned |
| Caching consistency | WP1.13 | planned |
| HTTP correctness | WP1.13 | planned |
| OIDC and JWT validation | WP1.13 | planned |
| Observability signals | WP1.13 | planned |
| Testing pyramid and determinism | WP1.13 | planned |
| Requirement traceability | WP1.13 | planned |
| Secure software supply chain and SLSA | WP2.7 | planned |
| CI threat model and workflow trust boundaries | WP2.7 | planned |
| Zero trust on Kubernetes | WP3.10 | planned |
| Network segmentation and trust boundaries | WP3.10 | planned |
| Workload identity and dynamic secrets | WP3.10 | planned |
| Service-level objectives and canary analysis | WP4.4, WP4.7 | planned |
| GitOps and promotion | WP4.7 | planned |
| Rollback engineering | WP4.6, WP4.7 | planned |

## Concepts you can read today

- [ADR-0001](../adr/0001-record-architecture-decisions.md): why this repository records major decisions in small, durable ADR files.
- [ADR-0002](../adr/0002-repository-visibility-and-capability-strategy.md): why the repository is public and how capability-aware automation follows from that choice.
- [ADR-0003](../adr/0003-branch-and-work-package-protocol.md): how phase branches and work-package branches keep delivery small and reviewable.
- [ADR-0004](../adr/0004-licensing-and-dependency-license-policy.md): how license policy and dependency review shape tool adoption.
- [ADR-0005](../adr/0005-documentation-system.md): why the docs use Diataxis, DocFX, and an audited tool inventory.
- [ADR-0006](../adr/0006-commit-identity-and-privacy.md): how the project limits privacy exposure in commits and history.
- [ADR-0007](../adr/0007-solo-maintainer-governance.md): how one maintainer still uses rulesets, CODEOWNERS, and documented bypasses responsibly.
- [Threat model](../security/threat-model.md): the planned trust boundaries, assets, actors, and controls across the whole system.
- [Testing strategy](../testing/strategy.md): the test suites, determinism rules, traceability model, and quality thresholds.
- [GitHub supply-chain verification](../research/2026-10-04-github-supply-chain-verification.md): current facts about GitHub capabilities, hosted runners, attestations, and signing.
- [Kubernetes platform verification](../research/2026-10-04-kubernetes-platform-verification.md): the local-cluster constraints, footprint, and platform viability checks behind Phase 3.
- [.NET ecosystem verification](../research/2026-10-04-dotnet-ecosystem-verification.md): the library, runtime, caching, testing, and tool facts that shape Phase 1.
- [Decision-critical addendum](../research/2026-10-04-decision-critical-addendum.md): follow-up research that settles edge cases around Vault, Kargo, Keycloak, and related choices.
- [External AI skill review](../research/2026-10-04-external-skill-review.md): why external skills are reviewed before installation and why none are installed in Phase 0.
