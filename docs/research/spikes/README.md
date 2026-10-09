---
title: "Spike reports"
description: "One report per time-boxed risk spike, with the question, the pass criteria, the result, and the decision each one changed."
audience: [maintainers, learners]
last-verified: 2026-10-06
owner: "@RostomOhannessian"
---

# Spike reports

A spike answers one question about the riskiest integration of a phase within a time box of one day or less ([plan section 5.5](../../plans/implementation-plan.md)). Each report states the pass criteria before the run, records the result even when it is negative, and names the ADRs it affected. A failed spike triggers a plan amendment before any dependent work package starts.

Spike code is throwaway. It lives on local `spike/<phase>.<x>-<slug>` branches that are never merged or pushed ([ADR-0003](../../adr/0003-branch-and-work-package-protocol.md)), so each report carries the versions, image digests, commands, and key snippets needed to repeat the spike. The reports are written from [the spike template](../../templates/spike-report-template.md).

## Phase 1 spikes (WP1.0, run 2026-10-06)

| Spike | Question | Result | Decisions and risks touched |
| --- | --- | --- | --- |
| [1.a](1.a-pactnet-kestrel.md) | Does PactNet 5.0.1 verify a Kestrel-hosted provider on .NET 10? | Pass | [ADR-0017](../../adr/0017-contract-first-api-design.md), [ADR-0018](../../adr/0018-test-stack-and-quality-gates.md), risk R10 |
| [1.b](1.b-fusioncache-valkey.md) | Does the FusionCache backplane work against Valkey 8.1 and 9.1? | Pass with conditions | [ADR-0016](../../adr/0016-cache-implementation-fusioncache.md), risk R11 |
| [1.c](1.c-mtp-coverage-stryker.md) | Do Microsoft.Testing.Platform, Code Coverage, and Stryker's preview runner work together? | Pass with conditions | [ADR-0018](../../adr/0018-test-stack-and-quality-gates.md), [ADR-0004](../../adr/0004-licensing-and-dependency-license-policy.md) |
| [1.d](1.d-testcontainers-mssql.md) | Does Testcontainers start SQL Server 2022 reliably on the supported hosts? | Pass on Windows x86-64; other hosts not verified hands-on | [ADR-0019](../../adr/0019-resource-profiles-and-host-support.md), [ADR-0018](../../adr/0018-test-stack-and-quality-gates.md), risk R3 |
| [1.e](1.e-rotating-credentials.md) | Can file-rotated credentials work with pooling on and a cleared old pool? | Pass | [ADR-0008](../../adr/0008-secrets-broker-vault-over-openbao.md) reviewed, no change |

## What the results leave open

- **Host coverage.** Spike 1.d verified Windows x86-64 only. WP1.1 has no container tests, so the Linux Testcontainers evidence moves to the first integration suite in WP1.8 (see the [spike 1.d follow-up](1.d-testcontainers-mssql.md)). macOS Intel is not verified hands-on, and Apple Silicon and Windows on Arm are untested and documented by Microsoft as unsupported.
- **A proposed plan clarification.** Spike 1.b observed cache key and channel names that the ACL patterns in plan section 8.5 do not match. The plan is not edited until the maintainer approves the change.
- **Unexplained delay.** Spike 1.b saw a one to two second delay on the first cache call after a Valkey outage began, on one host. WP1.9 must measure it.
- **Not tested.** Vault itself (Phase 3), Linux file events for credential rotation, and PactNet and Testcontainers on Linux and macOS.

## Adding a spike report

1. Copy [the spike template](../../templates/spike-report-template.md) and name the file `<phase>.<x>-<slug>.md`.
2. Write the question and the pass criteria before running anything.
3. Record versions, digests, commands, and what did not work. Do not paste secrets, personal data, or absolute local paths.
4. Add a row here, and update the ADRs and risks the result touches in the same pull request.
