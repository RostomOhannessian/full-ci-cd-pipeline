---
title: "Spike {{phase.x}}: {{Question}}"
description: "{{The question, the answer in one sentence, and the decision it changes.}}"
audience: [maintainers]
type: spike-report
date: {{YYYY-MM-DD}}
last-verified: {{YYYY-MM-DD}}
owner: "@RostomOhannessian"
---

# Spike {{phase.x}}: {{Question}}

<!--
A spike answers one question within a time box. Record the result even when it is negative: a failed spike is a plan input.
Spike branches are never merged. This report and the ADR change are.
-->

## Question

{{One question with a measurable outcome.}}

## Risk reduced

{{Link the risk register entry and the ADR that depends on the answer.}}

## Time box and method

- **Time box:** {{one day or less}}
- **Environment:** {{host, versions, profile}}
- **Method:** {{what was built or measured, and how}}

## Results

{{Measurements and observations, with the exact versions and commands. Include what did not work.}}

## Conclusion

**Answer:** {{yes, no, or yes with conditions}}

{{What the result means for the plan.}}

## Decisions and follow-ups

- ADR: {{updated or created, with the link}}
- Plan changes: {{work packages added, removed, or resized}}
- New risks or Issues: {{links}}

## Reproduce

```text
{{Commands another person can run to repeat the spike.}}
```
