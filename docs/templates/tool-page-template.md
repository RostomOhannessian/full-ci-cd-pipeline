---
title: "{{Tool name}}"
description: "{{One sentence: what the tool is and its role in this repository.}}"
audience: [learners, contributors]
tier: A # A, B, or C. See docs/reference/tools/index.md for what each tier requires.
tools: [{{inventory-id}}]
introduced: "{{phase-N}}"
prerequisites: ["{{what the reader must have or know first}}"]
estimated-time: "{{for example 25 minutes}}"
last-verified: {{YYYY-MM-DD}}
verified-against: { {{inventory-id}}: "{{version}}" }
owner: "@RostomOhannessian"
---

# {{Tool name}}

<!--
Write for a developer who knows the syntax of the languages involved but not this tool.
Tier B pages keep the four required sections, compact. Tier A pages use every section below.
Every command must be one you ran; pull commands from tested scripts where possible.
Link the ADR for the decision instead of repeating it.
-->

## Overview

{{The problem the tool solves, in this repository's terms. Define the three to five core concepts a reader needs. State the role the tool plays
and where it sits in the architecture, with a small Mermaid diagram if it helps.}}

## Decision rationale

{{What else was considered, the criteria, and why this tool won. Be honest about tradeoffs, license, and maintenance posture. Link the ADR.}}

## Setup tutorial

**Prerequisites:** {{tools, versions, resources}}

1. {{Step with the exact command.}}

   Expected output:

   ```text
   {{what the reader should see}}
   ```

2. {{Next step.}}

**Cleanup:** {{how to remove everything this tutorial created}}

## How this project uses it

{{The exact files, the trust boundary, the data flow, who owns it, and the failure modes. Link real paths.}}

## Validation and troubleshooting

{{How to prove it works: health checks and diagnostic commands. Then the common failures, each with symptom, cause, and fix.}}

## Security and operations

{{Credentials, permissions, network access, update policy, and blast radius if it fails or is compromised.}}

## Lab

{{Link the hands-on lab from docs/labs, or write "None yet" with the work package that will add it.}}

## Further research

- [{{Title}}]({{URL}}): {{why it is worth reading}} (checked {{YYYY-MM-DD}})
