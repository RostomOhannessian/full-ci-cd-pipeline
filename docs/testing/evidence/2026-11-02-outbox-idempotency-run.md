---
title: "Evidence record: outbox and idempotency suites on Windows and Linux (2026-11-02)"
description: "A dated record of the outbox and idempotency suites of WP1.8 on Windows with Docker Desktop and in the pinned Linux SDK image with the Docker socket: what ran where, the counts, the coverage, the migration rehearsal, and what the runs did not show."
type: evidence-record
date: 2026-11-02
status: verified
audience: [maintainers, contributors]
last-verified: 2026-11-02
owner: "@RostomOhannessian"
---

# Evidence record: outbox and idempotency suites on Windows and Linux (2026-11-02)

This record is the evidence for `REQ-DATA-003` and `REQ-DATA-004` and for the outbox part of `REQ-DATA-002` and `REQ-DOM-004`. It continues [the persistence record](2026-11-02-persistence-linux-run.md) for the second pull request of WP1.8, and it says what ran where, so a reader can tell which host proved which claim ([ADR-0035](../../adr/0035-outbox-processor-design.md), [ADR-0036](../../adr/0036-idempotency-store.md), [ADR-0034](../../adr/0034-integration-test-database-strategy-and-linux-run.md)).

## What ran where

| Where | Command | What it ran | Result |
| --- | --- | --- | --- |
| Windows x86-64, Docker Desktop 29.9.2 (server), .NET SDK 10.0.403 | `dotnet test --solution ProductCatalog.slnx --configuration Release --no-build --coverage --coverage-output-format cobertura --report-trx --results-directory TestResults` after a locked restore (`CI=true`) and a Release build with warnings as errors | Every test project natively, including `tests/Catalog.Infrastructure.Tests` against a SQL Server 2022 container | All seven projects passed: 192 Application, 90 Architecture, 100 Contract, 173 Domain, 168 Infrastructure, 265 Documentation.Auditor, and 608 Governance.Auditor tests, 1,596 in total |
| Linux, the pinned SDK 10.0.401 image, Docker socket mounted | `docker compose -f tools/lint/compose.yaml run --rm linux-tests` | A scratch copy of the repository: locked restore, Release build with warnings as errors, every test project with the code coverage extension, and the coverage thresholds | 1,596 passed, 0 failed, 0 skipped |

The Linux run started SQL Server and the Testcontainers reaper as sibling containers through the socket, as in the first record. The Infrastructure project took about 20 seconds on both hosts: one container for the assembly and a migrated database for each test class.

## What the new tests proved

| Claim | Test class | Host |
| --- | --- | --- |
| An aggregate change and its outbox rows commit or roll back together, the events are cleared after a save, a failed save keeps them, and the synchronous and asynchronous saves behave the same | `OutboxInterceptorTests` | Windows and Linux |
| The JSON of each of the eight events is a snapshot, every event type of the Domain has one, and an event without a shape is refused | `OutboxPayloadTests` | Windows and Linux |
| The processor delivers at least once in order, records a receipt for each handler, retries with a bounded backoff, moves exhausted work to the dead-letter state with a metric and an error, and stores the type of a fault and never its message | `OutboxProcessorTests` | Windows and Linux |
| A stop after delivery, the death of a processor, and a lost lease are recovered by the clock, and a processor whose lease ended cannot finish the message | `OutboxProcessorTests` | Windows and Linux |
| Four concurrent processors never lease one message twice, and a lease skips a row that another transaction holds and does not wait for it | `OutboxProcessorTests` | Windows and Linux |
| The loop delivers after a wake-up, leases the next batch at once after a full one, survives a fault of a pass, runs the cleanup, and stops | `OutboxBackgroundServiceTests`, `OutboxSignalTests` | Windows and Linux |
| The backoff doubles, stops at its maximum, and keeps its jitter inside the bounds, and the settings have validated bounds | `OutboxBackoffTests`, `PersistenceOptionsTests` | Windows and Linux |
| The store reserves a key for one caller, replays the first result, answers reused and in progress, compares keys exactly, expires after 24 hours, lets a second request take over after two minutes, and lets only one request commit its effect after a takeover | `IdempotencyStoreTests` | Windows and Linux |
| Eight requests that begin together get one reservation, and a key with SQL text is data | `IdempotencyStoreTests` | Windows and Linux |
| The record and the effect commit together, and a rollback after the record was written removes both and the outbox rows | `IdempotencyStoreTests` | Windows and Linux |
| The collation migration keeps the rows and the primary key of a database that ran the first migration | `MigrationTests` | Windows and Linux |

No test sleeps. A fake clock ends each lease and each backoff, a channel carries each delivery of the loop to the test, and the only time limits are safety nets of 30 seconds that a passing run never reaches.

## Coverage

The thresholds of `governance/policies/coverage-thresholds.json` passed on both hosts.

| Scope | Metric | Required | Windows | Linux |
| --- | --- | --- | --- | --- |
| Catalog.Domain | lines | 90% | 99.63% (271 of 272) | 99.63% (271 of 272) |
| Catalog.Domain | branches | 80% | 98.41% (124 of 126) | 98.41% (124 of 126) |
| Catalog.Application | lines | 85% | 100% (383 of 383) | 100% (383 of 383) |
| Catalog.Application | branches | 80% | 99.52% (209 of 210) | 99.52% (209 of 210) |
| Solution | lines | 80% | 98.57% (1,517 of 1,539) | 98.57% (1,517 of 1,539) |

The Infrastructure assembly has no threshold of its own in the policy, and a change of a threshold needs an ADR, so none was added. Its line coverage without the generated migrations was 97.13 percent (711 of 732) on Windows. The lines that the suites do not reach are the design-time factory, the constructors that only EF Core calls, and a few guard branches.

## The migration rehearsal

The idempotent script of both migrations was generated and applied with `sqlcmd -I` to a throwaway SQL Server 2022 container of the digest that `compose.yaml` pins. It applied both migrations, a second run changed nothing, the caller and the key had the collation `Latin1_General_100_BIN2`, and the primary key existed. A plain `ALTER COLUMN` of a key column failed with error 5074, which is why the second migration drops the key. Without `-I` the script failed with error 1934, because `sqlcmd` turns `QUOTED_IDENTIFIER` off by default, and the runbook now says so. The migration bundle was not rebuilt for the second migration. `MigrationTests` applies both migrations through the same `Migrate` call that the bundle makes.

## Limits

- The Linux run needs the Docker socket, which is the exception of ADR-0034. The hosted `ci` workflow runs the suites directly, and no hosted run has happened, because nothing is pushed yet.
- The suites ran on x86-64 hosts only.
- The license gate of the `ci` workflow still fails on `Microsoft.Data.SqlClient.SNI.runtime`, the open finding of ADR-0033. The new package, `Microsoft.Extensions.Hosting.Abstractions` 10.0.14, passed it.
- The tests prove at-least-once delivery against one SQL Server with several processors in one process. A rollout with several replicas is the evidence that `REQ-DATA-003` also asks for in WP4.4.
- Timings are single samples from one machine.
