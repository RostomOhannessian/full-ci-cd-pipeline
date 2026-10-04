---
title: "Runbooks"
description: "What an operational runbook must contain in this repository and which runbooks the phase plans expect to add."
audience: [maintainers, operators, contributors]
type: reference
last-verified: 2026-10-04
owner: "@RostomOhannessian"
---

# Runbooks

A runbook is a short operational procedure for a real task or incident. It tells an operator when to use it, what risk it carries, the exact steps to run, how to verify the result, and how to roll back. Start new operational pages from [the runbook template](../templates/runbook-template.md).

## Runbook format and expectations

From the template, every runbook must name the trigger, state the blast radius, list exact commands, include a verification section, describe the rollback path, and keep a rehearsal log. The plans also expect runbooks to back drills, rotation work, and recovery procedures with durable evidence.

## Planned runbooks by phase

| Phase | Runbook | Work package | Purpose | Status |
| --- | --- | --- | --- | --- |
| 1 | Machine migration | WP1.4, extended in WP3.10 | Clone, open the Dev Container, read the status page, and recreate the local environment on a new machine without copying secrets or state. | planned |
| 2 | Compromised action or dependency response | WP2.7 | Contain and supersede a bad workflow dependency or build tool. | planned |
| 2 | Vulnerable released image response | WP2.7 | Triage a released image issue, document the decision, and ship the correction. | planned |
| 3 | Vault unseal and operator break-glass | WP3.3 | Recover access to the secrets tier and record trust-anchor handling. | planned |
| 3 | Credential rotation and revocation | WP3.3, WP3.4 | Rotate runtime credentials and prove workloads recover cleanly. | planned |
| 3 | Backup and restore the catalog database | WP3.4, WP3.10 | Recover catalog data from backup. | planned |
| 3 | Credential leak response | WP3.10 | Contain a leaked runtime or local platform credential. | planned |
| 3 | Policy bypass response | WP3.10 | Record, limit, and unwind an exception to normal policy enforcement. | planned |
| 3 | Cluster teardown and rebuild | WP3.10 | Destroy and recreate the disposable local cluster cleanly. | planned |
| 3 | Disaster recovery after local state loss | WP3.10 | Recover from lost cluster state, local state, or generated local credentials. | planned |
| 4 | Automated rollback | WP4.6 | Verify that a failed canary aborts automatically. | planned |
| 4 | Manual rollback | WP4.6 | Re-promote a previous Freight or revert a promotion safely. | planned |
| 4 | Emergency rollback with Git reconciliation | WP4.6 | Roll back live state first, then restore Git as the source of truth. | planned |
| 4 | Failed migration drill | WP4.6 | Respond when rollout or startup fails because of schema or migration trouble. | planned |
| 4 | Signature rejection drill | WP4.6 | Recover when admission rejects a signature or attestation. | planned |
| 4 | Argo CD outage drill | WP4.6 | Operate safely while reconciliation is unavailable. | planned |
| 4 | Kargo outage drill | WP4.6 | Handle promotion-control failure without losing audit evidence. | planned |
| 4 | Vault sealed drill | WP4.6 | Restore the secrets broker during an outage scenario. | planned |
| 4 | Valkey outage drill | WP4.6 | Run through cache failure, degradation, and recovery. | planned |

Some of these topics are named directly in the plans, while others are stated as required runbook coverage for drills or incident response. No runbook page exists yet, so every entry above remains plain text until its owning work package writes it.
