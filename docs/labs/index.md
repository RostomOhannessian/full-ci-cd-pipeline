---
title: "Labs"
description: "What a lab looks like in this repository and which hands-on exercises each phase plans to add."
audience: [learners, contributors, maintainers]
type: reference
last-verified: 2026-10-04
owner: "@RostomOhannessian"
---

# Labs

A lab is a hands-on exercise that asks you to predict, run, observe, and then break something safely. Start new lab content from [the lab template](../templates/lab-template.md).

## Lab format and expectations

Every lab follows the same teaching contract from the template and [plan section 11.3](../plans/implementation-plan.md):

- state the objective and prerequisites first;
- use steps with exact commands and the expected observation for each step;
- include verification so the learner can prove the result;
- include cleanup so the environment returns to a known state;
- include extensions or a break-it exercise so the learner can diagnose a failure, not just follow a script.

## Planned labs by phase

| Phase | Lab | Work package | Learning objective | Status |
| --- | --- | --- | --- | --- |
| 1 | Quick start: the API-only stack | WP1.4, WP1.10, WP1.13 | Bring up the first runnable local slice and call the API successfully. | planned |
| 1 | Walk the boundaries: break an architecture rule and watch the tests fail | WP1.1, WP1.13 | See Clean Architecture rules enforced as executable tests. | planned |
| 1 | Create a product through CQRS and watch the outbox deliver | WP1.7, WP1.8, WP1.13 | Follow one write through handlers, persistence, and event delivery. | planned |
| 1 | Prove L1/L2 caching and cross-node invalidation | WP1.9, WP1.13 | Observe cache layers, tags, and cross-node invalidation. | planned |
| 1 | Trigger 428 and 412 concurrency responses | WP1.10, WP1.13 | Learn how ETags and conditional writes protect consistency. | planned |
| 1 | Replay an idempotent request | WP1.8, WP1.10, WP1.13 | See why idempotency keys make retries safe. | planned |
| 1 | Trace a request end to end | WP1.11, WP1.13 | Connect HTTP traffic, logs, metrics, and traces. | planned |
| 1 | Add a destructive migration and watch the blast-radius evaluator flag it | WP1.12, WP1.13 | See governance detect risky schema change patterns early. | planned |
| 2 | Inspect the image layers and the user | WP2.1, WP2.7 | Verify non-root, minimal image construction. | planned |
| 2 | Read an SBOM and find a transitive dependency | WP2.4, WP2.7 | Learn how SBOMs expose what the image really contains. | planned |
| 2 | Reproduce the Trivy gate and write a time-limited VEX exception | WP2.4, WP2.7 | Practice vulnerability triage and justified exceptions. | planned |
| 2 | Verify the signature, provenance, and SBOM attestation by hand | WP2.5, WP2.7 | Prove an image's identity and build history outside CI. | planned |
| 2 | Trace a commit to its digest, attestation, and Rekor entry, and see exactly what identity metadata is public | WP2.5, WP2.7 | Understand the public evidence keyless signing exposes. | planned |
| 2 | Run a supply-chain incident tabletop exercise: the March 2026 trivy-action compromise mapped to our controls | WP2.7 | Map a real incident to the planned controls and evidence. | planned |
| 2 | Break a workflow rule (an unpinned action, excessive permissions, or `secrets.*`) and watch the gates block it | WP2.2, WP2.7 | See workflow hardening controls fail closed. | planned |
| 3 | Stack boundaries and state | WP3.1, WP3.10 | Understand Terraform stacks, local state, and environment boundaries. | planned |
| 3 | Prove segmentation with probes, including denied flows | WP3.2, WP3.10 | Verify public, private, and data-tier network boundaries. | planned |
| 3 | Exchange GitHub OIDC for a Vault token (CI) and a Kubernetes service-account token for a Vault token (cluster) | WP3.3, WP3.10 | Learn workload identity in both automation and cluster contexts. | planned |
| 3 | Watch a dynamic SQL credential rotate, get revoked, and the app reconnect | WP3.3, WP3.4, WP3.10 | Observe dynamic secrets and connection recovery. | planned |
| 3 | Decode a Keycloak token and map it to policies | WP3.5, WP3.10 | Connect claims, scopes, roles, and policy checks. | planned |
| 3 | Trigger Kyverno denials: an unsigned image, a `NodePort` in `data`, a root container | WP3.6, WP3.10 | Watch platform policy deny non-compliant workloads. | planned |
| 3 | Unseal Vault and run break-glass | WP3.3, WP3.10 | Practice operator recovery steps for the secrets tier. | planned |
| 3 | Back up and restore the catalog database | WP3.4, WP3.10 | Rehearse data recovery on the local platform. | planned |
| 3 | Read a Terraform plan PR comment | WP3.9, WP3.10 | Understand plan summaries, highlights, and review signals. | planned |
| 4 | Generate the client and break the contract | WP4.1, WP4.7 | See contract drift and breaking-change detection in action. | planned |
| 4 | Read a failing Pact verification | WP4.2, WP4.7 | Diagnose consumer-provider contract mismatch. | planned |
| 4 | Fuzz the API with Schemathesis | WP4.1, WP4.7 | Explore generated inputs and schema-driven testing. | planned |
| 4 | Watch Argo CD reconcile drift | WP4.3, WP4.7 | See Git stay authoritative when live state drifts. | planned |
| 4 | Run a canary and read an AnalysisRun | WP4.4, WP4.7 | Understand progressive delivery decisions and evidence. | planned |
| 4 | Promote one Freight from dev to prod | WP4.5, WP4.7 | Follow one signed digest through the promotion system. | planned |
| 4 | Bad canary → automatic abort | WP4.4, WP4.6, WP4.7 | See failed rollout analysis stop the release automatically. | planned |
| 4 | Manual and emergency rollback with Git reconciliation | WP4.6, WP4.7 | Practice both operator-led rollback paths and the Git fix-up step. | planned |
| 4 | Ship an expand/contract migration across two releases | WP4.4, WP4.6, WP4.7 | Learn safe schema evolution under rollback constraints. | planned |
