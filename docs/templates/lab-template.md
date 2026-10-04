---
title: "Lab {{N}}: {{What the learner does}}"
description: "{{One sentence: the skill the lab builds and the observation it produces.}}"
audience: [learners]
tools: [{{inventory-ids}}]
introduced: "{{phase-N}}"
prerequisites: ["{{tutorial or earlier lab}}"]
estimated-time: "{{for example 30 minutes}}"
last-verified: {{YYYY-MM-DD}}
owner: "@RostomOhannessian"
---

# Lab {{N}}: {{What the learner does}}

<!--
A lab teaches by making the learner predict, run, observe, and break something.
Steps come from a script under docs/labs/scripts so CI can run them. Every step states what the learner should observe.
-->

## Objectives

By the end you can:

- {{Objective 1, phrased as something you can demonstrate.}}
- {{Objective 2.}}

## Prerequisites and setup

- {{Environment: resource profile, tools, and the state the previous lab leaves behind.}}
- Setup command: `{{dev lab {{N}} setup}}`

## Steps

### 1. {{First step}}

{{What to do and why.}}

```text
{{command}}
```

**You should see:** {{the expected observation, specific enough to be wrong.}}

### 2. {{Second step}}

{{...}}

## Break it

{{A deliberate failure. Tell the learner what to change, then ask them to predict the result before they run it.}}

**Diagnosis:** {{How to find the cause with the tools in this repository, and the fix.}}

## Cleanup

```text
{{dev lab {{N}} cleanup}}
```

## Check your understanding

1. {{Question that needs reasoning, not recall.}}
2. {{Question.}}

<details>
<summary>Answers</summary>

1. {{Answer with the reason.}}
2. {{Answer with the reason.}}

</details>

## Further reading

- [{{Title}}]({{URL}}) (checked {{YYYY-MM-DD}})
