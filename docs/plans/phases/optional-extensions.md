---
title: "Optional extensions"
description: "Two optional pieces of work after the v1.0 launch gate: a nightly mutation report job and a web UI on top of the API. Neither is part of any exit gate, and each needs the maintainer's decision to start."
type: phase-plan
phase: optional
audience: [maintainers, learners]
last-verified: 2026-10-12
owner: "@RostomOhannessian"
---

# Optional extensions

Section numbers (§) in this file refer to [the implementation plan](../implementation-plan.md). Live progress is on [the status page](../../project/STATUS.md), which does not track these extensions until one is promoted.

| Field | Value |
| --- | --- |
| Branch | None until an extension is promoted |
| Milestone | None |
| Release | None. An extension never gates `v0.1.0` to `v1.0.0` |
| Depends on | The launch gate, except where an extension says otherwise |

## Purpose and rules

The plan of record ends with the launch gate. This file holds work that would make the portfolio stronger but that no outcome of §2.1 needs, so that it is written down once and does not get lost.

- An extension is **optional**. No exit gate, release, or required check depends on it, and the status page does not list it.
- An extension starts only when the maintainer decides to. The decision is a plan amendment (§19) that gives the extension a phase ID, work packages, Issues, and `status.yaml` entries, and that records the decision in an ADR where a technology choice is involved.
- The scope below is a starting point and not a commitment. Promotion revisits it with the facts of that day, as every phase does (§17).
- Every rule of the repository applies to an extension unchanged: contract-first, zero trust, SHA-pinned actions, OSI-licensed dependencies, and the Definition of Done (§5.4).

## Extension A: nightly mutation report job

### Goal

The mutation score of the domain and application tests is reported every night on `master`, so a slow loss of test strength shows up as a trend before it shows up as a bug. The job reports and never gates (§10.4).

### Why it is optional

WP1.6 delivers the mutation baseline and the `mutation` service of `tools/lint/compose.yaml`, and WP1.7 adds the Application baseline. A person runs them by hand. The nightly schedule saves that effort, but it needs the default branch and a hardened workflow, and Stryker.NET's Microsoft.Testing.Platform runner is still a preview ([ADR-0018](../../adr/0018-test-stack-and-quality-gates.md)), so a person can reasonably run it by hand until the runner is generally available.

### Preconditions

- Phase 2 is merged, so the workflow can follow the least-privilege and pinning patterns of the trusted workflows.
- WP1.7 has delivered the Application mutation baseline.
- The maintainer accepts that a report from a preview runner is a report to read and not a result to enforce.

### Scope

- A `mutation.yml` workflow that runs on a nightly schedule and on `workflow_dispatch`, checks out `master`, and runs `tools/lint/run-mutation.sh` through the same digest-pinned SDK image as the local service.
- Permissions are `contents: read` and nothing else, with no secrets and no write token, and every action is pinned by SHA (§9).
- The report files (`mutation-report.json` and `mutation-report.md`) are uploaded as an artifact with a retention period, and the score of each project goes to the job summary as a table.
- A score below 70 percent is an annotation (a warning), and never a failure.
- The latest report goes into the release evidence bundle (§5.6, from `v0.2.0`).

### Non-goals

- No pull request trigger, and no required check.
- No automatic Issue, comment, or other write on a drop in the score, because that would need a write token.
- No gate. The gate is WP4.8's decision, once the runner is generally available.

### Acceptance criteria

- The workflow runs on schedule for three nights in a row and finishes within 15 minutes.
- The score of a run equals the score of a local run of the `mutation` service on the same commit, within one point.
- `governance security` and the documentation auditor pass, and the Stryker.NET tool page and the testing strategy describe the job.
- The job summary shows the score of the Domain and of the Application.

### Evidence and size

A link to three consecutive scheduled runs, and the comparison with the local run. Size: S.

## Extension B: web UI on top of the API

### Goal

A small, accessible browser UI for the catalog, built as a consumer of the version 1 contract. It completes the vertical slice that the project teaches (browser, gateway, API, data) and shows a client that obeys the same HTTP rules as the API: strong ETags, `If-Match`, `Idempotency-Key`, cursor pagination, and Problem Details.

### Why it is optional

The brief asks for a secure product catalog platform and its delivery pipeline, and the API is the product. A UI adds a second technology stack, a browser trust boundary, and a large dependency surface, and Phase 1 states "no user interface" as a non-goal. It is worth doing only after the platform is complete.

