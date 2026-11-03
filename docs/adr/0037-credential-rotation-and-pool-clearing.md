---
title: "ADR-0037: Set the database credential on each new connection from a cached source, and clear the pool of an old credential after a grace period"
description: "Decide where the database credential comes from, how a rotation reaches new connections without disabling pooling, when and how the pool of the old credential is cleared, in what order a rotation and a revocation must happen, how a bad file is handled, and how the persistence layer keeps a credential out of every log, message, and printed object."
status: accepted
date: 2026-11-02
decision-makers: ["@RostomOhannessian"]
supersedes: []
superseded-by: []
tools: [efcore, sqlclient, sql-server]
related-adrs: ["0008", "0012", "0033", "0034", "0035"]
evidence:
  - src/Catalog.Infrastructure/Credentials
  - tests/Catalog.Infrastructure.Tests/CredentialRotationTests.cs
  - tests/Catalog.Infrastructure.Tests/CredentialSourceTests.cs
  - docs/research/spikes/1.e-rotating-credentials.md
  - docs/testing/evidence/2026-11-02-credential-rotation-run.md
---

# ADR-0037: Set the database credential on each new connection from a cached source, and clear the pool of an old credential after a grace period

## Context and problem statement

Plan section 8.4 says that an `IDbConnectionInterceptor` sets the connection string from a cached `ICredentialSource` when a connection opens, that pooling stays on, and that on a rotation the pools keyed by the old credential are cleared after a grace period, with a test. [ADR-0008](0008-secrets-broker-vault-over-openbao.md) chose Vault as the broker of dynamic SQL Server credentials, and Phase 3 (WP3.3 and WP3.4) delivers the agent that renders a short-lived credential into a file and rotates it. [Spike 1.e](../research/spikes/1.e-rotating-credentials.md) showed on SQL Server 2022 that the mechanism works and listed five findings for this work package. The spike left four things open: a periodic re-read as a fallback for the file watcher, a single function that builds the connection string, the order of a rotation and a revocation, and a bad file. A fifth need comes from the repository: a credential must never reach a log, an exception message, an exception data bag, or a test output (plan section 9, the threat model). This ADR serves REQ-DATA-005.

## Decision drivers

- Rotation without a restart, without failed requests, and without turning pooling off.
- The old login ends with no session, so an operator can revoke it.
- A bad, half-written, or missing file never takes the application down once it runs.
- No value of a credential in any text that the process writes.
- The Compose stack keeps working with one static credential, and Kubernetes uses the rotating file.
- Tests that are deterministic: a fake clock ends the grace period, and the files are temporary.

## Considered options

1. A cached source, an interceptor that sets the connection string on each open, and a janitor that clears the old pool after a grace period (chosen).
2. A connection string that holds the credential and a restart on rotation.
3. `SqlCredential` on the connection instead of a connection string.
4. Turn pooling off, so every connection reads the current credential.
5. A custom `DbConnection` factory or a data source wrapper in place of an interceptor.

## Decision outcome

Chosen option: **a cached source, an interceptor, and a janitor**, because it is what plan section 8.4 and spike 1.e describe, it needs no restart, and every behavior in it has a test against a real SQL Server.

### The source

`ICredentialSource` has a cached `Current` credential and a `Rotated` event. There are two implementations, chosen by the settings in `DatabaseOptions`:

- `StaticCredentialSource` holds one credential from the configuration, for the Compose stack, where the password comes from `.env`. It never raises `Rotated`.
- `RotatingFileCredentialSource` reads a JSON file, `{"username":"...","password":"..."}`, which the agent of Phase 3 renders, and switches when the file changes. A `FileSystemWatcher` notices a change within milliseconds, and a timer reads the file again every 30 seconds, because a watcher can miss the swap of a projected volume on Kubernetes (spike 1.e did not test Linux file events). Both routes call the same `Refresh`, which is idempotent: a file that did not change raises nothing.
- **A file that cannot be used never replaces the credential in use.** A missing file, a half-written file, JSON that is not an object, a missing or empty field, or a file larger than 4 KB is logged as a fixed phrase at the warning level, and the old credential stays. A read at startup that fails throws, because the application cannot connect without a credential, and the host stops.
- A listener of `Rotated` that throws is logged by the name of its exception type, and it does not stop the other listeners or the switch, because the event runs on a timer or a watcher thread, where an unhandled exception ends the process.

