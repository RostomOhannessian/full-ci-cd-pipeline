---
title: "Threat model entry: {{Trust boundary}}"
description: "{{The boundary, what crosses it, and the threats considered.}}"
audience: [maintainers]
last-verified: {{YYYY-MM-DD}}
owner: "@RostomOhannessian"
---

# Threat model entry: {{Trust boundary}}

<!--
One entry per trust boundary, added to docs/security/threat-model.md. Update it whenever a work package changes a boundary,
a data flow, or a credential (Definition of Done item 7).
-->

## Boundary

- **Between:** {{zone A}} and {{zone B}}
- **Assets:** {{what is worth protecting here}}
- **Actors:** {{who or what can act across the boundary, including attackers}}

## Data flows

| Flow | From | To | Data | Protocol and authentication |
| --- | --- | --- | --- | --- |
| {{F1}} | {{source}} | {{destination}} | {{what crosses}} | {{TLS, token, certificate}} |

## STRIDE analysis

| Threat | Category | Scenario | Control | Test or evidence |
| --- | --- | --- | --- | --- |
| {{T1}} | {{Spoofing, Tampering, Repudiation, Information disclosure, Denial of service, Elevation of privilege}} | {{what the attacker does}} | {{what prevents or detects it}} | {{test ID or artifact}} |

## Residual risk

{{What remains after the controls, why it is acceptable or what will reduce it, and the owner.}}

## Open questions

- {{Anything not yet decided, with the work package that decides it.}}
