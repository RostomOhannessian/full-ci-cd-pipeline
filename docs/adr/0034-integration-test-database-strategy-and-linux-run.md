---
title: "ADR-0034: Test the persistence layer on one shared SQL Server container with a database for each test class, and give the Linux run the Docker socket as a reviewed exception"
description: "Decide how the integration suites start SQL Server, how each test class gets a clean database, how the images stay pinned, and how the Linux evidence of the Testcontainers suites is produced, including the Docker socket mount that it needs."
status: accepted
date: 2026-11-02
decision-makers: ["@RostomOhannessian"]
supersedes: []
superseded-by: []
tools: [testcontainers-dotnet, sql-server, docker-compose, xunit-v3]
related-adrs: ["0018", "0019", "0020", "0033"]
evidence:
  - docs/research/spikes/1.d-testcontainers-mssql.md
  - tests/Catalog.Infrastructure.Tests/Support
  - tools/lint/run-linux-tests.sh
  - docs/testing/evidence/2026-11-02-persistence-linux-run.md
---

# ADR-0034: Test the persistence layer on one shared SQL Server container with a database for each test class, and give the Linux run the Docker socket as a reviewed exception

## Context and problem statement

[ADR-0018](0018-test-stack-and-quality-gates.md) chose Testcontainers for the integration suites, and REQ-QUA-001 requires a real SQL Server, because `rowversion`, the filtered unique index, and the lock hints of the outbox are SQL Server behavior that a fake cannot show. [Spike 1.d](../research/spikes/1.d-testcontainers-mssql.md) proved the pattern on Windows with Docker Desktop and left four things for WP1.8: pin the image by digest and set a generated password, decide what to do with the privileged resource reaper, run the pattern on Linux and record the result, and keep memory measurements honest. Three facts of this repository add to that:

- The host that built WP1.8 enforces Smart App Control, which has blocked freshly built test assemblies before (WP1.6, WP1.7). The Linux SDK image is the place where every test can run, and the architecture tests, the coverage tracker, and the Testcontainers suites all behave differently on Linux (ADR-0029, the tracker defect of WP1.6).
- Plan section 10.3 requires that tests are deterministic: no wall clock, no sleeps, no order dependence.
- A test that starts containers needs the Docker daemon, and a container that runs the tests on Linux can reach the daemon only through its socket.

This ADR serves REQ-QUA-001, REQ-QUA-002, and REQ-QUA-004.

## Decision drivers

- One start of SQL Server for the whole assembly, because it takes about six seconds (spike 1.d).
- A test class sees only its own data, so classes can run in parallel without an order.
- Every image is pinned by digest, and no password is a constant.
- The Linux evidence is real: the same suites run in the pinned Linux SDK image with coverage, and the run says what ran where.
- A reviewed boundary: the Docker socket gives the holder control of the daemon, so the exception is named, limited, and recorded in the threat model.

## Considered options

1. One container for the assembly, a database for each test class, and a Linux service that mounts the Docker socket (chosen).
2. A container for each test class.
3. One database for the assembly, with a transaction rolled back by each test, or with a cleaner such as Respawn.
4. Run the Testcontainers suites on the Windows host and the rest in Linux.
5. Docker in Docker, or a TCP endpoint with a socket proxy, for the Linux run.
6. Skip the Testcontainers suites when no daemon is reachable.

## Decision outcome

Chosen option: **one container for the assembly, a database for each test class, and a Linux service with the Docker socket**, because it keeps the start cost at one, isolates classes without an order, and makes the Linux run the same suites that every other host runs.

### The suites

- `SqlServerFixture` is an xUnit v3 assembly fixture. It starts one SQL Server 2022 container from the image that `compose.yaml` pins, so the local stack and the tests run the same digest, and a change of the pin in one place is a change in both. The fixture reads the reference from the file and fails when none is pinned by tag and digest.
- The password is generated for each run with a cryptographic random source, and it exists only for the life of the container. No test prints it.
- `CatalogDatabaseFixture` is a class fixture that takes the assembly fixture in its constructor. It creates a database with a name that no other class uses, applies the migrations that ship, and drops the database when the class ends. Tests that need an empty database call `ResetAsync`, and the other tests build data that no test of the class can see, with identifiers from counters.
- A test that needs its own server-level objects, such as the logins of the migrator test, creates its own databases and drops them.
- Time comes from `FakeTimeProvider`, identifiers come from counters, and no test sleeps. A test that waits for a background service waits on an observable event with a bound.
- The project turns the invariant globalization mode off, because SqlClient does not support it (it throws `NotSupportedException` when it opens a connection), and a module initializer sets the invariant culture for every thread, so the tests still run under the invariant culture (plan section 10.3).
- The reaper (Ryuk) image is pinned by digest in the fixture, the digest that Testcontainers 4.15.0 uses by default (checked 2026-11-02), so a package update cannot move it. The fixture turns the privileged flag off, because the reaper needs it only on hosts that confine the Docker socket with SELinux. It ran without it on Docker Desktop and in the Linux service.
- Parallelism is declared in `xunit.runner.json`, and every class has a database of its own.