### The interceptor and one connection string

`CredentialConnectionInterceptor` overrides `ConnectionOpening` and `ConnectionOpeningAsync` and sets `connection.ConnectionString` to the string of the current credential. EF Core opens its connection through the interceptor each time, so the first connection that opens after a rotation uses the new login, and the connections that the pool already holds keep the login that they were opened with until the janitor closes them.

SqlClient keys a pool by the **full** connection string, credential included. The interceptor and the janitor therefore build the string with one function, `CatalogConnectionString.Build`, which uses the normalizing `SqlConnectionStringBuilder`, so strings that differ in the order or the spelling of their keys give the same text and the janitor clears the pool that was actually used. The function forces `Pooling=true`. `DatabaseOptions` refuses, at startup, a base string that already holds a user name, a password, an integrated or token authentication, or `Pooling=false`, so the credential source is the only supplier, and the message of the refusal names the keyword and never repeats a value.

### The janitor and the order of a rotation

`CredentialPoolJanitor` follows `Rotated`. After the grace period (60 seconds by default, validated between zero and one hour) it builds the old connection string and calls `SqlConnection.ClearPool`, which closes the idle connections of that pool at once and marks the connections that are in use to close when they return. **Clearing never interrupts a running command**, which spike 1.e measured and a test here repeats: a transaction that started on the old login finishes after the clear, and the session ends when the connection returns. Two guards keep the janitor safe:

- A credential that returns to use (A, then B, then A) is never cleared while it is the current one.
- The timers of the grace periods belong to the clock, so a test ends a grace period by moving a fake clock, and a stopped janitor drops the clears that still wait.

The order that keeps a rotation safe is **rotate, switch, clear, revoke**. Spike 1.e found that revoking the old login first kills in-flight work on it (SQL Server error 596) and that SQL Server refuses to drop a login that still has a session (error 15434). With Vault, the old credential keeps its lease after the new one is issued, so the revocation normally comes well after the switch. The grace period therefore bounds how long idle sessions of the old login linger. It does not protect running work, which the order does. WP3.3 must confirm that the revocation statements of the Vault role come after the clear.

### Nothing secret in any text

- `DatabaseCredential` prints its user name and `[redacted]` instead of the password, and `DatabaseOptions` prints only its type name, so a log line, an exception, or a debugger display of either cannot leak.
- The source logs fixed phrases and the name of an exception type, never the content of the file, a part of it, or the message of an exception that a parser or a listener threw. The janitor logs counts and phrases.
- The refusal of a refused credential part names the part, and the refusal of a setting names the setting.
- A test puts a known password in a file, breaks the file in several ways, rotates to a login that does not exist, clears the pool, and collects every log entry, every exception message, every exception data bag, and every printed credential. None of them holds the password. SqlClient's own login failure names the login and not the password.

### Where the registrations live

The Api is the composition root, and an architecture rule forbids `IServiceProvider` anywhere else, so the registrations cannot live in the Infrastructure project. `CredentialSources.Create` chooses the source for a set of settings, and the settings classes have validators for `IValidateOptions`, so the Api registers them with start-up validation in WP1.10. `tests/Catalog.Infrastructure.Tests/Support/TestComposition.cs` is the reference composition: it registers the credential source, the interceptors, the context factories, the adapters, the outbox loop, and the janitor, and a test builds a real container from it, commits a category, and sees the message reach a handler as the configured login.

