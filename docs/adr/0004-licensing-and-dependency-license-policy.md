---
title: "ADR-0004: Adopt Apache-2.0 and enforce an open-source dependency license policy"
description: "License the repository under Apache-2.0 and allow only OSI-approved permissive or weak-copyleft linked dependencies, with documented tool-only exceptions."
status: accepted
date: 2026-10-04
decision-makers: ["@RostomOhannessian"]
supersedes: []
superseded-by: []
tools: ["nuget-license", "dependency-review", "vault", "terraform", "k6", "ms-code-coverage", "fluentassertions", "mediatr", "automapper", "masstransit"]
related-adrs: ["0002", "0008", "0018"]
evidence:
  - docs/research/2026-10-04-dotnet-ecosystem-verification.md
  - docs/research/2026-10-04-decision-critical-addendum.md
---

# ADR-0004: Adopt Apache-2.0 and enforce an open-source dependency license policy

## Context and problem statement

This repository is a public teaching and portfolio project. Its code, docs, and examples should be reusable by
learners and recruiters without license ambiguity, and later phases will publish artifacts, SBOMs, and evidence
bundles. Plan finding F15 showed that several familiar .NET libraries changed from open-source terms to commercial or
non-OSI licenses during 2025 and 2026, so "use whatever is common" is no longer safe.

Plan section 9 requires a license allowlist, vulnerability review, and documented exceptions for tools that are used
but not linked into distributed artifacts. Plan section 2 also frames the repository as a public portfolio, which makes
patent clarity and contribution terms more important than they would be for a private experiment.

The policy must distinguish between linked dependencies, which shape the terms of distributed code, and operational
tools, which may run in development or CI without being linked, redistributed, or offered as a service by this
repository.

## Decision drivers

- Keep the repository straightforward to study, fork, and reuse.
- Prefer an explicit patent grant for public code contributions.
- Prevent accidental adoption of newly commercial or non-OSI linked dependencies.
- Allow operational tooling that is acceptable only when it stays outside shipped binaries and services.
- Make compliance enforceable in CI, not dependent on memory.

## Considered options

1. Apache-2.0 with an enforced linked-dependency allowlist
2. MIT with a lighter dependency policy
3. Leave the repository unlicensed until launch
4. Dual-license the repository

## Decision outcome

Chosen option: **Apache-2.0 with an enforced linked-dependency allowlist**, because it best matches the repository's
public teaching role while giving contributors and downstream users an explicit patent grant.

The repository license is Apache-2.0. Contributions are accepted inbound under the repository license, consistent with
Apache-2.0 section 5, and the project does not use a separate CLA.

Linked dependencies must use OSI-approved permissive or weak-copyleft terms. The allowlist is enforced in CI, and the
policy explicitly forbids FluentAssertions 8 and later, MediatR 13 and later, AutoMapper 15 and later, MassTransit 9
and later, and any non-OSI license such as the current QuestPDF community license.

Tools that are used operationally but are not linked, redistributed, or offered as a service by this repository may
carry other terms, but each such case requires a documented note. On 2026-10-04, the GitHub license API for
`hashicorp/terraform` returned SPDX `NOASSERTION` and license name `Other`, and the same endpoint for `grafana/k6`
returned SPDX `AGPL-3.0` and license name `GNU Affero General Public License v3.0`. The decision-critical addendum
also records Vault as BSL 1.1, and the plan records Microsoft Code Coverage as closed source but free to use. Those
tools are acceptable here only under the tool-only rule.

The same rule covers Vault Agent and the Vault Secrets Operator (both BSL 1.1), and the Grafana, Loki, and Tempo
services (AGPL-3.0, confirmed through the same GitHub license endpoint on 2026-10-04). The repository runs them
unmodified in the local cluster and does not offer any of them as a hosted service. The tool inventory carries the
note for every such case, and a gate in the Documentation.Auditor fails the build when a tool with a non-permissive
license has none.

Enforcement happens at three levels. WP1.1 adds a `nuget-license` gate with a forbidden list. WP2.3 adds dependency
review on pull requests. The v1.0 launch generates third-party notices from the release SBOMs so the published
artifacts carry their final dependency evidence.

### Consequences

- Good: the repository is easy to reuse and contributes a clear patent position for public portfolio code.
- Good: the license policy prevents quiet drift into commercial or non-OSI linked dependencies.
- Bad: operational exceptions need ongoing review; the mitigation is a documented note per exception and CI checks on
  linked packages.
- Bad: some familiar libraries are intentionally off-limits; the mitigation is to document safe alternatives early and
  enforce the policy in tooling rather than in reviewer memory.
- Neutral: some tools remain acceptable only as build-time, CI-time, or local-only tooling and never as linked runtime
  dependencies.

### Confirmation

WP1.1 confirms this decision by failing the build when the solution adds a forbidden dependency license.

WP2.3 confirms it by running dependency review for pull requests and checking both vulnerabilities and license posture.

The v1.0 launch gate confirms it by generating SBOM-backed third-party notices for released artifacts and by reviewing
the exception notes for tool-only licenses.

## Pros and cons of the options

### Apache-2.0 with an enforced linked-dependency allowlist

- Good: explicit patent grant, clear reuse terms, and a policy that can be automated.
- Good: separates acceptable linked code from acceptable operational tooling.
- Bad: more policy detail to maintain than a bare top-level license file.

### MIT with a lighter dependency policy

- Good: familiar and simple.
- Bad: weaker patent language for a public engineering portfolio.
- Bad: a lighter policy does not address the concrete license drift found in the .NET ecosystem review.

### Leave the repository unlicensed until launch

- Good: defers a decision while the codebase is small.
- Bad: blocks clear reuse and undermines the public teaching goal from the start.
- Bad: makes contribution and dependency-policy decisions ambiguous during the exact phases when they matter most.

### Dual-license the repository

- Good: could preserve flexibility for future commercialization.
- Bad: unnecessary complexity for a solo-maintained teaching repository.
- Bad: complicates contribution terms and the portfolio story without solving the linked-dependency problem.

## Revisit when

- A proposed linked dependency arrives under non-OSI terms or changes license after adoption.
- The licenses or operational terms of Vault, Terraform, k6, Microsoft Code Coverage, Grafana, Loki, or Tempo change
  materially, or the repository modifies one of the AGPL services.
- The repository starts redistributing, embedding, or offering a currently tool-only component in a way that would make
  the exception rule invalid.

## Further reading

- Implementation plan v2: [../plans/implementation-plan.md](../plans/implementation-plan.md) (checked 2026-10-04)
- .NET ecosystem verification: [../research/2026-10-04-dotnet-ecosystem-verification.md](../research/2026-10-04-dotnet-ecosystem-verification.md) (checked 2026-10-04)
- Decision-critical addendum: [../research/2026-10-04-decision-critical-addendum.md](../research/2026-10-04-decision-critical-addendum.md) (checked 2026-10-04)
- Secrets broker ADR: [./0008-secrets-broker-vault-over-openbao.md](./0008-secrets-broker-vault-over-openbao.md) (checked 2026-10-04)
