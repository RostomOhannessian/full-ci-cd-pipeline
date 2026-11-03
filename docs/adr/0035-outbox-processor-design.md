---
title: "ADR-0035: Deliver the outbox with a leased, fenced, at-least-once processor that records a receipt for each handler"
description: "Decide how the outbox processor leases messages, how it keeps two hosts from delivering one message at once, how it retries and dead-letters, what it records as proof of delivery, how a commit wakes it, and what shape a stored event payload has."
status: accepted
date: 2026-11-02
decision-makers: ["@RostomOhannessian"]
supersedes: []
superseded-by: []
tools: [efcore, sqlclient, sql-server, microsoft-extensions-hosting-abstractions]
related-adrs: ["0012", "0033", "0034"]
evidence:
  - src/Catalog.Infrastructure/Outbox
  - tests/Catalog.Infrastructure.Tests/OutboxProcessorTests.cs
  - tests/Catalog.Infrastructure.Tests/OutboxPayloadTests.cs
  - docs/testing/evidence/2026-11-02-outbox-idempotency-run.md
  - docs/reference/persistence.md
---

# ADR-0035: Deliver the outbox with a leased, fenced, at-least-once processor that records a receipt for each handler

## Context and problem statement

[ADR-0012](0012-domain-events-outbox-vs-direct-orchestration.md) chose a transactional outbox, and plan section 8.4 says what the processor must do: it is a `BackgroundService` that delivers at least once, leases batches with `READPAST, UPDLOCK, ROWLOCK` semantics, retries with exponential backoff and jitter, moves a message to a dead-letter state after N attempts with a metric, and relies on idempotent handlers that record a receipt. Ordering is guaranteed only within a single message. After a commit the writing node dispatches in process on a best-effort basis.

Those sentences leave the hard parts open. What happens when a host dies halfway through a batch? How does a host that was slow, not dead, avoid overwriting the work of the host that took over? When is an attempt counted? What does "a receipt" prove, and when is it written? What does a stored payload look like, so that a message written today can be read after the code changes? This ADR answers those questions. It serves REQ-DATA-002, REQ-DATA-003, and REQ-DOM-004.

## Decision drivers

- No message is lost after the commit of its aggregate, and none is delivered by two hosts at the same time.
- A crash, a stop, or a slow handler at any point leaves a state that the next pass can continue from.
- A broken handler never blocks the messages behind it, and never retries forever.
- The tests are deterministic: a fake clock ends leases and retry delays, and nothing sleeps (plan section 10.3).
- A stored payload is a contract with the rows that already exist.
- No broker, no mediator, no library that needs a commercial license (ADR-0004).

## Considered options

1. A lease statement with lock hints, a fence on the lease identifier, and receipts for each handler (chosen).
2. A transaction that holds the row locks while the handlers run.
3. A lock table or an application lock (`sp_getapplock`) around the whole pass.
4. A single status column without a lease, with a timeout on `Processing` rows.
5. A broker or a library for outbox processing.

## Decision outcome

Chosen option: **a lease statement with lock hints, a fence on the lease identifier, and a receipt for each handler**, because it keeps the locks short, lets any number of hosts run, and makes every failure a state that the next pass reads from the table.

### The lease

One statement leases a batch. A common table expression takes the first rows that are `Pending`, due (`NextAttemptAt` is not in the future), and not held by a lease that is still running, with `WITH (ROWLOCK, UPDLOCK, READPAST)`, ordered by `NextAttemptAt` and `Id`. The `UPDATE` of the expression sets `LeasedUntil` to the time of the pass plus the lease duration, sets `LeaseId` to a new identifier, adds one to `Attempts`, and returns the rows with `OUTPUT`. The statement is one transaction that commits on its own, so the locks last for the length of one statement.

