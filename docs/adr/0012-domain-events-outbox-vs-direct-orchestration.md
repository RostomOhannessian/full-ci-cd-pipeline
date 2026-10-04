---
title: "ADR-0012: Use a transactional outbox for domain events"
description: "Record domain events and persist them with a transactional outbox so post-commit work is reliable without adding a full message broker."
status: proposed
date: 2026-10-04
decision-makers: ["@RostomOhannessian"]
accepted-in: "WP1.8"
supersedes: []
superseded-by: []
tools:
  - efcore
  - sql-server
  - masstransit
  - mediatr
related-adrs: ["0016"]
evidence:
  - docs/research/2026-10-04-dotnet-ecosystem-verification.md
---

# ADR-0012: Use a transactional outbox for domain events

## Context and problem statement

Phase 1 needs aggregates that raise domain events, handlers that invalidate cache and trigger follow-on work, and a persistence model that stays correct when the process crashes at awkward times. Plan sections 8.3, 8.4, and 8.5 define the target behavior, and WP1.8 and WP1.9 are the work packages that must prove it.

The repository does not start with a message broker. It starts with one API, one SQL Server database, and a requirement to stay understandable for learners. At the same time, the example endpoint must combine CQRS, caching, and domain events, so "just call everything inline" is not good enough.

The failure cases are the real reason for an ADR:

- If the command commits and the process dies before side effects run, users can see committed state but miss cache invalidation or follow-on work.
- If retries happen, handlers can run more than once.
- If one handler is broken, the request path should not keep failing forever.
- If credentials rotate during delivery, the system still needs a recovery path.

ADR-0004 also matters here. The .NET ecosystem verification record confirms that MediatR 13+ and MassTransit 9+ moved to commercial licensing models, which makes "just add a mediator" or "just add a broker stack" less attractive for this repository (checked 2026-10-04).

## Decision drivers

- Never lose a domain event after the main state change commits.
- Keep the initial implementation inside SQL Server and EF Core.
- Tolerate duplicate delivery through idempotent handlers.
- Keep request latency low for the common path without sacrificing correctness.
- Avoid unnecessary licensed or operational footprint in Phase 1.

## Considered options

1. Transactional outbox with best-effort immediate dispatch after commit.
2. Direct orchestration inside the command handler.
3. In-process events dispatched before commit through a mediator.
4. In-process events dispatched after commit without an outbox.
5. Change data capture or a brokered event stack.

## Decision outcome

Chosen option: **transactional outbox with best-effort immediate dispatch after commit**, because it is the smallest design here that closes the crash-after-commit gap without adding a broker stack to Phase 1.

The repository design is:

- Aggregates record domain events in memory during command handling.
- A `SaveChangesInterceptor` writes those events into an outbox table inside the same EF Core transaction as the state change.
- A background processor leases batches, retries with exponential backoff and jitter, and moves poison messages to a dead-letter state after N attempts.
- Handlers are idempotent and store receipts.
- The writing node also dispatches best-effort immediately after commit for low latency, but the outbox remains the correctness guarantee.

One small sketch captures the boundary:

```csharp
// sketch only
await dbContext.SaveChangesAsync(cancellationToken); // entity state + outbox rows
await immediateDispatcher.TryDispatchAsync(recordedEvents, cancellationToken); // best effort
```

The crash behavior is now explicit:

- Crash after commit, before dispatch: the background processor still sees the outbox row.
- Duplicate delivery: receipts make handlers idempotent.
- Poison message: retries end in dead-letter plus metric and alert.
- Handler down or dependency unavailable: the message stays retriable instead of being silently lost.

### Consequences

- Good: the repository gets reliable post-commit work without a broker or a second storage technology in Phase 1.
- Good: the model is teachable because every moving part is visible in SQL tables, EF Core interception, and one background processor.
- Good: the best-effort immediate dispatch path can keep common-path latency low while preserving eventual completion.
- Bad: at-least-once delivery pushes complexity into idempotency and receipts. Mitigation: make those tests mandatory in WP1.8.
- Bad: the outbox adds tables, cleanup, and operational metrics. Mitigation: define them in plan section 8.4 from the start instead of treating them as incidental complexity.
- Neutral: this does not make the application distributed. It is still one process boundary with a durable handoff inside one database.

### Confirmation

The decision is accepted only if WP1.8 and WP1.9 prove the failure handling:

- **WP1.8** tests a crash between commit and dispatch.
- **WP1.8** tests duplicate delivery and confirms idempotent handlers with receipts.
- **WP1.8** tests a poison message and confirms dead-letter behavior plus metrics.
- **WP1.8** tests credential rotation during delivery.
- **WP1.9** proves cache invalidation still converges correctly when outbox delivery is the fallback path.

## Pros and cons of the options

### Transactional outbox with best-effort immediate dispatch after commit

- Good: closes the most important reliability gap while keeping the stack small.
- Good: uses tools the repository already needs: EF Core and SQL Server.
- Good: keeps the handler pipeline explicit and testable.
- Bad: adds outbox, receipts, retry policy, and cleanup mechanics.
- Bad: handlers must be written to tolerate at-least-once delivery.

### Direct orchestration inside the command handler

- Good: simplest control flow to read on day one.
- Good: no background processor or extra tables.
- Bad: a crash after commit but before side effects completes loses work.
- Bad: request latency couples the write path to every side effect.

### In-process events dispatched before commit through a mediator

- Good: keeps the handler readable and decouples some follow-on work.
- Good: no separate polling component.
- Bad: work can run before the transaction commits, which exposes inconsistent behavior on failure.
- Bad: the repository would also lean toward a mediator package whose newer commercial license is already a concern.

### In-process events dispatched after commit without an outbox

- Good: lower footprint than a transactional outbox.
- Good: reasonable for non-critical best-effort notifications.
- Bad: events are lost if the process dies after commit and before dispatch.
- Bad: there is no durable retry record to inspect or recover.

### Change data capture or a brokered event stack

- Good: stronger integration path if the system later becomes multi-service.
- Good: can scale beyond one process and one database.
- Bad: too much operational and conceptual footprint for Phase 1.
- Bad: a broker library path would reopen the commercial-license issue around the MassTransit 9+ line.

## Revisit when

- The application crosses a process boundary that needs a real broker or CDC feed.
- WP1.8 cannot prove reliable crash recovery or idempotent handling.
- The outbox backlog or latency becomes a measured bottleneck under the project's workload.
- A future ADR introduces cross-service messaging that changes the durability boundary.

## Further reading

- .NET ecosystem verification: [../research/2026-10-04-dotnet-ecosystem-verification.md](../research/2026-10-04-dotnet-ecosystem-verification.md) (checked 2026-10-04)
- ADR-0016 cache design: [./0016-cache-implementation-fusioncache.md](./0016-cache-implementation-fusioncache.md) (checked 2026-10-04)
- ADR-0004 license policy: [./0004-licensing-and-dependency-license-policy.md](./0004-licensing-and-dependency-license-policy.md) (checked 2026-10-04)
