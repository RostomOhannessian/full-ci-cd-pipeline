---
title: "ADR-0023: Audit the documentation with a deterministic .NET tool, and build the site with warnings as errors"
description: "Check the tool inventory, the tool pages, and every Markdown page with a first-party command line whose rules are data, and run DocFX, cspell, Mermaid CLI, and lychee from pinned versions, because a documentation check that a pull request controls must give the same answer everywhere."
status: accepted
date: 2026-10-08
decision-makers: ["@RostomOhannessian"]
supersedes: []
superseded-by: []
tools: [documentation-auditor, docfx, cspell, mermaid-cli, lychee, markdownlint-cli2, system-commandline, yamldotnet, github-actions, copilot-skills]
related-adrs: ["0004", "0005", "0020", "0021", "0022"]
evidence: []
---

# ADR-0023: Audit the documentation with a deterministic .NET tool, and build the site with warnings as errors

## Context and problem statement

[ADR-0005](0005-documentation-system.md) made documentation a product with gates, and WP0.6 wired the first three: Markdown style, internal
links, and a secret scan. Plan section 11 asks for more, and WP1.3 delivers it: a DocFX site with warnings as errors, spelling, diagram
checks, a nightly external link check, a tool inventory that is checked against what the repository uses, tool pages with the sections their
tier requires, front matter with a freshness date, and commands that come from tested files. `REQ-CI-005`, `REQ-DOC-002`, `REQ-DOC-003`,
`REQ-DOC-004`, `REQ-DOC-005`, and `REQ-AI-002` depend on it.

Facts that shape the design (checked 2026-10-08):

- No linter knows the tool inventory, the tiers, or the license note of [ADR-0004](0004-licensing-and-dependency-license-policy.md). A custom
  check is needed for them whichever tools run the rest.
- At the start of WP1.3 the inventory held 133 entries, and 17 of them were in use. None had a page, and five tools that had been in use
  since Phase 0 (markdownlint-cli2, lychee, gitleaks, Docker Compose, and Copilot custom instructions) were recorded as planned.
- DocFX 2.81.0 gives the warning "No .NET API detected" no code, and it fires while no project under `src` has a public type, which stays true until
  WP1.6. The `rules` setting in `docfx.json` can only override a warning by its code, so it cannot silence this one.
- The DocFX glob `**/*` does not match a hidden folder, a file that starts with a dot, or a file with no extension. A page that links to
  `.github/CODEOWNERS` or `LICENSE` fails the link check until the pattern names them.
- The pages can link to any file in the repository. DocFX accepts a link only to a file that is part of the build.
- A pull request controls every file the auditor reads, so the input is hostile, as it is for the governance tool in
  [ADR-0021](0021-governance-auditor-cli.md).

## Decision drivers

- A check gives one answer on every machine, and each rule can be shown to fail (`REQ-QUA-003`).
- Every rule is data that a reviewer can read, and an exception names the work package that ends it.
- The inventory cannot understate what the repository uses, and a tool cannot go undiscovered.
- A page that is owed is a dated debt that turns into a failure, not a silent gap.
- The documentation checks run from pinned versions, and local runs and CI use the same commands.
- Nothing weakens a gate to make the build pass.

## Considered options

1. A first-party .NET command line, `Documentation.Auditor`, separate from `Governance.Auditor`, with its rules in a policy file and a few source
   files shared by compilation (chosen).
2. Add the checks to `Governance.Auditor` as a `governance docs` command group.
3. PowerShell scripts with Pester tests.
4. Linters only: a Markdown style tool, a prose linter, and a link checker, with no inventory audit.
5. A shared class library that both auditors reference.

## Decision outcome

Chosen option: **a separate first-party .NET command line**, because it reuses what ADR-0021 proved, gives each auditor its own assembly and its own
tests, and keeps the inventory rules in a place that only documentation changes touch.

### The auditor

- **Commands.** `inventory`, `pages`, and `audit`, which runs both. Exit code 0 means every check passed, 1 means a check failed, and 2 means the
  tool could not run. A nonzero code always means "do not merge".
- **Rules are data.** `governance/policies/documentation-policy.yaml` holds the required sections of each tier, the front matter keys of each kind
  of page, the permissive license list, the pages where a command must be tested, the roots that an include may read, the unsupported sources,
  and the owed pages. The inventory has a schema, `docs/reference/tools/inventory.schema.json`, and the tool refuses a schema that uses a keyword
  it does not implement.
