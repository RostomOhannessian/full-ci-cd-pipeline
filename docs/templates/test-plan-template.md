---
title: "Phase {{N}} test plan"
description: "{{Scope: the requirements and risks this plan covers and how each is verified.}}"
audience: [maintainers, contributors]
last-verified: {{YYYY-MM-DD}}
owner: "@RostomOhannessian"
---

# Phase {{N}} test plan

<!--
Every requirement in scope maps to at least one test or a justified piece of non-automated evidence.
`governance trace` (WP1.2) checks this table against requirements.yaml and the test metadata.
-->

## Scope and approach

{{What is in scope, what is not, the risks that drive test depth, and the environments used.}}

## Test cases

| ID | Requirement | Purpose | Type | Preconditions | Automation | Expected result | Failure diagnostics | Evidence |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| {{TC-N-001}} | {{REQ-XXX-001}} | {{what it proves}} | {{unit, integration, contract, policy, e2e, drill}} | {{state needed}} | {{path or workflow}} | {{observable result}} | {{where to look when it fails}} | {{artifact or report}} |

## Negative and failure-injection cases

| ID | Failure injected | Expected behavior | Automation |
| --- | --- | --- | --- |
| {{TC-N-101}} | {{dependency down, expired credential, malformed input}} | {{degrades or rejects as designed}} | {{path}} |

## Manual drills

| ID | Drill | Procedure | Evidence recorded |
| --- | --- | --- | --- |
| {{DR-N-001}} | {{rollback, restore}} | {{link runbook}} | {{transcript path}} |

## Environments and data

{{Profiles, fixtures, synthetic data, and how containers are shared.}}

## Exit criteria

- {{Every in-scope requirement has passing evidence.}}
- {{Thresholds met: coverage, mutation, performance.}}
- {{No quarantined flaky test older than seven days.}}
