---
title: "ADR-0036: Reserve idempotency keys outside the transaction, record the first result inside it, and compare keys exactly"
description: "Decide how the idempotency store reserves a key, tells a replay from a reused key and from a request that is still running, writes the first result in the transaction of the command, recovers from a request that died, and compares keys and callers."
status: accepted
date: 2026-11-02
decision-makers: ["@RostomOhannessian"]
supersedes: []
superseded-by: []
tools: [efcore, sqlclient, sql-server]
related-adrs: ["0012", "0028", "0032", "0033", "0035"]
evidence:
  - src/Catalog.Infrastructure/Idempotency
  - tests/Catalog.Infrastructure.Tests/IdempotencyStoreTests.cs
  - src/Catalog.Infrastructure/Persistence/Migrations
  - docs/testing/evidence/2026-11-02-outbox-idempotency-run.md
  - docs/reference/persistence.md
---

# ADR-0036: Reserve idempotency keys outside the transaction, record the first result inside it, and compare keys exactly

## Context and problem statement

Plan section 8.4 says that a `POST` that creates a resource carries an `Idempotency-Key`, and that each record stores the key, the caller (`sub` or `azp`), a request hash, the status, and a response snapshot, and expires after 24 hours. The record is written in the same transaction as the effect of the command. A key that comes back with a different payload gets 422, and a key that is in use by a request that is still running gets 409. A cleanup job purges expired records. [ADR-0028](0028-http-api-conventions-of-v1.md) fixed the header and the key format, and [ADR-0032](0032-decorator-pipeline-and-application-results.md) put the decorator in the pipeline and defined the port `IIdempotencyStore` with three operations: begin, complete, and release.

The port leaves the store's behavior open, and four situations need a decision:

1. Two requests with the same key arrive together. Exactly one may run the command.
2. The first request dies after it reserved the key. The key must not stay locked for 24 hours.
3. A request writes its effect, and the record of its result must exist exactly when the effect does.
4. Two keys that differ only in case, or two callers that differ only in case, must stay two keys.

This ADR serves REQ-DATA-004 and the idempotency parts of REQ-DATA-002.

## Decision drivers

- At most one committed effect for one key, even when the first request is slow and a second one takes over.
- The answer 409 for a request that runs, which means that the second request must not wait for the first.
- No record of a result without the effect, and no effect without the record.
- A crash never locks a key for the whole retention.
- The key is an opaque string that the caller chooses.

## Considered options

1. Reserve the key with an insert outside the transaction, record the result inside the transaction of the command, and take over a reservation that is older than a timeout (chosen).
2. Insert the record inside the transaction of the command only.
3. A distributed lock in Valkey around the command, and the record written afterward.
4. Cache the response in a middleware and keep no record of the request.

## Decision outcome

Chosen option: **reserve outside, record inside, take over after a timeout, and compare exactly**, because it is the only option that gives the answer 409 without a wait and still ties the result to the effect.

### The three operations

- **Begin** runs outside the transaction of the command, as single statements that commit on their own. It deletes the record of the same key and caller if that record expired, then inserts a row with the status `InProgress`, the fingerprint of the request, the time, and the expiry (24 hours later). The primary key is `(CallerId, Key)`, so of two inserts that arrive together exactly one succeeds, and the other gets the unique violation (2627) and reads the record. The decision is then, in this order:
  1. The fingerprint differs: `Reused` (the API answers 422).
  2. The record is `Completed`: `Replay`, with the stored response.
  3. The record is `InProgress` and younger than the timeout (2 minutes): `InProgress` (409).
  4. The record is `InProgress` and older: the reservation is taken over, with an `UPDATE` that names the old creation time (compare-and-swap), so of two requests that try it, one wins.
- **Complete** runs inside the transaction of the command. The decorator registers it with `IUnitOfWork.BeforeCommit`, so the unit of work runs it after the changes were saved and before the commit. It updates the record to `Completed` with the response, but only when the record is still `InProgress` with the same fingerprint. **If it changes no row, it throws**, which rolls the effect back: the reservation is gone (another request took it over, or the cleanup deleted it), and committing the effect without a record would let a retry run the command again. A test shows that a rollback after Complete removes the record's update, the effect, and the outbox rows together.
- **Release** runs outside the transaction and deletes the record only when it is still `InProgress` with the same fingerprint, so a request that failed lets the caller send the same request again with the same key. It never deletes a finished record, and it never deletes the reservation of a request with another fingerprint.

All statements are parameterized and go through the connection of the scope without the change tracker, so the store never saves the pending changes of the aggregates, and none of its values reaches the SQL text.

### A request that died, and a slow one

