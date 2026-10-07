---
title: "Research records"
description: "Dated, source-checked evidence behind the plan and the ADRs, with the confidence and limits of each record."
audience: [maintainers, learners]
last-verified: 2026-10-04
owner: "@RostomOhannessian"
---

# Research records

Decisions in this repository rest on facts that change quickly: plan limits, tool licenses, release lines, retirements, and security incidents.
Each fact is recorded here with its source and the date it was read, so a reader can judge how far to trust it and when to check it again.

## Records

| Record | Scope | Produced by | How it was checked |
| --- | --- | --- | --- |
| [Decision-critical addendum](2026-10-04-decision-critical-addendum.md) | Facts that changed or confirmed a decision, plus corrections to the other records | Maintainer session | Primary sources and the GitHub API, read directly |
| [GitHub and supply-chain verification](2026-10-04-github-supply-chain-verification.md) | GitHub plan limits, Actions security, attestations, Sigstore, CodeQL licensing, runner pricing | Research agent | Official docs, advisories, and live GitHub API data; some items marked unverified |
| [Kubernetes platform verification](2026-10-04-kubernetes-platform-verification.md) | Ingress, Gateway API, Kyverno, Argo CD, Kargo, Vault and OpenBao, Keycloak, SQL Server containers, resource budgets | Research agent, compacted | Official docs and release data; gaps listed per item |
| [.NET ecosystem verification](2026-10-04-dotnet-ecosystem-verification.md) | .NET 10, caching, testing, licensing, OpenAPI tooling, Pact, EF Core, OpenTelemetry, DocFX | Research agent | NuGet, GitHub, and Microsoft documentation; confidence stated per item |
| [External AI skill review](2026-10-04-external-skill-review.md) | Security, license, and fit review of twelve candidate AI skills | Research agent | Skill directories read at pinned commits |
| [Spike reports](spikes/README.md) | Time-boxed risk spikes: five for Phase 1 (WP1.0), each with pass criteria, results, and the decisions changed | Maintainer session | Built and run locally, with versions, image digests, and primary sources recorded |

## How to read a record

- The agent-produced records are preserved **verbatim** and are excluded from Markdown linting on purpose. They are evidence, not documentation.
- A confidence level and a source accompany each claim. Items the agent could not verify say so.
- Where the addendum disagrees with another record, the addendum wins, and it says why.
- Recommendations inside a record are inputs to decisions, not decisions. The decisions are in the [ADRs](../adr/README.md).

## When to add or refresh a record

- A work package starts on a tool whose facts are older than 90 days: re-verify versions, licenses, and lifecycle status, and add a dated record.
- A spike produces a result that changes a decision: write the spike report under `docs/research/spikes/` and update the ADR. The [spike index](spikes/README.md) lists the reports.
- A claim in a record turns out to be wrong: do not edit the record. Add a correction to the latest addendum and fix every document that relied on it.

Never put secrets, tokens, or personal data in a record.
