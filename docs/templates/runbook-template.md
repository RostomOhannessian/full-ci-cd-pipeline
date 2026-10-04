---
title: "Runbook: {{Procedure}}"
description: "{{When to use this runbook and what it achieves.}}"
audience: [maintainers, operators]
tools: [{{inventory-ids}}]
introduced: "{{phase-N}}"
last-verified: {{YYYY-MM-DD}}
owner: "@RostomOhannessian"
---

# Runbook: {{Procedure}}

<!--
A runbook is used under pressure. Short sentences, exact commands, expected output, and a way back at every step.
It is rehearsed, and the rehearsal date is recorded under "Rehearsal log".
-->

## When to use this

{{The trigger: the alert, symptom, or scheduled task.}}

## Impact and risk

- **Blast radius:** {{what can be affected}}
- **Reversible:** {{yes or no, and how}}
- **Needs confirmation from:** {{who must agree first, if anyone}}

## Before you start

- {{Access, tools, and state you need.}}
- {{Verify the cluster context or environment first, with the exact command.}}

## Procedure

1. {{Step.}}

   ```text
   {{command}}
   ```

   Expected output: {{what you should see}}. If you see something else: {{what to do}}.

2. {{Step.}}

## Verify

{{Commands and signals that prove the procedure worked.}}

## Roll back

{{How to undo the procedure, or why it cannot be undone and what to do instead.}}

## After the procedure

- {{Record evidence: transcript, links, and times.}}
- {{Follow-ups: Issue, ADR, or runbook update.}}

## Rehearsal log

| Date | Who | Environment | Result | Changes made to this runbook |
| --- | --- | --- | --- | --- |
| {{YYYY-MM-DD}} | {{name}} | {{profile}} | {{pass or fail}} | {{what changed}} |
