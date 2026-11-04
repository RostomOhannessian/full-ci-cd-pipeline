---
title: "Evidence record: the cache invalidation path across two hosts (2026-11-03)"
description: "A dated record of the second pull request of WP1.9 on Windows with Docker Desktop and in the pinned Linux SDK image: what ran where, the counts, what the tests showed about the backplane, the outbox fallback, the receipts, and a Valkey server that stops and returns, and what the runs did not show."
type: evidence-record
date: 2026-11-03
status: verified
audience: [maintainers, contributors]
last-verified: 2026-11-03
owner: "@RostomOhannessian"
---

# Evidence record: the cache invalidation path across two hosts (2026-11-03)

This record is the evidence for the second pull request of WP1.9 (Caching): the invalidation policy and the outbox handler `CacheInvalidationSubscriber`, the immediate in-process delivery of the dispatcher, and the tests that run two hosts against one SQL Server and one Valkey ([ADR-0039](../../adr/0039-cache-invalidation-by-immediate-dispatch-and-the-outbox.md)). It continues [the record of the adapter](2026-11-03-cache-adapter-run.md). It says what ran where, so a reader can tell which host proved which claim. It also gives the proof of the last item of the confirmation of [ADR-0012](../../adr/0012-domain-events-outbox-vs-direct-orchestration.md): cache invalidation converges when the outbox is the fallback path.

## What ran where

| Where | Command | What it ran | Result |
| --- | --- | --- | --- |
| Windows x86-64, Docker Desktop 29.9.2 (server), .NET SDK 10.0.403 | `dotnet test --solution ProductCatalog.slnx --configuration Release --no-build` | Every test project natively, with a SQL Server 2022 container and Valkey 8.1 containers started by the fixtures and by the outage tests | 1,843 tests ran, 1,843 passed, 0 failed, 0 skipped. The Infrastructure suite, which holds the new integration tests, took about 47 seconds. Smart App Control did not block a test assembly in this run, unlike some earlier runs of the same day ([the record of the adapter](2026-11-03-cache-adapter-run.md)) |
| Linux, the pinned SDK 10.0.401 image, Docker socket mounted | `docker compose -f tools/lint/compose.yaml run --rm linux-tests` | A scratch copy of the repository: locked restore, Release build with warnings as errors, every test project with the code coverage extension, and the coverage thresholds | 1,843 tests ran, 1,843 passed, 0 failed, 0 skipped, in 40.6 seconds. The coverage gate passed: Catalog.Application 99.54% of lines and 99.58% of branches, Catalog.Domain 99.63% of lines and 98.41% of branches, and the solution 98.22% of lines (2,099 of 2,137) |

## What the tests showed

| Question | Answer | Test |
| --- | --- | --- |
| Does a removal on one node reach the memory of another? | Yes. An observing backplane says when the other node applied the message, and the read after it loads the new data. The wait is on that signal and not on a pause | `CacheAcrossNodesTests` |
| What if the backplane does not reach a node? | The node keeps serving the entry in its memory after the removal, until the entry expires. The marker is in Valkey, but a node does not ask Valkey for an entry that its memory holds | `CacheAcrossNodesTests` |
| Does a write read its own write on the writing host, with the lists too? | Yes. The immediate removal ran before the answer, so the writer's next read of the product and of the list returned the new price. The reader took the new price after the backplane message | `CacheInvalidationConvergenceTests` |
| Does the outbox repeat the removal once? | Yes. After the immediate path removed the two tags, the processor delivered the message and the handler removed them again (four removals in all), the message is `Done`, and one receipt exists | `CacheInvalidationConvergenceTests` |
| What if the immediate removal never happens? | The processor of another host delivered the message, and the host that wrote and lost its dispatch converged through the backplane message of that removal. Both hosts served the old price before the pass and the new one after it | `CacheInvalidationConvergenceTests` (the proof of the last item of ADR-0012) |
| What if Valkey is down when the write commits? | The immediate delivery failed without failing the write, and one warning was logged. The writer read its own write from the database. The processor failed once and kept the message `Pending` with the error type name. After the server returned and both hosts subscribed again (Valkey counts the subscribers of the channel), the processor delivered the message, the other host converged, and the message is `Done` with more than one attempt | `CacheInvalidationConvergenceTests` |
| Does a message that is delivered again remove nothing twice? | Yes. A second handler failed once, so the message was delivered twice, and the cache handler ran only on the first delivery, because its receipt stopped the second. Each tag was removed once | `CacheInvalidationConvergenceTests` |
| What does the policy do with an event it does not know, or a payload of the wrong shape? | It refuses it, the outbox moves the message to the dead-letter state, and no tag is removed | `CacheInvalidationConvergenceTests` |
| What does the immediate delivery do? | It gives each message of a commit to every handler once, in the order of the rows, with an envelope equal to the row, writes no receipt, and wakes the processor last. A failing handler is counted and logged and never stops another. A run that did not commit delivers nothing. A cancellation ends the delivery and still wakes the processor | `OutboxImmediateDispatchTests` |
| What does a node do when Valkey stops? | Reads are served from memory or the loader, a write stays in memory, no call throws, the gauge `catalog.cache.shared.down` reads 1, and the breaker answers every later call at once. The first read after the stop stayed under the timeout of the shared cache plus a margin | `CacheOutageTests` |
| Does the backplane gauge open when Valkey stops? | No. A stopped server opens the breaker of the shared cache and leaves the backplane breaker closed. A server that refuses the channel while it accepts the keys opens the backplane breaker, makes the removal incomplete, and leaves the other node stale | `CacheOutageTests` |
| Does a node recover by itself? | Yes. After the server returned empty, a new entry reached the shared cache, the gauges returned to 0, and a removal was confirmed again, with no restart of the node. A node that started while Valkey was down served from the loader and joined the server when it appeared | `CacheOutageTests` |

## Limits

- FusionCache measures the expiration of a memory entry and the duration of a circuit breaker from the wall clock, so no test can fake them. The tests that need a breaker to close use a duration of 200 milliseconds and poll a condition of the system with a bound of 30 seconds, and the expiration of an entry is proven by reading the expiration that the adapter set ([the record of the adapter](2026-11-03-cache-adapter-run.md)), never by waiting for it.
- The outbox retries in the test of the outage move the fake clock past the backoff and run a pass until the message is `Done`. The number of passes depends on how fast the breaker and the connection recover, so the test asserts the end state and that there was more than one attempt, never the number of passes.
- A node that missed a backplane message while it was disconnected keeps its entry until it expires, and the outbox cannot reach it. The test of a backplane that refuses the message shows the staleness, and [the consistency model](../../explanation/cache-consistency-model.md) states the bound.
- Timings are single samples from one machine. Valkey 9.1 was not run in this work package, and the image that the tests use is the 8.1 digest of `compose.yaml`.
- No hosted run existed for this branch when this record was written. The `ci` workflow runs the same suites on Linux without the Docker socket exception.
