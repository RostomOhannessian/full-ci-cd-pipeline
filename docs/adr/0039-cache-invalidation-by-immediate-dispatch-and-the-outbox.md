---
title: "ADR-0039: Invalidate the cache by an immediate in-process delivery and an outbox handler that share one policy"
description: "Decide how a committed write removes the stale cache entries: the dispatcher gives each committed message to the handlers in process right after the commit, the outbox repeats it with a receipt, one policy maps each domain event to the tags to remove, and a removal that Valkey could not confirm is retried until it is."
status: accepted
date: 2026-11-03
decision-makers: ["@RostomOhannessian"]
supersedes: []
superseded-by: []
tools: [fusioncache, valkey, efcore, sql-server, testcontainers-dotnet]
related-adrs: ["0012", "0016", "0032", "0035", "0038"]
evidence:
  - src/Catalog.Application/Caching/CacheInvalidationPolicy.cs
  - src/Catalog.Application/Caching/CacheInvalidationSubscriber.cs
  - src/Catalog.Infrastructure/Outbox/OutboxDispatcher.cs
  - tests/Catalog.Application.Tests/CacheInvalidationTests.cs
  - tests/Catalog.Infrastructure.Tests/OutboxImmediateDispatchTests.cs
  - tests/Catalog.Infrastructure.Tests/CacheInvalidationConvergenceTests.cs
  - tests/Catalog.Infrastructure.Tests/CacheAcrossNodesTests.cs
  - tests/Catalog.Infrastructure.Tests/CacheOutageTests.cs
  - docs/testing/evidence/2026-11-03-cache-invalidation-run.md
  - docs/explanation/cache-consistency-model.md
---

# ADR-0039: Invalidate the cache by an immediate in-process delivery and an outbox handler that share one policy

## Context and problem statement

Plan section 8.5 gives the write path of the cache: commit, remove the affected keys and tags synchronously on the writing node (the other nodes learn through the backplane), and let the outbox handler repeat the invalidation to guarantee it. Plan section 8.4 gives the outbox an immediate dispatch, which publishes in process after the commit on a best-effort basis. [ADR-0012](0012-domain-events-outbox-vs-direct-orchestration.md) accepted the outbox on the condition that WP1.9 proves that cache invalidation still converges when the outbox is the fallback path, and [ADR-0038](0038-catalog-cache-adapter-and-valkey-connection.md) left the decision of this part to the second pull request of WP1.9.

Three things were open:

- **Who performs the immediate removal.** The port `IOutboxDispatcher` of WP1.8 only woke the processor. The transaction decorator calls it after every successful commit and ignores its failures, but nothing gave it the messages that the commit wrote.
- **Which tags an event makes stale.** The queries attach tags (`product:{id}`, `products:list`, and `category:{id}`), and nothing said which event removes which.
- **How the fallback is proven.** The claim that the outbox converges the cache needs a test in which the immediate removal is lost, or Valkey is down, and the hosts still end with the new data.

This ADR serves REQ-CACHE-001, REQ-CACHE-002, and REQ-CACHE-003, and it amends [ADR-0035](0035-outbox-processor-design.md), whose dispatcher only woke the processor.

Facts that shape the decision (checked 2026-11-03):

- `IOutboxMessageHandler` is the port of the handlers, and the processor of WP1.8 gives each message to every handler that has no receipt, writes a receipt after each handler, and retries a message whose handler threw with an exponential backoff, until it moves to the dead-letter state.
- FusionCache's `RemoveByTag` writes a marker with the current timestamp, and an entry that is older than the marker is dropped when it is read. A second removal makes the entries that were loaded in between stale again.
- When the shared cache fails, the circuit breaker of the shared cache opens and the breaker of the backplane stays closed (measured in `CacheOutageTests`, checked 2026-11-03). The backplane breaker opens when a publish itself fails, for example when the server refuses the channel, and the test of that case shows the gauge.
- A node whose subscription to the backplane is down when a message is published never receives it. The shared cache holds the marker, but a node that holds the entry in memory does not read the shared cache until that entry expires.

## Decision drivers

- A write reads its own writes: the response that follows a commit never shows the old data from the cache of the writing node.
- A removal that fails or is lost never stays lost: another host finishes it, with a backoff, until Valkey confirms it.
- One place says which entries an event makes stale, so the two paths cannot disagree and a new event cannot ship without a rule.
- The code of the Application knows the ports and nothing else, and the Api composes (plan section 8.1, ADR-0004).
- The tests are deterministic: no sleep makes a test pass, and the clock of the outbox is fake.

## Considered options

1. The dispatcher of the scope delivers the committed messages to every handler in process, the processor delivers them again with receipts, and one policy in the Application maps each event to its tags (chosen).
2. A decorator that removes the tags of the command directly after the commit, and an outbox handler for the guarantee.
3. The outbox alone, with no immediate removal.
4. Write-through: a command updates the entries of the cache instead of removing them.
5. A second, separate mechanism for the immediate path, such as a notification table or a distributed lock.