- `READPAST` makes a host skip a row that another host holds, so two hosts take disjoint rows and neither waits. A test holds an `UPDLOCK` on a row in another transaction and shows that the lease returns the other rows at once.
- A lease that ends (`LeasedUntil` is in the past) makes the row due again. That is the whole recovery story for a dead host: nothing needs to notice it.
- **An attempt is counted when it is leased, not when it fails.** A host that dies in the handler never writes a failure, so counting at the lease is the only way that a message that kills its host reaches the dead-letter state and stops.
- The default batch is 20 rows, the default lease is 2 minutes, and the lease must be longer than the slowest handler. The validator checks the bounds at startup.

### The fence

Completing or failing a message is an `UPDATE ... WHERE Id = @id AND LeaseId = @lease`. A host whose lease ended and whose rows were leased again by another host changes zero rows, logs that the lease was lost, and counts nothing. So a slow host can never mark done, reschedule, or dead-letter a message that another host now holds. The lease identifier is the fence, and a test runs exactly this case.

### Receipts

A handler is idempotent and has a stable name. After a handler returns, the processor inserts a row `(MessageId, Handler)` into `OutboxReceipts`. Before it runs a handler it reads the receipts of the message and skips the handlers that have one. Two things follow:

- A message with two handlers, where the second one fails, retries only the second one.
- A host that lost its lease after a handler finished has already written the receipt, so the host that takes over does not run that handler again. The receipt is not an exactly-once guarantee: a crash between the end of the handler and the insert of the receipt delivers the handler a second time, which is why the handler must be idempotent. The test of the stop after delivery shows this.

A message with no registered handler completes, so a deployment that has not registered a handler yet does not pile up work.

### Retries and the dead-letter state

A handler that throws fails the delivery of that message only. The message goes back to `Pending` with `NextAttemptAt` set to the time of the failure plus `BaseBackoff * 2^(attempt - 1)`, capped at `MaxBackoff` and spread by a jitter of up to 20 percent in both directions, so two hosts do not retry in step. The defaults are 2 seconds, 5 minutes, and 8 attempts. When the attempt count reaches `MaxAttempts` the status becomes `DeadLetter`, the processor increments the counter `catalog.outbox.dead_lettered` on the meter `Catalog.Outbox` and writes an error to the log, and no pass leases the message again. A person decides what to do with it. The alert that watches the counter is planned for WP1.11, and until then the counter and the error log are the signal. The cleanup never deletes a dead-letter message.

The failure is recorded as the name of the exception type in `LastError`, never as its message, because the message of a handler fault can hold a value that must not reach a table or a log line of the processor.

### Stop and cancellation

A pass that is cancelled stops at once and leaves the lease in place for the message in progress and for the rest of the batch. They are delivered again when the lease ends, which is at most one lease duration later. A graceful stop of the host therefore delays at most one lease and loses nothing.

### The wake-up

After a commit the dispatcher (`IOutboxDispatcher`) raises a signal, and the background service, which waits on the signal with a timeout of the poll interval (5 seconds by default), runs a pass. A notification that arrives while no pass waits is kept, and many notifications count as one, because one pass leases a whole batch. The wake-up is an optimization: the poll finds every due message even when no signal ever arrives, and a pass that leased a full batch runs the next pass at once. The loop survives a fault of a pass (the database is down, for example): it logs the fault and waits for the next signal or poll.

### The cleanup

A job in the same loop deletes the finished messages that are older than the retention (7 days) together with their receipts (a cascading delete), and the idempotency records that expired ([ADR-0036](0036-idempotency-store.md)). It deletes in batches of 1,000 rows, so a large backlog never holds a long lock.

### The payload

The interceptor of the unit of work writes one row for each recorded domain event in the same transaction as the aggregate change. The payload is JSON that a function writes by hand, with plain values and camel-case names, and it always starts with `eventType`, `schemaVersion`, and `occurredAt`. It is not the serialization of the record, so a rename of a property of the Domain never changes a stored message. A snapshot test holds the full text of each of the eight events, and another test fails when the Domain has an event type without a snapshot, and `OutboxPayloads` refuses an event that it has no shape for, so a new event cannot reach the outbox untested. A change of a shape is the moment to raise the `SchemaVersion` of the event and to write the upgrade for the rows that exist. The snapshots are strings in the test, not files that a snapshot library writes, so the change needs no new dependency family.

### Consequences