- **Shared source, not a shared assembly.** The tool compiles eight files from `Governance.Auditor`: the finding record, the glob matcher, the exception type, the Markdown text
  helpers, the repository file reader, the YAML reader, the document and JSON helpers, and the schema validator. Neither assembly references the other, a change to a shared file
  shows in both builds at once, and the tests of those files stay in the governance test project.
- **Hostile input.** The same rules as ADR-0021 apply. The tool refuses XML with a DTD and YAML with anchors, gives every regular expression a timeout, keeps
  every path inside the repository root, and reports a file it cannot read instead of skipping it. It reads Compose files by line, because a Compose
  file may share a service with an anchor, and no finding prints more than a name, a path, and a line.

### The inventory and discovery

- **`detect` rules.** An inventory entry may carry rules of the form `kind:pattern`. The kinds are `nuget`, `dotnet-tool`, `action`, `image`, and
  `file`. The scanners read `Directory.Packages.props` and project files, `.config/dotnet-tools.json`, workflow `uses:` lines and containers, Compose
  files, and Dockerfiles. A used tool that no entry detects fails, an entry that disagrees with the files fails, and an in-use entry whose rules match
  nothing fails, so the inventory is checked in both directions.
- **Versions are compared.** A pin and the recorded version must agree on their numbers, so `v2.81.0`, `2.81.0`, and `lychee-v0.24.2` match.
- **Unsupported sources fail.** Terraform, Helm charts, Kubernetes manifests, Kyverno policies, the Dev Container, and `tools/versions.yaml` do not exist yet.
  Each is listed in the policy with the work package that adds it, and the audit fails as soon as one exists without a scanner, so no tool can hide in a format
  that the auditor cannot read.
- **The license note.** An entry whose license is not on the permissive list needs a `notes` value that says why it is acceptable. This is the gate that ADR-0004
  promised.
- **Pages are owed, and the debt expires.** Eighteen tools were in use before the auditor existed, and their pages belong to WP1.13, which writes the Phase 1 tool pages.
  The policy lists them with that work package. Each is a note until WP1.13 is completed in `status.yaml`, and then each is an error. A page that
  exists must leave the list, so the list cannot go stale.

### The pages

- **Front matter by kind.** The first matching rule decides which keys a page needs: an ADR, a dated research record, a tool page, or any other page under `docs`.
  A list that a decision may leave empty, such as `supersedes`, must still be declared.
- **Freshness.** A page more than 180 days past `last-verified` is a warning, as plan section 11.3 says. The date is an input, so a test fixes it.
- **Tested commands.** In tutorials, labs, reference pages, explanations, and runbooks, a block in a shell language is a command, and so is a `text` block whose first line
  starts with a command name from the policy. It must come from a file through a DocFX include, or carry the word `illustrative` after the language. Records
  such as ADRs and plans are exempt.
- **Includes.** A code include must name a file inside the repository, under a root that CI builds or runs, and the region or line range it names must exist.
- **Diagrams.** A Mermaid diagram needs `accTitle` and `accDescr`. Mermaid CLI proves that it renders, but it accepts a diagram with neither.

### The pipeline

- **`docs.yml`.** The WP0.6 workflow is renamed from `docs-quality.yml`, as plan section 7 names it, and keeps its three required jobs under the same names, so the
  rulesets are unaffected. It adds Spelling, Diagrams, Documentation site, and Documentation auditor on every pull request, and External links on a nightly schedule. The nightly check
  reports and blocks nothing, because a third-party site that is down must not stop an unrelated change.
- **Pinned containers.** cspell and Mermaid CLI run from images pinned by digest in `tools/lint/compose.yaml`, like the other lint tools. DocFX is a local tool pinned in `.config/dotnet-tools.json`.
- **DocFX.** The configuration uses the `modern` template. It maps the root Markdown files and every other repository file to `/repo/`, so a link from a page to a workflow or a source file
  resolves. The build runs with `--warningsAsErrors`. The metadata step accepts exactly one warning, "No .NET API detected", through `tools/ci/Invoke-DocFx.ps1`, and a Pester test names it so that
  another exception needs a reviewed change. Remove the exception in WP1.6.
