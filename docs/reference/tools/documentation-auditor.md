---
title: "Documentation.Auditor"
description: "The repository's own command-line tool that checks the tool inventory, the tool pages, and every Markdown page, and fails the build when one of them is wrong."
audience: [learners, contributors]
tier: B
tools: [documentation-auditor, docfx, mermaid-cli]
introduced: "phase-1"
prerequisites: ["The .NET SDK 10.0.401"]
estimated-time: "20 minutes"
last-verified: 2026-10-08
verified-against: { documentation-auditor: "n/a" }
owner: "@RostomOhannessian"
---

# Documentation.Auditor

## Overview

The Documentation.Auditor is a small .NET command line. Its project file is [Documentation.Auditor.csproj](../../../tools/Documentation.Auditor/Documentation.Auditor.csproj).
It reads the repository's
files and applies the documentation rules of [plan section 11](../../plans/implementation-plan.md) as code. A person, CI, and the
[documentation-auditor skill](../../../.github/skills/documentation-auditor/SKILL.md) run the same command and get the same answer, because a
check that decides whether a pull request may merge must not depend on who runs it.

It has three commands, and `audit` runs the other two.

| Command | What it checks |
| --- | --- |
| `inventory` | The inventory file against its schema, the license note that ADR-0004 asks for, ADR links, and freshness. The tools the repository uses against the inventory. The tool pages and the dependency catalog |
| `pages` | Every Markdown page for front matter, freshness, tested commands, resolvable includes, and accessible diagrams |
| `audit` | Both of the above |

The rules are data. [governance/policies/documentation-policy.yaml](../../../governance/policies/documentation-policy.yaml) holds the required sections, the
front matter keys, the permissive license list, and the exceptions, so a change to a rule is a reviewed change to that file.

## Decision rationale

[ADR-0023](../../adr/0023-documentation-auditor.md) records the decision. A pull request controls the files this tool reads, so the tool must be
deterministic, refuse hostile input, and show that each rule can fail. Linters can check a page, but none of them knows the inventory, the tiers,
or the license note, and a custom step was needed for those anyway.

- Built on the same libraries as [Governance.Auditor](../../adr/0021-governance-auditor-cli.md): System.CommandLine and YamlDotNet, both MIT, and
  a few source files that both tools compile.
- The trade-off is that the project maintains it, and that a pull request that changes it is audited by the changed tool. The blast-radius
  map flags such a change for a close human read.

## Setup tutorial

**Prerequisites:** the .NET SDK that `global.json` pins.

1. Run the audit from the repository root. CI builds the tool and runs this step, which is a workflow step, so the lines below are YAML.

   [!code-bash[Audit the inventory, the tool pages, and every page](../../../.github/workflows/docs.yml#audit)]

   Locally, run it through the project instead.

   ```bash illustrative
   dotnet run --project tools/Documentation.Auditor -- audit
   ```

   Expected output ends with a summary line. Notes are not failures: each one is a page that a later work package owes.

   ```text
   documentation audit: passed (0 warnings, 18 notes).
   ```

2. Read a finding. Each line names the severity, the rule, the file and line, and the fix.

   ```text
   error [command-block-untested] docs/tutorials/start.md:14: A hand-written command block must come from a tested file through an include, or be marked 'illustrative' after the language.
   ```

3. Fix it, then run the audit again. The table below lists the findings that people meet most often.

| Rule | Cause | Fix |
| --- | --- | --- |
| `tool-undocumented` | A package, action, image, or tool is used and no inventory entry detects it | Add an entry to the inventory with a `detect` rule, with the tool-doc-author skill |
| `tool-usage-state` | A tool is used and the inventory says `planned` | Set `usage` to `in-use`, and write its page |
| `tool-version-drift` | A pin and the inventory disagree | Update the entry and its sources in the same change as the pin |
| `tool-page-missing` | A Tier A or B tool is in use and has no page | Write the page from the template |
| `tool-page-section` | A page lacks a section its tier requires | Add the section, or say what is missing and which work package adds it |
| `inventory-license-note` | A non-permissive license has no `notes` entry | Say why the tool is acceptable, as ADR-0004 asks |
| `command-block-untested` | A shell command block is neither included from a tested file nor marked | Include it, or add `illustrative` after the language |
| `include-region-missing` | An include names a region the file does not have | Add `# <name>` and `# </name>` comments around the lines |
| `diagram-accessibility` | A Mermaid diagram has no `accTitle` or `accDescr` | Add both lines under the diagram type |
| `front-matter-key` | A page lacks a key its kind needs | Add it. The kinds and keys are in the policy file |

**Cleanup:** nothing is left behind. The audit only reads files, and it writes a report only when you pass `--summary`.

## Further research

- [ADR-0023](../../adr/0023-documentation-auditor.md): the design, the alternatives, and when to revisit it
- [Plan section 11](../../plans/implementation-plan.md): the inventory, the tiers, and the docs-as-code rules that the tool enforces
- [The tool reference](index.md): how the inventory, the tiers, and the pages fit together
