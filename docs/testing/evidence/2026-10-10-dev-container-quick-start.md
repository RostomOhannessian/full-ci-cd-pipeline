---
title: "Evidence record: Dev Container quick start (2026-10-10)"
description: "A dated record that a fresh clone builds the Dev Container, passes dev doctor, builds and tests the solution, and runs the API-only stack, with the commands that ran and what they reported."
type: evidence-record
date: 2026-10-10
status: verified
audience: [maintainers, contributors]
last-verified: 2026-10-10
owner: "@RostomOhannessian"
---

# Evidence record: Dev Container quick start (2026-10-10)

This record is the manual evidence for `REQ-DX-001`, `REQ-DX-002`, `REQ-DX-005`, `REQ-IDN-001`, and the `dev doctor` step of `REQ-GOV-003`. A fresh clone of the WP1.4 branch built the Dev Container, ran `dev bootstrap` and `dev doctor` on creation, and then built the solution, ran the tests, and ran [Lab 1](../../labs/quick-start-api-stack.md) against the API-only stack inside the container.

## Scope and limits

- The clone came from the committed WP1.4 branch at commit `3112ba1`, into an empty folder. It was made on the author's Windows x86-64 machine with Docker Desktop (Docker 29.8.1, 24 CPUs, 30.2 GB). No build output, restored package, installed tool, or `.env` carried over.
- The Dev Container was built with the Dev Containers CLI 0.89.0 (`devcontainer up --frozen-lockfile`), which runs the same build and lifecycle commands as the VS Code extension and the `devcontainers/ci` action. The CLI itself ran in a Node 24 container. No VS Code window, Codespace, or hosted runner took part, so the hosted `devcontainer` workflow run, linked from the pull request, is the second piece of evidence for `REQ-DX-001`.
- Inside the container, Docker is the container's own daemon (docker-in-docker, Docker 29.9.0). The host's Docker was never mounted.
- The run covers one machine and one author. It does not cover macOS, Windows on Arm, or a person who has never seen the repository. [The host support matrix](../../reference/host-support.md) marks those paths untested, and the Phase 1 retrospective asks a second person to follow the quick start.
- The requirements stay `in_progress` in [the requirements register](../../requirements/requirements.yaml) where later work packages remain.

## Steps and results

| Step | Command or action | Result |
| --- | --- | --- |
| 1 | `git clone --branch wp/1.4-developer-environment <source> <empty folder>`, then `git config --local user.email` | The clone succeeded, and the repository's own email setting was empty. The noreply address was then set, as [ADR-0006](../../adr/0006-commit-identity-and-privacy.md) requires. |
| 2 | `devcontainer up --frozen-lockfile` | The container built and started in 204 seconds, including the image and feature pulls. The frozen lock file makes the CLI fail if the lock would change, and it did not. |
| 3 | `postCreateCommand`: mark the workspace as a safe Git directory, `sh ./dev bootstrap`, `sh ./dev doctor` | Bootstrap installed Pester 5.9.0 and PSScriptAnalyzer 1.25.0 after checking their SHA-256, and created `.env` with 10 generated secrets. Doctor reported `12 ok, 0 warning(s), 0 failure(s)`: Linux x64, Git, the noreply identity, SDK 10.0.401, PowerShell 7.6.6, Docker 29.9.0 with Compose, memory, CPUs, free ports, and `.env`. |
| 4 | `sh ./dev build` | `Build succeeded.` with 0 warnings and 0 errors. |
| 5 | `sh ./dev test unit` and `sh ./dev test architecture` | The unit suites passed: 265 documentation auditor tests and 366 governance auditor tests. The architecture suite passed: 58 tests. |
| 6 | `sh ./dev test scripts` | PSScriptAnalyzer found no problems, and 306 Pester tests passed with none skipped. On the Windows host 305 passed and 1 was skipped, because one test applies only to Linux and macOS. |
| 7 | `sh ./dev lab 1 setup` | The stack started and every health check passed. Everything listened on 127.0.0.1 only. |
| 8 | `sh ./dev lab 1 run` | `dev compose check` reported 5 ok: the containers, the SQL Server login, the Valkey ACL, the Keycloak realm, and the telemetry stack. The smoke-tests token carried `iss=http://localhost:8180/realms/catalog`, `aud=catalog-api`, and the scopes `catalog.admin catalog.write catalog.read`. The reader got `catalog-reader`, the editor got `catalog-editor` and `catalog-reader`, and the admin got all three roles. |
| 9 | `sh ./dev lab 1 cleanup` | The stack and its volume were removed. |

Steps 4 to 9 took 267 seconds in total, including the pull of the stack's images into the container's own Docker daemon.

## The native Windows path

The same machine also ran the native path without the Dev Container, with only Git, Docker, and the .NET SDK installed. `./dev doctor` reported `12 ok, 0 warning(s), 0 failure(s)` for Windows x64, and `./dev compose check` against the running stack reported `5 ok, 0 warning(s), 0 failure(s)`. The 689 .NET tests, the format check, the license gate, and the coverage gate passed there as well.

## What the run found

The first runs found defects that the Windows host had hidden. Each is fixed in the same pull request, with a test.

- Git refused the bind-mounted workspace as "dubious ownership", because a Windows bind mount shows the files as owned by root. `postCreateCommand` now marks the workspace as safe, and `dev doctor` explains the case instead of reporting a missing email.
- The documentation auditor counted a repository-local Dev Container feature as needing a lock entry, and its skill did not name the new `devcontainer-lock-*` rules. Both are fixed, with a test for each.
- Two Pester tests failed only on Linux: `Remove-Item` needs `-Force` for a dot file, and .NET prints file mode flags in value order.
- The documentation site build copied the generated `.env` into `_site`. The working-tree secret scan found it, and `docfx.json` now excludes `.env` and `.dev/`, with a test.

## What this does not prove

- It does not prove that the Dev Container opens in VS Code or in a Codespace. The hosted workflow proves the build and the lifecycle commands on a hosted runner, and a person must still open the container in an editor once.
- It does not prove the quick start for a person who has never seen the repository.
- It does not exercise the API, which arrives in WP1.10, so Lab 1 stops at the stack and its identity.
