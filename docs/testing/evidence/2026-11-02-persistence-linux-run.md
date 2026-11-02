---
title: "Evidence record: persistence suites on Windows and Linux (2026-11-02)"
description: "A dated record of the persistence suites of WP1.8 on Windows with Docker Desktop and in the pinned Linux SDK image with the Docker socket: what ran where, the counts, the coverage, and what the runs did not show."
type: evidence-record
date: 2026-11-02
status: verified
audience: [maintainers, contributors]
last-verified: 2026-11-02
owner: "@RostomOhannessian"
---

# Evidence record: persistence suites on Windows and Linux (2026-11-02)

This record is the Linux evidence of the Testcontainers suites that [spike 1.d](../../research/spikes/1.d-testcontainers-mssql.md) expected from WP1.8, and the manual evidence for `REQ-QUA-001`. It says what ran where, so a reader can tell which host proved which claim ([ADR-0034](../../adr/0034-integration-test-database-strategy-and-linux-run.md)).

## What ran where

| Where | Command | What it ran | Result |
| --- | --- | --- | --- |
| Windows x86-64, Docker Desktop 29.9.2 (server), .NET SDK 10.0.403 | `dotnet test --solution ProductCatalog.slnx --configuration Release --no-build` | Every test project natively, including `tests/Catalog.Infrastructure.Tests` against a SQL Server 2022 container | All seven projects passed: 192 Application, 90 Architecture, 100 Contract, 173 Domain, 60 Infrastructure, 265 Documentation.Auditor, and 608 Governance.Auditor tests, 1,488 in total |
| Linux, the pinned SDK 10.0.401 image, Docker socket mounted | `docker compose -f tools/lint/compose.yaml run --rm linux-tests` | A scratch copy of the repository: locked restore, Release build with warnings as errors, every test project with the code coverage extension, and the coverage thresholds | 1,488 passed, 0 failed, 0 skipped, in about 14 seconds of test time |

The Linux run started SQL Server and the Testcontainers reaper as sibling containers through the socket, and the test container reached them through `host.docker.internal`. The persistence project took about 13 seconds on both hosts, with one container for the assembly and a migrated database for each of its test classes.

## Coverage on Linux

The thresholds of `governance/policies/coverage-thresholds.json` passed in the same run.

| Scope | Metric | Required | Actual |
| --- | --- | --- | --- |
| Catalog.Domain | lines | 90% | 99.63% (271 of 272) |
| Catalog.Domain | branches | 80% | 98.41% (124 of 126) |
| Catalog.Application | lines | 85% | 100% (382 of 382) |
| Catalog.Application | branches | 80% | 99.52% (209 of 210) |
| Solution | lines | 80% | 98.75% (1,110 of 1,124) |

The Infrastructure assembly has no threshold of its own in the policy, and the solution threshold excludes `/Migrations/`. The Infrastructure code is covered by the persistence suites.

## The migration bundle

The bundle was built and run on Windows against a throwaway SQL Server 2022 container of the digest that `compose.yaml` pins. It applied `InitialCreate`, a second run applied nothing, and the six tables of the schema existed. The rehearsal is in the log of [the migration runbook](../../runbooks/database-migration.md). The bundle was not run on Linux.

## Limits

- The Linux run needs the Docker socket, which is the exception of ADR-0034. The hosted `ci` workflow runs the suites directly on `ubuntu-latest`, and no hosted run has happened, because nothing is pushed yet. The first hosted run on the pull request is the confirmation.
- The suites ran on x86-64 hosts only. Apple Silicon and Windows on Arm are best effort (ADR-0019) and were not tried.
- The license gate of the `ci` workflow fails on `Microsoft.Data.SqlClient.SNI.runtime`, an open finding for the maintainer that ADR-0033 describes. The test runs are not affected.
- Timings are single samples from one machine.