### The Linux run

The service `linux-tests` of `tools/lint/compose.yaml` runs `tools/lint/run-linux-tests.sh` in the pinned SDK image: it copies the repository to a scratch directory, restores in locked mode, builds in Release with warnings as errors, runs every test project with the code coverage extension, and checks the coverage thresholds. It mounts the repository read-only, a folder for the reports, and `/var/run/docker.sock`. Testcontainers starts SQL Server and the reaper as siblings through the socket, and the container reaches them through `host.docker.internal`.

The socket is the exception. A process that can use it controls the Docker daemon of the host, which is root-equivalent on a Linux host. The exception is limited:

| Rule | How it is held |
| --- | --- |
| Developer machine only | The service is not a service of the Compose stack, and no CI job runs it. The hosted runner has Docker and runs the suite directly |
| One service | No other service mounts the socket, and this ADR is the review that a second one would need |
| Pinned image and read-only repository | The image is the digest of the mutation service, and the repository mount is read-only |
| Named | The service carries a comment that names the exception, and the threat model lists the boundary with its residual risk |

### Where each run happened

| Where | What runs | Why |
| --- | --- | --- |
| The Windows host with Docker Desktop | Every project, including the Testcontainers suites, natively | The first feedback, and the host that spike 1.d verified |
| The Linux SDK image, through the service | Every project with coverage, including the Testcontainers suites | The Linux evidence that spike 1.d asked for, and the run that finds the defects that only Linux shows |
| The hosted `ci` workflow | Every project on `ubuntu-latest`, after the maintainer pushes | The runner has Docker, so the suites run there too (planned, checked by the first pull request) |

### Consequences

- Good: one container serves the assembly, a class cannot see the data of another, and the suites need no order.
- Good: the suites run unchanged on Windows and on Linux, and the image is the one that the local stack runs.
- Good: the image of the reaper cannot move without a change that a reviewer sees.
- Bad: the Docker socket is mounted in one service. Mitigation: the limits above, and the threat model names the residual risk.
- Bad: a database for each class costs about a second of migration time for each class. Mitigation: classes run in parallel, and the whole suite takes about 15 seconds.
- Bad: the project turns a global test setting off. Mitigation: the module initializer keeps the culture invariant, and an architecture test already checks the other test projects.
- Neutral: macOS and Apple Silicon contributors cannot start the image (ADR-0019), so they run the suites in a Codespace or on an x86-64 host, as the support matrix says.

### Confirmation

- The suites in `tests/Catalog.Infrastructure.Tests` pass on Windows and in the Linux service, and the evidence record `docs/testing/evidence/2026-11-02-persistence-linux-run.md` says what ran where, with the counts.
- A test reads `compose.yaml` and fails when the SQL Server image is not pinned by digest.
- The first hosted run of the `ci` workflow on a pull request that holds this work is the confirmation of the third row of the table.

## Pros and cons of the options

### One container for the assembly, a database for each test class, and a Linux service with the socket

- Good: one start, real isolation, and the same suites everywhere.
- Bad: the socket exception, and a migration for each class.

### A container for each test class

- Good: the strongest isolation.
- Bad: about six seconds and 1 GB of memory for each class, and the memory adds up when classes run in parallel.

### One database for the assembly with a rolled-back transaction or a cleaner

- Good: the fastest reset.
- Bad: a rolled-back transaction hides the commit behavior that the outbox and the idempotency store depend on, and a cleaner makes classes share a database, which brings back an order.

### Run the Testcontainers suites on the Windows host and the rest in Linux

- Good: no socket mount.
- Bad: the Linux run would skip the suites that spike 1.d asked to see on Linux, and the Linux image would pass without the most valuable tests.

### Docker in Docker, or a TCP endpoint with a socket proxy

- Good: a smaller grant than the whole socket.
- Bad: a privileged container or a second image to pin and keep patched, which is more surface for a developer-only run. It is the first thing to revisit if the exception becomes a concern.

### Skip the suites when no daemon is reachable

- Good: the suite never fails for a missing daemon.
- Bad: a gate that passes silently without its tests is a weakened gate, which the repository does not allow.

## Revisit when

- A second service needs the socket, or the run moves to a shared host, which calls for a socket proxy or rootless containers.
- The hosted `ci` workflow shows that the runner cannot start the container, or that the suite exceeds the time budget of plan section 10.5.
- A newer Testcontainers release changes the default reaper image or the way that the privileged flag works.

## Further reading

- Testcontainers for .NET, custom configuration: [https://dotnet.testcontainers.org/custom_configuration/](https://dotnet.testcontainers.org/custom_configuration/) (checked 2026-11-02)
- Spike 1.d: [../research/spikes/1.d-testcontainers-mssql.md](../research/spikes/1.d-testcontainers-mssql.md)
- The threat model: [../security/threat-model.md](../security/threat-model.md)
