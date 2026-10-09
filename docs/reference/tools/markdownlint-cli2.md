---
title: "markdownlint-cli2"
description: "The Markdown linter that enforces the repository's house style and heading structure on every pull request."
audience: [learners, contributors]
tier: B
tools: [markdownlint-cli2]
introduced: "phase-0"
prerequisites: ["Docker"]
estimated-time: "10 minutes"
last-verified: 2026-10-08
verified-against: { markdownlint-cli2: "0.23.3" }
owner: "@RostomOhannessian"
---

# markdownlint-cli2

## Overview

markdownlint-cli2 applies a set of numbered style rules to Markdown files and reports each violation with its file, line, and rule ID. The rules
catch the mistakes that make a page hard to read or render: a skipped heading level, a code fence with no language, a list with uneven
indentation, or a missing blank line.

The rules are in [.markdownlint-cli2.jsonc](../../../.markdownlint-cli2.jsonc), which starts from the default rule set and records each deviation
with a comment. The configuration turns off or relaxes six rules (`MD013`, `MD024`, `MD025`, `MD029`, `MD033`, and `MD036`), because long
lines are normal in tables, sibling headings repeat, the page title lives in the front matter, steps count upward, a few HTML elements have
no Markdown form, and bold lead-ins are used on purpose. [ADR-0005](../../adr/0005-documentation-system.md) records why.

The `Markdown style` job in [docs.yml](../../../.github/workflows/docs.yml) runs it on every pull request, and the ruleset requires that job.

## Decision rationale

[ADR-0005](../../adr/0005-documentation-system.md) names markdownlint-cli2 as the style gate. No other Markdown linter was evaluated.

- It is the command-line wrapper that the markdownlint author maintains, so the rule IDs and the documentation are the ones the community uses.
- It runs from a container image pinned by digest in [tools/lint/compose.yaml](../../../tools/lint/compose.yaml).
- Its license is MIT (checked 2026-10-08 on the [repository license](https://github.com/DavidAnson/markdownlint-cli2/blob/main/LICENSE)).
- The trade-off is a rule set that is stricter than most people write by hand, which is the reason for the gate. Fix the content. Do not loosen
  the configuration to make a page pass.

## Setup tutorial

**Prerequisites:** Docker.

1. Lint every Markdown file. CI runs this step, which is a workflow step, so the lines below are YAML.

   [!code-bash[Lint Markdown](../../../.github/workflows/docs.yml#markdown)]

   Expected output ends with a summary line.

   ```text
   Summary: 0 issues in 0 files
   ```

2. When a rule fires, the line names its ID, such as `MD040`. Look the ID up in the
   [rule reference](https://github.com/DavidAnson/markdownlint/blob/main/doc/Rules.md), which says what the rule wants and how to fix it.

**Cleanup:** nothing is left behind. The repository is mounted read-only.

## Further research

- [markdownlint-cli2](https://github.com/DavidAnson/markdownlint-cli2): the command line, the configuration file, and the container image (checked 2026-10-08)
- [The markdownlint rules](https://github.com/DavidAnson/markdownlint/blob/main/doc/Rules.md): one section for each rule ID (checked 2026-10-08)
