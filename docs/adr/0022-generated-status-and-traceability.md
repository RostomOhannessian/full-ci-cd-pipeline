---
title: "ADR-0022: Generate the status page and the traceability report, and prove requirements with tests or an evidence register"
description: "Render STATUS.md and the traceability report from data and fail CI when they drift or when a requirement that is due has no proof, because a hand-kept status page and an unchecked trace both go stale."
status: accepted
date: 2026-10-08
decision-makers: ["@RostomOhannessian"]
supersedes: []
superseded-by: []
tools: [governance-auditor, yamldotnet, xunit-v3, pester]
related-adrs: ["0018", "0021"]
evidence: []
---

# ADR-0022: Generate the status page and the traceability report, and prove requirements with tests or an evidence register

## Context and problem statement

Phase 0 kept `docs/project/STATUS.md` by hand, next to `docs/project/status.yaml`, and said that WP1.2 would generate it. Every work
package since then edited both files, and a reviewer had to compare them by eye. Plan section 10.2 adds a second generated page,
`docs/testing/traceability.md`, which maps requirements to tests and evidence and fails when an in-scope requirement has neither.
`REQ-GOV-002`, `REQ-GOV-003`, and `REQ-QUA-003` depend on both.

Two questions need an answer. What does a requirement need to count as proven? And when is a requirement in scope, so that a missing
proof is a failure and not a to-do?

Facts about the repository (checked 2026-10-08): the requirements register lists 121 requirements, each with the work packages that
deliver it. Most of them are delivered across several work packages, in several phases. A check that demanded proof for a requirement
as soon as its first work package merged would fail for work that is not meant to exist yet.

## Decision drivers

- A generated page is never out of date, and a reviewer sees the generated change in the same pull request.
- Resuming on another machine reads one page, and that page is correct by construction (`REQ-GOV-003`).
- A rule that cannot fail is not a rule, so "no proof" must fail the build.
- A requirement is proven by something a reader can open: a test that carries its ID, or a file that records the evidence.
- The check does not demand proof for work that is planned and not yet built.

## Considered options

1. Render both pages from data with a committed output and a drift check, define a requirement as in scope when every work package that
   delivers it is completed, and accept two kinds of proof: a tagged test and an evidence register entry (chosen).
2. Keep `STATUS.md` by hand and check only that `status.yaml` is valid.
3. Render the pages in CI and publish them as artifacts without committing them.
4. Treat a requirement as in scope as soon as any work package that delivers it is completed.

## Decision outcome

Chosen option: **render and commit both pages, with drift checks and an explicit scope rule**, because it keeps the pages readable in
the repository and on GitHub, makes every status change a visible diff, and still fails on a missing proof.

The rules:

- **`STATUS.md` is generated** by `governance status render` from `status.yaml` alone, with the fixed wording in an embedded template
  (`tools/Governance.Auditor/Status/StatusPage.template`). Nobody edits it by hand. `governance status render --check` fails when the file
  differs, and the `governance` workflow runs it on every pull request. The schema stays at version 1, so no data file changes.
  The page keeps the **Resume here** section, a phases table, and a work-package table for each phase that has started.
- **`status validate` checks what a schema cannot:** work-package references, dependency order and cycles, a consistent current phase,
  work package, and branch, a completed work package with evidence that exists, and a blocked one with a blocker.
- **A requirement is in scope** when every work package that delivers it is completed, or when its status is `verified`. A deferred or
  withdrawn requirement is never in scope. `--phase N` puts every requirement that phase N and the earlier phases deliver in scope, for
  a phase exit gate. `--all` puts every active requirement in scope, to list what is still missing.
- **A requirement is proven** by a .NET test with `[Trait("Requirement", "REQ-...")]`, by a comment line `# requirements: REQ-...` in a
  Pester, Chainsaw, Kyverno, Terraform, or k6 file, or by an entry in `docs/testing/evidence.yaml`. An evidence entry names a file that
  must exist. A manual drill records its transcript in a file under `docs/testing/evidence/` and the register points to it.
- **`trace` fails** for an in-scope requirement with no proof, for a test or an entry that names an unknown requirement, for a work
  package the register names but the status file lacks, and for evidence whose file is missing. It warns when a requirement is proven,
  fully delivered, and still `planned`.
- **`docs/testing/traceability.md` is generated and committed**, and `trace --check` fails when it differs.

### Consequences

- Good: a reader opens one page to resume, and the page cannot disagree with `status.yaml`.
- Good: proof is concrete. A reviewer clicks from a requirement to the test file or the evidence file.
- Good: partial delivery is honest. A requirement whose work is not finished shows as not yet due, and shows as covered early when its
  tests already exist.
- Bad: the pages change whenever the data changes, so a pull request that edits `status.yaml` must also render the page. Mitigation:
  the `session-handoff` skill and `AGENTS.md` list the command, and CI fails with a message that names it.
- Bad: a requirement delivered by many work packages is not checked until the last one completes. Mitigation: the tests still tag their
  IDs as they are written, the report shows that proof as early cover, and the phase exit gate runs `trace --phase N`.
- Bad: the template is part of the tool, so changing the fixed wording is a code change. Mitigation: it is a Markdown file with
  placeholders, a test renders a reviewed example, and a change to the wording shows in that example.
- Neutral: the evidence register is a second file to keep. It holds only proof that is not a test, and a requirement with tests needs
  no entry.

### Confirmation

- **WP1.2** delivers the commands and tests: the renderer against a reviewed example, each validator rule, the scope rule, each proof
  source, and the drift checks. Tests also run both commands over this repository.
- **WP1.13** runs `trace --phase 1` at the Phase 1 exit gate and publishes the report.
- **WP1.12** adds `/test-plan-evaluator`, which reads `trace --all` to propose cases for what is missing.

## Pros and cons of the options

### Render both pages with committed output and a drift check

- Good: readable on GitHub, reviewable as a diff, and checked by CI.
- Bad: regenerating after every data change.

### Keep `STATUS.md` by hand

- Good: no tool.
- Bad: it is what Phase 0 did, and the page and the data had to be compared by eye in each pull request.

### Render in CI without committing

- Good: no generated diffs in pull requests.
- Bad: a person on another machine, or an agent without CI, has no readable page, which defeats the resume protocol.

### In scope as soon as any delivering work package is completed

- Good: it forces proof earlier.
- Bad: of the 121 requirements, 34 had a completed work package on 2026-10-08 (counting WP1.2), and 29 of them had no tagged test, many
  for work that is meant to arrive in later work packages. The check would fail on plan, and the answer would be to add evidence that
  says nothing.

## Revisit when

- The requirements register moves to per-work-package acceptance, so that a partly delivered requirement can be checked in parts.
- The Documentation.Auditor (WP1.3) takes over page generation for other documents, which could share the template mechanism.
- The traceability page grows past what a reader can scan, which would argue for one page per area.

## Further reading

- [The testing strategy](../testing/strategy.md) for how tests declare requirement IDs.
- [ADR-0021](0021-governance-auditor-cli.md) for the tool that renders the pages.