### Consequences

- Good: rotation needs no restart and no failed request, and the old login ends with no session after the grace period.
- Good: the interceptor, the janitor, and the tests share one connection string function, so the pool that is cleared is the pool that was used.
- Good: a bad file costs a warning and nothing else, and a wrong setting stops the host at startup.
- Bad: for the length of the grace period two pools exist, and the old one holds idle sessions. Mitigation: the grace period is a setting, and a pool has a maximum size.
- Bad: an application replica that restarts during a revocation window sees a login that no longer exists. Mitigation: the order of the rotation, and the credential of a restarting replica comes from the current file.
- Bad: the janitor cannot know that a revocation happened early. Mitigation: the order is documented here and in the threat model, and WP3.3 and WP3.4 confirm it with Vault.
- Neutral: the poll of the file runs every 30 seconds even when the watcher works. It reads a file of a few bytes.

### Confirmation

- `CredentialRotationTests` run against a real SQL Server: the interceptor gives each connection the credential of the source, a rotation switches new connections while the pool is reused, the old login has no session after the grace period (read from `sys.dm_exec_sessions`), work in flight finishes after the clear, the old login can be dropped afterward, a credential that returns is not cleared, a zero grace clears at once, an outbox batch that spans a rotation is delivered completely (the test that ADR-0012 asks of WP1.8), and no secret leaks.
- `CredentialSourceTests` and `ConnectionStringAndOptionsTests` cover the file handling, the watcher and the poll, the failing listener, the connection string, and the validators. `PersistenceCompositionTests` build a real container.
- The tests wait for the server in one place only: the session table of SQL Server shows the end of a closed connection a moment after the client closed it, so `EventuallyAsync` polls it with a bound of 30 seconds. It is a poll of an external system, and a condition that never holds fails the test.
- The suites ran on Linux in the pinned SDK image with the Docker socket, because Smart App Control blocked the freshly built test assembly on the Windows host, and the evidence record says so.

## Pros and cons of the options

### A cached source, an interceptor, and a janitor

- Good: no restart, the same design as the spike, and testable on a fake clock.
- Bad: a janitor and a grace period to tune.

### A connection string with the credential and a restart on rotation

- Good: the simplest code.
- Bad: every rotation restarts every replica, and a short-lived credential would restart them often.

### `SqlCredential`

- Good: the password lives in a read-only, secure string in the connection.
- Bad: the pool key then includes the credential object in a way that the clear of an old pool cannot reproduce without the same object, and the spike did not compare it. It is the first thing to revisit if the string form becomes a concern.

### Turn pooling off

- Good: no pool to clear.
- Bad: a new TCP and TLS connection for each operation, and the requirement says to keep pooling.

### A custom connection factory

- Good: full control.
- Bad: more code than an interceptor that EF Core already supports, and the interceptor is what the plan names.

## Revisit when

- WP3.3 or WP3.4 shows that the Vault agent writes the file in a way that neither the watcher nor the poll sees, or that the revocation statements run before the clear.
- A rotation causes failed requests under load, which would call for a longer grace period or a check that the new login works before the switch.
- SqlClient adds a supported way to clear a pool without building a connection.

## Further reading

- Spike 1.e: [../research/spikes/1.e-rotating-credentials.md](../research/spikes/1.e-rotating-credentials.md)
- Connection pooling in SqlClient: [https://learn.microsoft.com/sql/connect/ado-net/sql-server-connection-pooling](https://learn.microsoft.com/sql/connect/ado-net/sql-server-connection-pooling) (checked 2026-11-02)
- EF Core interceptors: [https://learn.microsoft.com/ef/core/logging-events-diagnostics/interceptors](https://learn.microsoft.com/ef/core/logging-events-diagnostics/interceptors) (checked 2026-11-02)
- Plan section 8.4: [../plans/implementation-plan.md](../plans/implementation-plan.md)