- Good: any number of hosts can run the processor, a crash or a stop at any point is recovered by the clock, and a slow host cannot overwrite the host that took over.
- Good: the failure modes are states in a table, so every one of them has a test that moves a fake clock.
- Good: a message that kills its host reaches the dead-letter state, because attempts are counted at the lease.
- Bad: a message whose host died waits for the lease to end (2 minutes by default) before another host delivers it. Mitigation: the lease duration is a setting, and it must stay above the slowest handler.
- Bad: delivery is at least once, so a handler runs again after a crash between its end and its receipt. Mitigation: handlers are idempotent by contract, the receipt keeps the common case to once, and the cache invalidation of WP1.9 is idempotent by nature.
- Bad: the order of messages is not guaranteed across a batch boundary or a retry. This is what plan section 8.4 states, and a handler that needs an order must carry the information in the message.
- Bad: raw SQL for the lease and the finish, which EF Core cannot express. Mitigation: every value is a parameter (an interpolated `FromSql` call), the statements are covered by the suites of the real database, and the threat model lists the boundary.
- Neutral: the processor writes through its own short-lived contexts, so it holds no change tracker between passes.

### Confirmation

- `OutboxProcessorTests` cover delivery in order, receipts, the backoff arithmetic, the dead-letter state with the metric and the error log, the stop after delivery, the death of a processor, the fence, concurrent leases of four processors, the skip of a locked row, the cancellation of a batch, and the cleanup.
- `OutboxBackgroundServiceTests` cover the wake-up, the next pass after a full batch, the survival of a fault, and the stop. `OutboxSignalTests` cover the signal, and `OutboxBackoffTests` and `PersistenceOptionsTests` cover the pure parts.
- `OutboxPayloadTests` hold the eight snapshots and the completeness check, and `OutboxInterceptorTests` prove that a rollback leaves no row.
- The suites ran on Windows and in the Linux service of [ADR-0034](0034-integration-test-database-strategy-and-linux-run.md), and the evidence records say so.

## Pros and cons of the options

### A lease statement with lock hints, a fence, and receipts

- Good: short locks, no waiting, recovery by the clock, and a proof of delivery for each handler.
- Bad: raw SQL, and a lease duration to tune.

### A transaction that holds the locks while the handlers run

- Good: no lease, no fence, and a crash releases the locks.
- Bad: a transaction that is open for the length of the handlers holds locks and a log, and a handler that waits on a slow service blocks the table. It also hides a stuck host from every other host.

### An application lock around the whole pass

- Good: simple to reason about.
- Bad: one processor at a time for the whole cluster, so the throughput of a single host is the ceiling, and a host that dies inside the pass needs the session timeout of the connection.

### A status column without a lease

- Good: the smallest schema.
- Bad: a row stays `Processing` after its host dies, and a timeout on that status is a lease without the fence.

### A broker or a library

- Good: less code.
- Bad: the commercial terms of the likely candidates (ADR-0004, ADR-0012), and a runtime that this repository would have to teach instead of the pattern.

## Revisit when

- A handler needs an order across messages, which calls for a partition key or a stream.
- The backlog or the delivery latency exceeds the budgets of plan section 8.11 (the outbox age p95 under 5 seconds), which calls for a larger batch, several loops for each host, or a broker.
- The dead-letter alert of WP1.11 shows that a person cannot keep up, which calls for a replay tool.

## Further reading

- Plan section 8.4: [../plans/implementation-plan.md](../plans/implementation-plan.md)
- ADR-0012: [0012-domain-events-outbox-vs-direct-orchestration.md](0012-domain-events-outbox-vs-direct-orchestration.md)
- Table hints, `READPAST` and `UPDLOCK`: [https://learn.microsoft.com/sql/t-sql/queries/hints-transact-sql-table](https://learn.microsoft.com/sql/t-sql/queries/hints-transact-sql-table) (checked 2026-11-02)
- The transactional outbox pattern: [https://microservices.io/patterns/data/transactional-outbox.html](https://microservices.io/patterns/data/transactional-outbox.html) (checked 2026-11-02)
