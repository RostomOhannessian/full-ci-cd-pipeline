---
title: "Evidence record: credential rotation suites on Linux (2026-11-02)"
description: "A dated record of the credential rotation suites of WP1.8: what ran where, why the Infrastructure tests ran in the pinned Linux SDK image, the counts, the coverage, and what the runs did not show."
type: evidence-record
date: 2026-11-02
status: verified
audience: [maintainers, contributors]
last-verified: 2026-11-02
owner: "@RostomOhannessian"
---

# Evidence record: credential rotation suites on Linux (2026-11-02)

This record is the evidence for `REQ-DATA-005` (the application side) and for the composition checks of `REQ-DATA-002` to `REQ-DATA-004`. It continues [the outbox and idempotency record](2026-11-02-outbox-idempotency-run.md) for the third pull request of WP1.8, and it says what ran where ([ADR-0037](../../adr/0037-credential-rotation-and-pool-clearing.md), [ADR-0034](../../adr/0034-integration-test-database-strategy-and-linux-run.md)).

## What ran where

| Where | Command | What it ran | Result |
| --- | --- | --- | --- |
| Windows x86-64, .NET SDK 10.0.403 | `dotnet test --project tests/<project>/<project>.csproj --configuration Release --no-build` for each project except the Infrastructure tests | Architecture, Application, Domain, Contract, Documentation.Auditor, and Governance.Auditor tests | Passed: 90, 192, 173, 100, 265, and 608 tests |
| Windows x86-64 | The same command for `tests/Catalog.Infrastructure.Tests` | The persistence suites | **Blocked by Smart App Control**: the freshly built test assembly did not load (`An Application Control policy has blocked this file`, 0x800711C7), also after a rebuild without the incremental cache. No host setting was changed and no gate was skipped |
| Linux, the pinned SDK 10.0.401 image, Docker socket mounted | `docker compose -f tools/lint/compose.yaml run --rm linux-tests` | A scratch copy of the repository: locked restore, Release build with warnings as errors, every test project with the code coverage extension, and the coverage thresholds | 1,648 passed, 0 failed, 0 skipped. The Infrastructure project has 220 tests |

The Testcontainers suites, including every credential test, therefore ran on Linux only in this pull request. The Linux run started SQL Server and the reaper as sibling containers through the socket.

## What the new tests proved

| Claim | Test class |
| --- | --- |
| The interceptor gives each new connection the credential of the source, and follows a rotation | `CredentialRotationTests`, `ConnectionStringAndOptionsTests` |
| A rotation switches new connections to the new login, the pool is reused before and after (ten sequential operations used at most two sessions), and the new login has a pool of its own | `CredentialRotationTests` |
| After the grace period (a fake clock moves 60 seconds) the old pool is cleared and `sys.dm_exec_sessions` shows no session for the old login. Before the end of the grace period the clear still waits | `CredentialRotationTests` |
| A transaction that started on the old login finishes after the clear, and its session ends when the connection returns | `CredentialRotationTests` |
| An outbox batch that spans a rotation is delivered completely: a handler rotates the credential and ends the grace period in the middle of the batch, the four messages end as `Done` with four receipts, the messages after the rotation were finished by the new login, and the old login ends with no session (the credential rotation during delivery that ADR-0012 asks WP1.8 to test) | `CredentialRotationTests` |
| The old login can be dropped after the clear, and the new login keeps working | `CredentialRotationTests` |
| A credential that returns to use is not cleared, a zero grace clears at once, and a stopped janitor drops the clears that wait | `CredentialRotationTests` |
| No value of a credential reaches a log entry, an exception message, an exception data bag, or a printed credential, through a broken file, a login that does not exist, and a pool clear | `CredentialRotationTests`, `CredentialSourceTests` |
| The rotating source reads the file, accepts any case of the property names, switches once for one change, reads a file that an agent replaced by a rename, keeps the credential in use for eight kinds of bad file, reads again on the poll timer, and notices a change through the real file watcher | `CredentialSourceTests` |
| The connection string is the same text for the same credential in any key order, pooling is forced on, a base string with a credential or without pooling is refused by a message that never repeats a value, and the settings and their validators have bounds | `ConnectionStringAndOptionsTests` |
| A real container wires the layer, a commit reaches a handler through the loop as the configured login, each scope has its own context, and a setting that holds a credential is refused when the options are read | `PersistenceCompositionTests` |

The tests wait for the server in one place: `SqlLogins.EventuallyAsync` polls the session table of SQL Server with a bound of 30 seconds, because the server ends the session of a closed connection a moment after the client closed it. A condition that never holds fails the test. Every other wait is a fake clock, a channel, or an event, and no test sleeps to make a result appear.

## A latent defect that this work found

The migrator test of the second pull request (`MigrationTests`) passed only because other test classes cleared every SqlClient pool when they ended. The application identity's failed migration attempt left the EF Core migration lock (`__EFMigrationsLock`, an application lock owned by the session) in a pooled session, and the migrator then waited for it until the 30-second command timeout. The test fails when run alone on the second pull request as first committed, and a query of the session and request tables on the server showed the sleeping session of the application identity as the blocker. The fix ends the session of the application identity before the migrator runs, and it is in the second pull request. The fixtures no longer clear every pool, so the credential tests can count the sessions of their own logins.

## Coverage

The thresholds of `governance/policies/coverage-thresholds.json` passed in the Linux run. The Infrastructure assembly has no threshold of its own in the policy, and a change of a threshold needs an ADR, so none was added.

## Limits

- The Windows host could not load the Infrastructure test assembly in this session, so the persistence and credential suites ran on Linux only. The hosted `ci` workflow runs them on `ubuntu-latest`, and no hosted run has happened, because nothing is pushed yet.
- The tests prove the application side of the rotation with a local file. Vault, the agent that renders the file, lease renewal, and the revocation statements of the role are Phase 3 (WP3.3 and WP3.4), which will confirm the order rotate, switch, clear, revoke on the real broker.
- The file watcher was tested on Linux and, in spike 1.e, on Windows. Kubernetes projected volumes swap a symbolic link, which the poll timer covers and the tests drive with a fake clock. A rehearsal in a cluster is a Phase 3 item.
- The suites ran on x86-64 hosts only. Timings are single samples from one machine.