- **Preview.** The site is uploaded as the `docs-preview` artifact for seven days. It is not published. GitHub Pages arrives at the Phase 1 exit (WP1.13).
- **Skill.** `/documentation-auditor` runs the command and explains the findings. It holds no write scope, and the tests run it over a fixture.

### Consequences

- Good: the inventory and the pages are checked by one engine, with the same tests, the same policy file, and the same exit codes as the governance checks.
- Good: tools in use cannot be missing from the inventory, and the inventory cannot say "planned" for a tool that runs. Fixing the data found five such cases, one missing license note,
  and two broken source links.
- Good: a tutorial command is either a command that CI runs or says that it is illustrative.
- Bad: the tool is built from the pull request's own source, so a change to it is audited by the changed tool. Mitigation: the blast-radius map flags it for a close human read, and the
  tests show each rule failing.
- Bad: the eight shared files couple the two tools. Mitigation: the coupling is in the build, so a break is immediate, and the revisit trigger below names the way out.
- Bad: the owed-pages list is 18 notes in every report until WP1.13. Mitigation: each is a note, the list names its work package, and it becomes an error when that work package is completed.
- Bad: refusing a Compose anchor in the YAML reader but reading Compose by line is a special case. Mitigation: it is one scanner, and the test names the reason.
- Neutral: adding `Spelling`, `Diagrams`, `Documentation site`, and `Documentation auditor` as required checks in the rulesets is a settings change. The maintainer has not approved it,
  so these jobs report on every pull request and do not block it yet.

### Confirmation

- **WP1.3** delivers the tool, the policy file, the schema, the workflow, the configuration, the skill, and the tests. The tests run every rule against a conforming repository and a violating
  one, and the self-check tests run the audit over this repository, so a repository that breaks a rule fails the unit tests as well as the workflow.
- **WP1.6** removes the DocFX metadata exception, when the first public type exists.
- **WP1.13** writes the owed pages and empties the list.
- **WP1.4, WP3.1, WP3.6, and WP3.8** add the scanners for the formats that each introduces, and remove the matching unsupported source.
- **WP1.12** reviews the auditor depth, and **WP2.2** consumes the job selection that the blast-radius map describes.

## Pros and cons of the options

### A separate first-party .NET command line

- Good: one engine, one policy file, and its own tests, and each auditor can be reviewed on its own.
- Good: the code that reads files safely is shared by compilation, so a fix lands in both tools.
- Bad: the project maintains it, and it is audited by its own changes.

### A `governance docs` command group

- Good: no second assembly, and no shared source.
- Bad: ADR-0021 already names the tool's growth as a reason to split it, and the documentation rules change for different reasons than the status, security, and
  traceability rules do. A reviewer of the governance tool would also read the documentation rules.

### PowerShell scripts with Pester tests

- Good: no build step.
- Bad: the reasons of ADR-0021 apply. Reading YAML, XML, and schemas safely in PowerShell needs modules that the lock files and the license gate do not cover.

### Linters only

- Good: nothing to maintain.
- Bad: nothing checks the inventory against the files, the tiers, the license note, or the owed pages, which are the parts of plan section 11 that make the documentation trustworthy.

### A shared class library

- Good: no coupling by compilation, and one place for the common code.
- Bad: moving the code changes about 35 files and their tests in a tool that audits pull requests, which is a larger review than this work package should ask for.

## Revisit when

- The shared files change more often than the documentation rules, which would argue for the shared library.
- The DocFX metadata warning gains a code, or the first public type exists, which removes the exception.
- A scanner for a new format needs more than a line reader and a parser, which would argue for a plugin shape.
- DocFX, cspell, or Mermaid CLI changes its license or stops being maintained.
- The documentation jobs become required checks, which turns a report into a gate.

## Further reading

- [ADR-0005: Treat documentation as code](0005-documentation-system.md) for the decision that this record carries out
- [ADR-0021: Build the governance auditors as one deterministic .NET command line](0021-governance-auditor-cli.md) for the design that this tool follows
- [DocFX configuration reference](https://dotnet.github.io/docfx/reference/docfx-json-reference.html) (checked 2026-10-08)
- [DocFX code snippets and includes in Markdown](https://dotnet.github.io/docfx/docs/markdown.html) (checked 2026-10-08)
- [Mermaid accessibility](https://mermaid.js.org/config/accessibility.html) (checked 2026-10-08)