### Preconditions

- Phase 4 is merged, so the Kiota-generated `Catalog.Client` and its facade (WP4.1 and WP4.2), the consumer-verified contracts, the Gateway API routes, and the signed, attested delivery of an image exist.
- The launch gate is done, so the UI cannot delay `v1.0.0`.
- The maintainer approves the framework decision (the first step below).

### Scope

The steps below are ordered, and each becomes a work package when the extension is promoted.

1. **Decision and spike.** Choose the rendering model and the framework in an ADR, after a time-boxed spike that builds the list screen and the login with each candidate. The candidates to evaluate, without a preference recorded here, are a Blazor application that shares `Catalog.Client`, a TypeScript application with a client that Kiota generates from the contract, and server-rendered pages with a light enhancement library. The criteria are the license of every linked dependency (ADR-0004), the size of the SBOM, testability, accessibility, and the supply-chain risk of the ecosystem. The repository has no Node toolchain today, so a TypeScript candidate also pays for tools, lock files, and the tool inventory entries that come with it.
2. **Shell and sign-in.** The UI signs in through Keycloak with the authorization code flow and PKCE, as a public client that the catalog realm gains. The browser keeps no token in `localStorage`. The UI is served from the same origin as the API through the gateway, so no CORS rule is needed, and the page sends a strict content security policy with no inline script.
3. **Catalog flows.** List, search, and filter products and categories with cursor pagination. Create with an `Idempotency-Key`. Edit with `If-Match`, and handle 412 (reload and show the conflict) and 428. Discontinue, which is idempotent. Show a Problem Details error as a message on the field named in `errors`, and never show a raw response. The UI shows only what the role of the signed-in user allows, and the API stays the only place that enforces it.
4. **Quality.** Component tests, end-to-end tests with a browser against the Compose stack and the kind platform, and automated accessibility checks with an OSI-licensed engine plus a keyboard-only pass. A flaky browser test follows the quarantine policy (§10.5).
5. **Delivery.** The UI image is built by the trusted workflow (signed, attested, with an SBOM), runs as a non-root user with a read-only root file system, is pinned by digest, and reaches the clusters through the same GitOps promotion as the API. The threat model gets a browser boundary: cross-site scripting, clickjacking, token handling, and third-party scripts, of which there are none.
6. **Learning content.** A tool page for each new tool, a concept page on browser authentication, and a lab that signs in as each role and causes a 412.

### Non-goals

- No change to the version 1 contract. A need of the UI becomes a contract-first change to the API, with its own ADR and tests.
- No business rule in the UI. The rules live in the domain and the API.
- No admin or multi-tenant portal, no offline mode, no mobile application, no analytics, and no third-party scripts or fonts.
- No claim of production readiness, as the Phase 1 plan already says.

### Acceptance criteria

- One command starts the stack with the UI, and a person signs in as the reader, the editor, and the admin and sees what each role allows.
- Every flow in step 3 works against the running API, a stale edit shows the conflict message, and a validation error appears on its field.
- The end-to-end and accessibility suites pass in CI, and the UI image is signed and attested.
- The UI makes no request to any origin but its own, which a test checks.
- The documentation, the threat model, and the tool inventory cover the new boundary and the new tools.

### Risks

| Risk | Mitigation |
| --- | --- |
| The JavaScript supply chain widens the attack surface | A lock file, exact versions, the license gate, the dependency review that every package goes through (ADR-0020), and a candidate with few packages weighs more in the framework ADR |
| Token theft through cross-site scripting | A strict content security policy, tokens held only in memory or behind a back end for the front end (decided in the ADR), and a test of the policy |
| The UI duplicates a rule and drifts from the API | No business rule in the UI, and the contract tests and the end-to-end suite catch a drift |
| Scope grows into a product | The non-goals above, and a size limit of one work package for each step |

### Size

L in total, split into the six steps above when the extension is promoted.

## How an extension becomes work

1. The maintainer says which extension to start and when.
2. A plan amendment gives it a phase ID, work packages with sizes and dependencies, and a milestone, adds a revision entry (§19), and updates `status.yaml`.
3. Each work package gets an Issue that meets the Definition of Ready (§5.4), and the first work package of an extension that needs a technology decision is a spike with an ADR (§5.5).
4. From then on the extension follows the same delivery model as a phase, including a fresh session for each work package (§5.8).