## Decision outcome

Chosen option: **one policy, one subscriber, and a dispatcher that delivers in process and then wakes the processor**, because the immediate path and the guarantee call the same code over the same messages, so a test of one is a test of both, and the layers of plan section 8.1 stay as they are.

### The policy

`CacheInvalidationPolicy` in `Catalog.Application.Caching` maps the message of an event to the tags to remove, in order. `CacheTags` builds the tags, and the queries build theirs from it too, so a reader and a writer cannot disagree about a name.

| Event type | Tags removed |
| --- | --- |
| `ProductCreated`, `ProductDetailsChanged`, `ProductPriceChanged`, `ProductCategoryChanged`, `ProductDiscontinued` | `product:{productId}`, then `products:list` |
| `CategoryCreated`, `CategoryRenamed`, `CategoryDiscontinued` | `category:{categoryId}` |

- Every list carries the tag `products:list`, so one removal covers a list with any filter. A list that a category narrows carries `category:{id}` too, and the events of a category remove it. The finer tag cannot replace `products:list` for a product event, because an unfiltered list has no tag of its category, so a product event removes `products:list`.
- An event type that the table does not know, a payload that is not in the shape of its event, and a version of the shape other than 1 are refused with an `InvalidOperationException` whose text names the event type and the message and never the payload. The outbox retries and then dead-letters such a message, which is a metric and an alert. A silent skip would leave a stale entry in the cache without a signal.
- A test fails when the Domain gets an event type that the policy lacks, and another one fails when a tag that a cached query attaches is not removed by the events that change its data.

### The subscriber

`CacheInvalidationSubscriber` implements `IOutboxMessageHandler` under the stable name `catalog-cache-invalidation`, which is the key of its receipt. It is in the Application, because it names only ports. It removes each tag of the policy through `ICatalogCache`, tries every tag even when one is not confirmed (so this node and the shared cache lose all the entries), and then throws one `CacheInvalidationIncompleteException` that names the number of tags and the message, so the outbox tries the whole message again. Any other fault, and a cancellation, reach the caller at once.
The subscriber is idempotent. A second removal of a tag only makes the entries that were loaded in between stale again, and the processor writes a receipt after the first success, so a message that is delivered again does not run it.

### The immediate delivery

The dispatcher changes from a signal to a delivery (this amends [ADR-0035](0035-outbox-processor-design.md), section "The wake-up").

- After a save, the outbox interceptor keeps the envelope of each row that it wrote on the write context of the scope. A run that is refused, that fails, or whose commit fails discards them in `EfUnitOfWork`, so nothing is delivered for a transaction that rolled back.
- `OutboxDispatcher` is a scoped service that takes the write context of its scope, the handlers, the signal, the metrics, and the log. `DispatchCommittedAsync` takes the envelopes and gives each one to every handler in process, and then raises the signal of the processor, also when a handler threw or the call was cancelled. A second call delivers nothing.
- The immediate delivery is best-effort. A handler that throws is logged and counted in `catalog.outbox.immediate` (tags `handler` and `outcome`) and never fails the use case, and the message stays in the outbox. A cancellation of the request ends the delivery and still wakes the processor.
- The immediate delivery writes no receipt. The processor then delivers the same message to every handler with receipts, so a handler runs at least twice under normal operation. For the cache this is the second delete: a reader that loaded the old row before the commit and wrote it to the cache after the first removal is made stale again by the second, within the poll interval of the processor. The transaction decorator already calls the dispatcher after each commit and ignores its failures, so it did not change.

### The fallback and the retry

When the immediate removal is lost, because the host died after the commit or the call never ran, the next pass of any host leases the message and runs the subscriber, which removes the tags and publishes, and the other hosts converge through the backplane. When Valkey is down, the subscriber throws, the processor schedules the next attempt with its backoff, and the attempts continue until Valkey confirms the removal. The tests run both with two hosts that share one SQL Server database and one Valkey server.

### What the model guarantees

[The consistency model](../explanation/cache-consistency-model.md) states it. In short: the writing node reads its own writes, because the removal happens before the response and the memory of the node loses the entry even when Valkey is down. The other nodes are stale for the backplane latency while it works, and for at most the memory time to live (30 seconds, 10 for a list) when it does not. A removal that Valkey could not confirm is repeated by the outbox until it can, and a node that missed the message while it was disconnected is bounded by the same memory time to live, because the repeat does not reach a node that already holds the entry in memory.

### Consequences

