---
title: "ADR-0005: Treat documentation as code with DocFX, Diataxis, and audited tool inventory"
description: "Build the documentation system as versioned, tested code using DocFX, a Diataxis structure, and repository-enforced quality gates."
status: accepted
date: 2026-10-04
decision-makers: ["@RostomOhannessian"]
supersedes: []
superseded-by: []
tools: ["docfx", "markdownlint-cli2", "lychee", "cspell", "mermaid-cli", "github-pages", "docker-compose"]
related-adrs: ["0001", "0017"]
evidence:
  - docs/research/2026-10-04-dotnet-ecosystem-verification.md
  - docs/research/2026-10-04-github-supply-chain-verification.md
---

# ADR-0005: Treat documentation as code with DocFX, Diataxis, and audited tool inventory

## Context and problem statement

Plan sections 2, 6, and 11 make documentation a first-class product. The repository must teach an intermediate or
advanced developer how the architecture and tooling work, preserve progress across machines, and publish portfolio
evidence. That requires more than a small README and scattered Markdown notes.

The documentation system also has to serve .NET API reference content, architecture explanation, labs, tutorials, tool
pages, status records, and ADRs. Plan gap G15 closes the risk that commands and examples drift away from reality by
requiring tested snippets pulled from scripts or source regions.

The chosen system must work the same in local development and in CI. It also has to support GitHub Pages once the
repository becomes public after Phase 0, and it has to fit the repository's teaching goal better than a generic static
site choice.

## Decision drivers

- Publish documentation from the repository with the same review and CI model as code.
- Generate .NET API reference from XML documentation without adding a second toolchain just for API docs.
- Organize content so tutorials, how-to material, reference, and explanation stay distinct.
- Detect drift in links, spelling, diagrams, front matter, and copied command snippets.
- Keep local and CI tooling identical through pinned containers.

## Considered options

1. DocFX with the modern template, Diataxis structure, and audited docs-as-code rules
2. Plain Markdown only, with no site generator
3. MkDocs Material
4. Docusaurus

## Decision outcome

Chosen option: **DocFX with the modern template, Diataxis structure, and audited docs-as-code rules**, because it
meets the teaching and API-reference needs without splitting the stack across multiple doc generators.

The documentation system is code. Pages live in the repository, build in CI, and publish to GitHub Pages from `master`
starting at the end of Phase 1 under a work-in-progress banner. The content follows the Diataxis structure:
tutorials, labs, reference, and explanation each have their own place, while ADRs, runbooks, testing, security, and
project-status content stay as separate sections.

DocFX uses the modern template. The .NET ecosystem verification record confirms that DocFX is community and
.NET-Foundation maintained, active as of 2026-10-04, and still the best fit for XML-doc-driven API reference in this
stack. The same record confirms native Mermaid support in the modern template, which matters for the architecture and
runbook diagrams this repository expects.

The repository keeps a tool inventory with tiers A, B, and C. Every tool page includes Overview, Decision rationale,
Setup tutorial, and Further research. Tier A pages add How this project uses it, Validation and troubleshooting,
Security and operations, and a linked lab. Every page carries front matter with ownership, prerequisites, tool IDs,
freshness metadata, and audience.

Quality gates are part of the decision. `markdownlint-cli2`, `lychee`, and `gitleaks` already run from digest-pinned
containers defined in `tools/lint/compose.yaml`, so local and CI execution are identical. `cspell`, `mermaid-cli`,
DocFX, and the Documentation.Auditor join them in WP1.3 under the same rule. The markdownlint configuration is explicit about its exceptions: MD013 is off for readability in source,
MD024 allows sibling headings, MD025 defers to the front matter title, MD029 uses ordered numbering, MD033 is limited
to an allow-list, and MD036 is off to avoid false emphasis warnings in structured docs.

Nightly jobs check external links. Internal link and anchor checks remain blocking on every pull request. Pages more
than 180 days past `last-verified` are flagged for review.

### Consequences

- Good: documentation quality is enforced early and stays coupled to the same workflows as code.
- Good: the chosen stack serves both narrative docs and .NET API reference without a second documentation platform.
- Bad: the docs pipeline adds several tools and conventions; the mitigation is pinned containers, shared commands, and
  an audited inventory that explains why each tool exists.
- Bad: front matter and tested-snippet rules add authoring discipline; the mitigation is templates, examples, and CI
  feedback that catches drift quickly.
- Neutral: external links are verified on a nightly cadence instead of blocking every pull request.

### Confirmation

WP0.6 confirms the docs-as-code baseline by wiring the Markdown style, link and anchor, and secret checks into CI.

WP1.3 confirms the publishing and audit model by adding DocFX, the spelling and diagram checks, the
Documentation.Auditor, the inventory audit, and the pull request preview. The Pages pipeline itself arrives at the Phase 1 exit (WP1.13).

Execution record (WP1.3, 2026-10-08): the `docs` workflow now runs Markdown style, relative links, spelling, Mermaid rendering, the DocFX build with
warnings as errors, the documentation auditor, and the full-history secret scan on every pull request, and an external link check every night. Each
check was shown to fail on a planted defect, and the audit found and fixed inventory entries that said "planned" for tools already in use, one missing
license note, and two dead source links. [ADR-0023](0023-documentation-auditor.md) records the auditor design. The decision does not change. The
DocFX metadata step accepts one warning, "No .NET API detected", until WP1.6 adds the first public type.

Later phases confirm the system continuously whenever tutorials, labs, runbooks, and tool pages are added under the
same rules.

## Pros and cons of the options

### DocFX with the modern template, Diataxis structure, and audited docs-as-code rules

- Good: strong fit for .NET API documentation plus narrative content, with native Mermaid support in the chosen
  template.
- Good: aligns with the repository's teaching and portfolio goals and keeps evidence in one pipeline.
- Bad: more moving parts than plain Markdown.

### Plain Markdown only, with no site generator

- Good: minimal setup and low cognitive overhead.
- Bad: no generated .NET API reference, weak navigation, and less structure for a large teaching repository.
- Bad: pushes too much responsibility to README-scale documents.

### MkDocs Material

- Good: pleasant documentation UX and strong community adoption.
- Bad: no native .NET XML-doc extraction, which would require another tool anyway.
- Bad: adds a Python-centered doc stack to a repository already centered on .NET and containers.

### Docusaurus

- Good: strong docs-site experience and ecosystem.
- Bad: like MkDocs, it does not solve .NET API extraction on its own.
- Bad: adds a JavaScript site pipeline without reducing overall complexity for this repository.

## Revisit when

- DocFX loses active maintenance or stops serving the target .NET versions well.
- Another documentation stack can generate equivalent .NET API reference and Mermaid-backed narrative docs with less
  complexity.
- The documentation checks become too slow or too noisy to keep blocking pull requests effectively.

## Further reading

- Implementation plan v2: [../plans/implementation-plan.md](../plans/implementation-plan.md) (checked 2026-10-04)
- .NET ecosystem verification: [../research/2026-10-04-dotnet-ecosystem-verification.md](../research/2026-10-04-dotnet-ecosystem-verification.md) (checked 2026-10-04)
- GitHub and supply-chain verification: [../research/2026-10-04-github-supply-chain-verification.md](../research/2026-10-04-github-supply-chain-verification.md) (checked 2026-10-04)
- Documentation index: [../index.md](../index.md) (checked 2026-10-04)
