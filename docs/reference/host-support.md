---
title: "Host support"
description: "Which hosts can run the API-only stack and the local platform, which of them have been verified by hand, and the limits that each host has."
audience: [learners, contributors, maintainers]
type: reference
tools: [docker-compose, sql-server, dev-containers, codespaces, dev-command]
last-verified: 2026-10-10
owner: "@RostomOhannessian"
---

# Host support

This page says what each kind of machine can run. The support matrix comes from [plan section 4.3](../plans/implementation-plan.md) and [ADR-0019](../adr/0019-resource-profiles-and-host-support.md), and the last column records
what has been checked by hand, so a reader can tell a promise from a proof. [`dev doctor`](tools/dev-command.md) checks the parts that it can see on your machine.

## The support matrix

| Host | API-only path (Compose) | Full platform (kind) | Verified by hand |
| --- | --- | --- | --- |
| Linux x86-64, Docker Engine | Supported | Supported | The Dev Container ran Lab 1 with docker-in-docker on Docker Desktop's Linux VM (2026-10-10). A bare Linux host is not verified yet |
| Windows 11 x86-64, Docker Desktop with WSL2 | Supported | Supported | Yes. The WP1.0 spikes and WP1.4 ran the stack and the quick start natively (2026-10-06 to 2026-10-10) |
| macOS Intel, Docker Desktop | Supported | Supported, subject to resources | No. No Intel Mac was available |
| macOS Apple Silicon | Best effort: SQL Server runs under Rosetta, which Microsoft does not support | Not supported. Use Codespaces or a remote x86-64 host | No |
| Windows on Arm | Best effort | Not supported | No |
| GitHub Codespaces | Supported (4 cores) | `lite` on 4 cores, `full` on 8 cores. The quota is documented | No. The Dev Container asks for 4 cores and 16 GB, and nobody has opened it in Codespaces yet |

"Supported" means that the design and the tests expect it to work. WP1.13 records the native smoke tests for Windows, Linux, and macOS that the Phase 1 exit gate requires, and the first Codespaces run.

## Why SQL Server decides the matrix

SQL Server container images run only on x86-64 Linux hosts. Microsoft's documentation says that emulation layers such as Rosetta 2, Prism, and QEMU are not tested or supported (checked 2026-10-06, [spike 1.d](../research/spikes/1.d-testcontainers-mssql.md)).
That is why Apple Silicon and Windows on Arm are best effort: the stack may start, and nobody promises that it will keep working. `dev doctor` warns on an Arm host and names the Codespaces fallback.

## Resource profiles

The budgets are indicative until WP3.0 measures them. `dev doctor --profile <name>` checks Docker's memory and CPUs against the profile.

| Profile | Runtime | Contents | Budget | Doctor warns below | Doctor fails below |
| --- | --- | --- | --- | --- | --- |
| `api` | Compose | SQL Server, Valkey, Keycloak, otel-lgtm, and the API on the host or in a container | about 4 to 5 GB and 2 CPUs | 4 GB or 2 CPUs | 3 GB |
| `lite` | kind, one node | The dev environment only, with metrics-only observability | about 10 GB and 4 CPUs | 10 GB or 4 CPUs | 8 GB |
| `full` | kind, three nodes | Dev, staging, and prod, with Kargo, Tempo, Loki, and Alertmanager | about 16 to 18 GB and 8 CPUs | 16 GB or 8 CPUs | 14 GB |
| `ci` | kind on a hosted runner | The subset a test needs, with Vault in dev mode | 4 CPUs and 16 GB at most | 16 GB or 4 CPUs | 12 GB |

Only `api` runs today. The idle `api` stack measured about 2.7 GiB by the containers' own counters (SQL Server 1.14 GiB, Keycloak 0.96 GiB, Grafana 0.52 GiB, Valkey 0.014 GiB, `docker stats`, 2026-10-10, one sample each),
so the 4 GB warning leaves room for the API, the editor, and the build.

## Host limits that affect more than the stack

- **Windows Smart App Control** can block a small unsigned assembly that is built locally, so a .NET test run may fail to load it. Run the tests in the Dev Container, or in a Linux container, when that happens
  ([spike 1.c](../research/spikes/1.c-mtp-coverage-stryker.md)).
- **PactNet 5.0.1** ships no `win-arm64` native library, and Stryker.NET could not start natively under Smart App Control ([spike 1.a](../research/spikes/1.a-pactnet-kestrel.md)).
- **Hosted macOS and Windows runners** cannot run Linux containers, so the Testcontainers suites run on Linux only (Phase 1 exit gate).
- **Docker Desktop on Windows and macOS** runs containers in a virtual machine, and the memory that `dev doctor` reports is the machine's, so raise it in Docker Desktop's settings and not in the operating system.
- **Ports.** The stack binds high ports on `127.0.0.1` only (1433, 6379, 8180, 3000, 4317, and 4318), and the API will use 8080 and 8081. No port needs administrator rights, and `dev doctor` names a port that another program holds.

## How to improve this page

If you run the quick start on a host that the last column marks as not verified, record the result in an Issue with the output of `dev doctor`, and the maintainer updates the matrix and the evidence.
