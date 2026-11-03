---
title: "Evidence record: the cache adapter against a real Valkey (2026-11-03)"
description: "A dated record of the first pull request of WP1.9 on Windows with Docker Desktop and in the pinned Linux SDK image: what ran where, the counts, the coverage, the behavior of Valkey that the tests showed, and what the runs did not show."
type: evidence-record
date: 2026-11-03
status: verified
audience: [maintainers, contributors]
last-verified: 2026-11-03
owner: "@RostomOhannessian"
---

# Evidence record: the cache adapter against a real Valkey (2026-11-03)

This record is the evidence for the first pull request of WP1.9 (Caching): the FusionCache adapter in `Catalog.CrossCutting`, the Valkey connection in `Catalog.Infrastructure`, and the access control list of the application user ([ADR-0038](../../adr/0038-catalog-cache-adapter-and-valkey-connection.md)). It says what ran where, so a reader can tell which host proved which claim. The two-host invalidation, the Valkey outage, and the convergence through the outbox belong to the second pull request and are not claimed here.

## What ran where

| Where | Command | What it ran | Result |
| --- | --- | --- | --- |
| Windows x86-64, Docker Desktop 29.9.2 (server), .NET SDK 10.0.403 | `dotnet test --solution ProductCatalog.slnx --configuration Release --no-build` | Every test project natively, with a SQL Server 2022 container and a Valkey 8.1 container started by assembly fixtures | The first full run of this branch ran 1,801 tests. 1,798 passed, and 3 failed: the self-checks of the documentation and of the traceability report, which the later edits of the same day fixed. The last run of the final tree passed the Architecture, Domain, Documentation auditor, and Governance auditor suites. Smart App Control then blocked the freshly built `Catalog.Application.dll` and `Catalog.Infrastructure.Tests.dll` (0x800711C7), also after a clean rebuild, so the Application and Infrastructure suites did not load natively and the Linux run is their record. No host setting was changed |
| Linux, the pinned SDK 10.0.401 image, Docker socket mounted | `docker compose -f tools/lint/compose.yaml run --rm linux-tests` | A scratch copy of the repository: locked restore, Release build with warnings as errors, every test project with the code coverage extension, and the coverage thresholds | 1,801 tests ran, 1,801 passed, 0 failed, 0 skipped, in 34.5 seconds (the Infrastructure suite took 33 seconds). The coverage gate passed: Catalog.Application 98.97% of lines and 99.52% of branches, Catalog.Domain 99.63% of lines and 98.41% of branches, and the solution 97.86% of lines (2,013 of 2,057) |

## What the tests showed about Valkey

These facts were measured against the server image that `compose.yaml` pins (Valkey 8.1.10, digest `sha256:640c5e62…`, checked 2026-11-03).

| Question | Answer | Test |
| --- | --- | --- |
| What does a key look like in Valkey? | `v2:catalog:{env}:v1:product:{id}`, a hash, with a time to live that follows the settings. A tag marker is `v2:catalog:{env}:__fc:t:{tag}` | `CacheOverValkeyTests` |
| Which keys, channels, and commands does the application user need? | `~v2:catalog:dev:*`, `&catalog:dev.Backplane:v2`, and 14 commands (three `client` subcommands: `id`, `setinfo`, and `setname`, plus `echo`, `expire`, `hmget`, `hmset`, `info`, `ping`, `publish`, `select`, `subscribe`, `unlink`, and `unsubscribe`). The log of denied commands has no entry for the scenario | `ValkeyAclTests` |
| Can the client handshake do without the `cluster nodes` and `config get` commands, the probe key, and the master-change channel? | Yes, with `CommandMap`, an empty `TieBreaker`, and an empty `ConfigurationChannel`. The scenario passes without those rules (spike 1.b, finding 4) | `ValkeyAclTests` |
| Does a refusal reach Valkey? | No. The loader sets a duration of zero and skips both writes, and no key exists after a refusal | `CacheOverValkeyTests` |
| Does a removal by tag delete the entries? | No. It writes a marker, and a node that has no memory entry loads again because the shared entry is older than the marker | `CacheOverValkeyTests` |
| Does the password leave the settings? | `ConfigurationOptions.ToString()` includes it in StackExchange.Redis 2.10.1, so no code prints the options. A connection to a closed port with a known password wrote it to no log entry and no exception, and no key or value held it | `CacheOverValkeyTests`, `ValkeyOptionsTests` |

## Limits

- The Linux run needs the Docker socket, which is the exception of [ADR-0034](../../adr/0034-integration-test-database-strategy-and-linux-run.md). The hosted `ci` workflow runs the suites directly, and no hosted run has happened for this branch yet.
- FusionCache computes the expiration of an entry from the wall clock, so no test waits for a time to live. `CatalogCacheTests` reads the expiration that the adapter set, with wide margins.
- Valkey 9.1 was not run in this work package. Spike 1.b ran it with identical results, and the image that the tests use is the 8.1 digest of `compose.yaml`.
- Timings are single samples from one machine.