A request that dies after Begin leaves an `InProgress` record. After 2 minutes (`InProgressTimeout`) the next request with that key takes the reservation over. If the first request was only slow and finishes later, both requests now think they hold the key. The conditional update of Complete decides: the first one to complete wins and commits its effect, and the other one throws and rolls its effect back. The result is **at most one committed effect for one key**, and the loser gets an error that a retry turns into a replay. The test of the takeover shows exactly this. The timeout is longer than any request that the API accepts, so the case needs a request that outlives its own timeout.

### Comparing exactly

The key is an opaque, case-sensitive string of printable ASCII (ADR-0028), and the caller identifier is the `sub` or `azp` claim, which is case-sensitive too. The default collation of SQL Server is case-insensitive, so with it the keys `Abc` and `abc` of one caller would be one row, and a second `POST` with another key and the same payload would get the replay of the first one instead of a new resource. Found while writing the tests of the store (2026-11-02), this is a defect of the first mapping. Both columns use the binary collation `Latin1_General_100_BIN2`, and the migration `CaseSensitiveIdempotencyKeys` changes them. SQL Server refuses to change the collation of a column that an index uses, and the primary key uses both, so the migration is written by hand after the generator: it drops the primary key, changes the two columns, and creates the key again. A migration test applies the first migration, inserts a row, applies the second, and checks that the row and the key survive and that the comparison is exact. The change is its own commit in the pull request, because paths named `Migrations` need the review of the code owner.

### The expiry and the cleanup

A record expires 24 hours after its reservation, which the plan states. An expired record counts as absent: Begin deletes it, so the key can be used again. The cleanup job that runs in the loop of the outbox ([ADR-0035](0035-outbox-processor-design.md)) deletes expired records in batches of 1,000, and an index on `ExpiresAt` finds them.

### Consequences

- Good: the second request never waits, and the API can answer 409 at once.
- Good: the record of the result and the effect commit or roll back together, and the unit of work already provides the hook.
- Good: a crash costs at most the timeout, not 24 hours.
- Good: keys and callers are compared exactly.
- Bad: Begin and Release commit on their own, so a request that dies between Begin and the transaction leaves a reservation for up to 2 minutes. Mitigation: that is what the takeover is for, and the timeout is a setting.
- Bad: a request that outlives the timeout may lose to the request that took over and see an error. Mitigation: the loser rolls back, so no state is wrong, and the retry gets a replay.
- Bad: raw SQL, because EF Core cannot express the reservation. Mitigation: parameterized statements and the suites of the real database, including eight concurrent requests.
- Neutral: the response snapshot is stored as `nvarchar(max)` text, and the decorator decides its serialization.

### Confirmation

- `IdempotencyStoreTests` cover a new key, a request that runs, a reused key (while running and after finishing), the replay, a long response with non-ASCII text, two callers, exact comparison, release, expiry at 24 hours, the takeover and its timeout, the winner after a takeover, eight concurrent requests that get one reservation, concurrent requests with two payloads, the commit with the effect, the rollback with the effect, the completion without a reservation, and the cleanup.
- `MigrationTests` cover the collation migration on a database that already has a row.
- The suites ran on Windows and in the Linux service of [ADR-0034](0034-integration-test-database-strategy-and-linux-run.md).

## Pros and cons of the options

### Reserve outside, record inside, take over after a timeout

- Good: 409 without a wait, one effect for one key, and a bounded crash window.
- Bad: three operations instead of one, and a timeout to tune.

### Insert the record inside the transaction of the command only

- Good: no `InProgress` state, no leftover, and no takeover.
- Bad: the second request blocks on the primary key lock until the first one commits, holding a connection, and then gets a unique violation. The plan says that a concurrent request gets 409, and this option can only answer after the wait, or deadlock-prone under load.

### A distributed lock in Valkey

- Good: a lock with a time to live is a natural reservation.
- Bad: a second system in the write path, a lock that is not in the transaction of the effect, and a failure mode where the lock expires while the command runs. The record would still need the table.

### A response cache in a middleware

- Good: simple.
- Bad: it keeps no fingerprint, so it cannot answer 422, and it keeps nothing in the transaction of the effect.

## Revisit when

- A request can outlive the timeout, which calls for a heartbeat that extends the reservation.
- The retention changes. The plan says 24 hours, and the setting has a validator.
- Another command needs a result that is not text, which calls for a content type in the record.

## Further reading

- Plan section 8.4: [../plans/implementation-plan.md](../plans/implementation-plan.md)
- The IETF draft of the `Idempotency-Key` header: [https://datatracker.ietf.org/doc/draft-ietf-httpapi-idempotency-key-header/](https://datatracker.ietf.org/doc/draft-ietf-httpapi-idempotency-key-header/) (checked 2026-11-02)
- Collation and Unicode support in SQL Server: [https://learn.microsoft.com/sql/relational-databases/collations/collation-and-unicode-support](https://learn.microsoft.com/sql/relational-databases/collations/collation-and-unicode-support) (checked 2026-11-02)
