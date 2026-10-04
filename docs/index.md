---
title: "Documentation"
description: "Start here: how the documentation is organized, where to begin, and what exists today."
audience: [learners, contributors, maintainers]
last-verified: 2026-10-04
owner: "@RostomOhannessian"
---

# Documentation

This repository teaches by building: a product catalog API, delivered through a secure supply chain onto a zero-trust Kubernetes platform, with
every decision, test, and tool documented. The documentation assumes you know the syntax of C#, YAML, HCL, PowerShell, and shell. It does not
assume you know the tools, patterns, or security model, and it explains each one.

## What exists today

The project is in **Phase 0**. The plan, decisions, evidence, and governance exist. The application, pipelines, and platform arrive in Phases 1 to 4.
[The status page](project/STATUS.md) always says what is done and what comes next.

## How the documentation is organized

The structure follows [Diataxis](https://diataxis.fr/): each kind of page answers one kind of need.

| You want to | Go to | Status |
| --- | --- | --- |
| Learn by doing, step by step | [Tutorials](tutorials/index.md) and [Labs](labs/index.md) | Planned, from Phase 1 |
| Understand why something works the way it does | [Explanation](explanation/index.md) and the [ADRs](adr/README.md) | ADRs available now |
| Look up a fact, a tool, a setting, or a command | [Tool reference](reference/tools/index.md), [Glossary](glossary.md) | Inventory available now; pages arrive with each tool |
| Operate or recover something | [Runbooks](runbooks/index.md) | Planned, from Phase 1 |
| See how the project is planned and how far it has come | [Implementation plan](plans/implementation-plan.md), [phase plans](plans/phases/phase-0-foundation.md), [status](project/STATUS.md) | Available now |
| Check what we know and how we know it | [Research records](research/README.md) | Available now |
| Judge the security design | [Threat model](security/threat-model.md) | First version available now |
| Judge the testing approach | [Testing strategy](testing/strategy.md) | Available now |
| Trace a requirement to its delivery and evidence | [Brief traceability](requirements/brief-traceability.md), [risk register](project/risk-register.md) | Available now |
| See how AI assistants are configured and governed | [AI skills and instructions](governance/ai-skills.md) | Available now |

## Suggested reading order

1. The [README](../README.md) for the premise and the architecture sketch.
2. The [implementation plan](plans/implementation-plan.md): sections 1 to 3 give the decisions, and section 8 gives the architecture.
3. The [ADRs](adr/README.md): short, each with its evidence.
4. The [threat model](security/threat-model.md) and the [testing strategy](testing/strategy.md).

## How every page is written

- Every page starts with front matter: title, description, audience, a `last-verified` date, and an owner.
  Pages that are more than 180 days past their date are flagged for review.
- Every tool has a page with an overview, the decision rationale, a setup tutorial, and curated external sources.
- Commands in tutorials come from scripts that run in CI. A command that was never run does not appear in a tutorial.
- Diagrams are Mermaid, tied to real file paths, and validated.

## Contributing to the documentation

Read [CONTRIBUTING.md](../CONTRIBUTING.md). Run the checks locally with the commands it lists. Start a new page from a template in
[docs/templates](templates/tool-page-template.md).
