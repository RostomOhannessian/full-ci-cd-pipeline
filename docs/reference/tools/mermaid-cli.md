---
title: "Mermaid CLI"
description: "The command-line renderer that proves every Mermaid diagram in the documentation is valid."
audience: [learners, contributors]
tier: B
tools: [mermaid-cli, documentation-auditor]
introduced: "phase-1"
prerequisites: ["Docker"]
estimated-time: "10 minutes"
last-verified: 2026-10-08
verified-against: { mermaid-cli: "12.0.0" }
owner: "@RostomOhannessian"
---

# Mermaid CLI

## Overview

Mermaid draws diagrams from text, so a diagram is a few lines in a Markdown code fence that Git can diff and review. Mermaid CLI (`mmdc`) renders
that text to an image with a headless browser. A diagram that does not parse makes `mmdc` fail, and that is what this repository uses it for:
the check proves that every diagram renders before the site shows it.

Two checks cover a diagram, and they answer different questions:

| Check | Question | Tool |
| --- | --- | --- |
| Does it render? | Is the syntax valid? | Mermaid CLI, through [tools/lint/check-diagrams.sh](../../../tools/lint/check-diagrams.sh) |
| Can everyone read it? | Does it have an accessible title (`accTitle`) and description (`accDescr`)? | The [Documentation.Auditor](documentation-auditor.md), because Mermaid CLI accepts a diagram with neither |

## Decision rationale

[ADR-0005](../../adr/0005-documentation-system.md) names Mermaid CLI as the diagram check, and the DocFX `modern` template renders the same
fences in the site. The browser in the site loads its own copy of Mermaid, so the two can differ by a version, and the check catches the syntax
errors that matter. No other renderer was evaluated.

- License: MIT (checked 2026-10-08 on the [repository license](https://github.com/mermaid-js/mermaid-cli/blob/master/LICENSE)).
- Delivery: the official container image `ghcr.io/mermaid-js/mermaid-cli/mermaid-cli`, pinned by digest in
  [tools/lint/compose.yaml](../../../tools/lint/compose.yaml). The image bundles Chromium, so nothing is installed on the host.
- Trade-off: the image is large, about 3.5 GB unpacked (checked 2026-10-08), so the first pull takes a while.

## Setup tutorial

**Prerequisites:** Docker.

1. Render every diagram. CI runs this step, which is a workflow step, so the lines below are YAML.

   [!code-bash[Render every Mermaid diagram](../../../.github/workflows/docs.yml#diagrams)]

   The script finds each Markdown file with a Mermaid fence, renders it into a temporary folder inside the container, and prints one line for each file.

   Expected output ends with a line such as the one below.

   ```text
   Mermaid: 4 files checked, 0 failed.
   ```

2. Break a diagram on purpose to see the failure: remove the arrow target from one line and run the check again. The output names the file
   and the parse error, and the command exits with code 1.

**Cleanup:** nothing is left behind. The repository is mounted read-only, so the rendered images exist only inside the container.

## Further research

- [Mermaid CLI](https://github.com/mermaid-js/mermaid-cli): the README covers Markdown input and the container image (checked 2026-10-08)
- [Mermaid accessibility](https://mermaid.js.org/config/accessibility.html): `accTitle` and `accDescr` (checked 2026-10-08)
- [Mermaid](https://mermaid.js.org/): the diagram syntax (checked 2026-10-08)