- Good: the writing node reads its own writes, even while Valkey is down, and the other nodes converge through the backplane within the time that a pub/sub message takes.
- Good: a lost or failed removal is finished by the outbox, with a backoff and a receipt, which closes the last item of the confirmation of ADR-0012.
- Good: the policy and the subscriber are in the Application and need no cache, database, or clock to test, and the integration tests run the whole path with two hosts.
- Good: a new event type cannot ship without a rule, because a test and the dead-letter state both refuse it.
- Bad: every handler runs at least twice for a message, once in process and once through the outbox, so a handler that is expensive or not idempotent needs an opt-in for the immediate delivery. Mitigation: the contract of `IOutboxMessageHandler` already demands idempotence, the cache handler is cheap, and the first handler that needs an opt-in changes the port in its own ADR.
- Bad: the immediate delivery runs in the request, so the response waits for the removal, which costs the awaited call to Valkey (500 milliseconds at most per tag, then the breaker answers at once). Mitigation: the timeout and the breaker bound it, and plan section 8.5 asks for an awaited removal.
- Bad: a node that is disconnected from the backplane when the message is published keeps the entry in memory until it expires, and the outbox cannot reach it. Mitigation: the bound is the memory time to live, the gauge `catalog.cache.backplane.down` reports it while the breaker is open, and the consistency model says so.
- Bad: FusionCache compares the timestamp of an entry with the timestamp of the marker, and each is the wall clock of the node that wrote it, so the clocks of the nodes must agree. Mitigation: the nodes of the platform synchronize their clocks, and a skew only makes entries reload sooner or later by the size of the skew.
- Neutral: the dispatcher is scoped now, so the composition root registers it as a scoped service, and `TestComposition` shows the registrations.

### Confirmation

- `CacheInvalidationTests` (no Docker) prove the table, its completeness against the Domain, the refusals, and the subscriber, including the attempt of every tag and the unchanged faults.
- `OutboxImmediateDispatchTests` (SQL Server) prove the delivery, the order, the envelope that equals the row, the best-effort failure with its metric and log, the delivery that the processor repeats with receipts, the rollback that delivers nothing, the isolation of scopes, and the cancellation.
- `CacheInvalidationConvergenceTests` (SQL Server and Valkey, two hosts) prove the write path, the fallback when the immediate removal never happens, the retry across a Valkey outage, the receipt that prevents a second removal, and the dead-letter state for a message that the policy refuses.
- `CacheAcrossNodesTests` and `CacheOutageTests` (Valkey) prove the backplane between two nodes, the staleness without it, the degradation and the recovery when Valkey stops and returns, and a backplane that refuses the message while the shared cache works.
- The suites ran on Windows and in the Linux SDK image of [ADR-0034](0034-integration-test-database-strategy-and-linux-run.md), and [the evidence record](../testing/evidence/2026-11-03-cache-invalidation-run.md) says which host ran what.

## Pros and cons of the options

### One policy, one subscriber, and a dispatcher that delivers in process and then wakes the processor

- Good: one code path for both guarantees, so a test of the fallback tests the immediate path too.
- Good: no new port, and the layers stay as plan section 8.1 draws them.
- Bad: the dispatcher depends on the write context of the scope, which makes it scoped.

### A decorator that removes the tags of the command, plus an outbox handler

- Good: no staging of messages on the context.
- Bad: two tables would say which entries a write makes stale, one in the commands and one in the handler, and they would drift. A command also does not know every event that its aggregate raised, and the outbox handler would have to repeat the knowledge of the decorator.

### The outbox alone

- Good: the simplest design, with one path.
- Bad: the writing node would show the old data until the next pass of the processor, which breaks the guarantee that the writer reads its own writes, and the poll interval of 5 seconds is far above the latency that plan section 8.5 asks for.

### Write-through

- Good: no miss after a write.
- Bad: a command would have to know every cached shape, including every list that the new data belongs to, and a mistake would put wrong data in the cache and not only an empty slot.

### A notification table or a distributed lock

- Good: a second path that does not depend on Valkey.
- Bad: the outbox is already that table, and a lock across nodes costs a failure mode (plan section 8.5 rules out a distributed lock).

## Revisit when

- A handler that is expensive or not idempotent joins the outbox, which calls for an opt-in to the immediate delivery.
- The staleness of the nodes that miss a backplane message exceeds the budget of the service level objective, which calls for a shorter memory time to live, a version stamp that the node checks, or the auto-recovery of FusionCache.
- A product event touches a category-specific entry that `products:list` does not cover, which calls for finer tags.
- FusionCache changes how it compares the timestamps of a marker and an entry.

## Further reading

- Plan sections 8.4 and 8.5: [../plans/implementation-plan.md](../plans/implementation-plan.md)
- ADR-0012: [0012-domain-events-outbox-vs-direct-orchestration.md](0012-domain-events-outbox-vs-direct-orchestration.md)
- ADR-0035: [0035-outbox-processor-design.md](0035-outbox-processor-design.md)
- ADR-0038: [0038-catalog-cache-adapter-and-valkey-connection.md](0038-catalog-cache-adapter-and-valkey-connection.md)
- The consistency model: [../explanation/cache-consistency-model.md](../explanation/cache-consistency-model.md)
- Tagging in FusionCache: [https://github.com/ZiggyCreatures/FusionCache/blob/v2.9.0/docs/Tagging.md](https://github.com/ZiggyCreatures/FusionCache/blob/v2.9.0/docs/Tagging.md) (checked 2026-11-03)
