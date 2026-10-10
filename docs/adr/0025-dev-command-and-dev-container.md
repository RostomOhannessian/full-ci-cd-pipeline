---
title: "ADR-0025: Run local development through one PowerShell command, thin shims, and a pinned Dev Container"
description: "Make the dev command the single entry point, start it from the .NET SDK alone, and offer a Dev Container with a digest-pinned base, locked features, and docker-in-docker, because setup must be one command on any host and a fresh environment must be proven in CI."
status: accepted
date: 2026-10-10
decision-makers: ["@RostomOhannessian"]
supersedes: []
superseded-by: []
tools: [dev-command, powershell, pester, psscriptanalyzer, dev-containers, devcontainers-ci, dotnet-devcontainer-image, devcontainer-feature-docker-in-docker, devcontainer-feature-github-cli, codespaces, docker-compose, github-actions, dependabot]
related-adrs: ["0003", "0018", "0019", "0020", "0024", "0026"]
evidence:
  - docs/testing/evidence/2026-10-10-dev-container-quick-start.md
---

# ADR-0025: Run local development through one PowerShell command, thin shims, and a pinned Dev Container

## Context and problem statement

Plan section 12 makes a Dev Container the primary environment, documents a native setup for Windows, Linux, and macOS with Git, Docker, and the .NET 10 SDK as the only
prerequisites, and calls for one `dev` command with subcommands that Pester and PSScriptAnalyzer test. `REQ-DX-001`, `REQ-DX-002`, `REQ-DX-003`, `REQ-DX-005`, and `REQ-CI-005` carry the details. The plan
also asks for a `devcontainers/ci` smoke test that runs doctor, the build, the unit tests, and the quick-start script.

Facts that shape the design (checked 2026-10-10):

- PowerShell 7.6.6 is a .NET tool on nuget.org, and `dotnet tool restore` installs it from `.config/dotnet-tools.json` with the required repository signature. `dotnet pwsh` then runs it. This
  keeps the native prerequisites at three.
- The Dev Containers specification supports a lock file, `devcontainer-lock.json`, that pins each feature to a version and a digest. CLI 0.89.0 writes it with `devcontainer upgrade` and checks it
  with `--frozen-lockfile`.
- The Microsoft .NET Dev Container image `mcr.microsoft.com/devcontainers/dotnet:10.0` carried SDK 10.0.401 on Ubuntu 24.04, with Git and a non-root `vscode` user.
- The docker-in-docker feature gives the container its own Docker daemon. It runs the container privileged. Its current major version is 4 (4.1.3), and the previous major version 2 reached 2.17.0.
- A `.sh` script on a Windows clone has no executable bit unless Git sets it, so a lifecycle command that calls `./dev` can fail there, and `sh ./dev` works.
- A GitHub-hosted runner has 4 vCPU and 16 GB of memory (plan section 4.1), which fits the `api` profile.

## Decision drivers

- A new contributor reaches a working environment with one command and no more than three prerequisites.
- Every host runs the same command, and the command is tested.
- A fresh Dev Container is proven in CI, so the onboarding path cannot rot.
- The base image, the features, and the tools are pinned, and an update is a reviewed change.
- The command says what it cannot do yet, and names the work package that delivers it.

## Considered options

1. A PowerShell `dev` command run through thin shims as a local .NET tool, with a Dev Container that builds from a digest-pinned image and runs docker-in-docker (chosen).
2. A task runner such as Make, Task, or just.
3. Bash scripts, with a separate set for Windows.
4. A Dev Container that shares the host's Docker socket (docker-outside-of-docker).
5. Codespaces as the only supported environment.
6. Run the Dev Container build in CI with the Dev Containers CLI directly, instead of the `devcontainers/ci` action.

## Decision outcome

Chosen option: **a PowerShell command behind thin shims, and a pinned Dev Container with docker-in-docker**, because one language and one set of tests serve every host, the shims need only the .NET SDK, and
docker-in-docker keeps Compose, kind, and Testcontainers away from the host's Docker daemon.

### The command

- **One entry point.** `scripts/dev.ps1` dispatches to modules under `scripts/modules`: `doctor`, `bootstrap`, `build`, `test [suite]`, `docs [serve]`, `compose up|down|reset|ps|logs|check`, and `lab <id> setup|run|cleanup`.
  `platform`, `tf`, `seed`, `demo`, and `clean` are in the command table as planned, with the work package that delivers each, and they exit with code 2 and a message.
- **Exit codes.** 0 is success, 1 is a failed check or step, and 2 is wrong usage or a command that does not exist yet. A nonzero code always means "do not continue".
- **The shims.** `dev` (a POSIX `sh` script, executable in Git) and `dev.cmd` (CRLF, as `.gitattributes` requires) run `dotnet tool restore` and then `dotnet pwsh -NoProfile -File scripts/dev.ps1`. They hold no
  logic, and a test checks that they stay under 20 lines.
- **Doctor.** `dev doctor [--profile api|lite|full|ci]` checks the host and its architecture, Git and the commit identity, the .NET SDK against `global.json`, PowerShell, the pinned script tools, Docker and Compose
  and their versions, Docker's memory and CPUs against the profile budget, the ports that the stack binds, and `.env`. It changes nothing. A warning does not fail, a failure does, and each non-passing check
  prints its fix. Only the `api` profile can run today, so `lite`, `full`, and `ci` get the budget check and a note that the kind platform arrives in Phase 3.
- **Budgets.** The `api` profile warns below 4 GB of Docker memory and fails below 3 GB, because SQL Server needs 2 GB and the idle stack measured about 2.7 GB (docker stats, 2026-10-10, [ADR-0026](0026-api-only-compose-stack-and-catalog-realm.md)).
  The numbers stay indicative until WP3.0 measures the real figures, as ADR-0019 says.
- **Wrappers.** `build`, `test`, and `docs` run the commands that CI runs, so a result locally is the CI result. `dev test scripts` runs `scripts/Invoke-ScriptTests.ps1`, which CI also runs.
  A new test project joins the `unit` suite by its name, so a later work package needs no change here.

### The Dev Container

- **Base.** `.devcontainer/Dockerfile` starts from `mcr.microsoft.com/devcontainers/dotnet:10.0` pinned by tag and digest, and sets the telemetry opt-outs.
- **Features.** `docker-in-docker:4` with `moby` off (Docker CE) and `github-cli:1`, pinned by `.devcontainer/devcontainer-lock.json`, which the documentation auditor checks against the configuration (missing, stale, and undigested entries fail).
- **Lifecycle.** `postCreateCommand` runs `sh ./dev bootstrap && sh ./dev doctor`, which installs the pinned tools, creates `.env`, and checks the machine. It calls `sh` explicitly, so a clone without the executable bit still works.
- **Host requirements.** 4 CPUs and 16 GB, which is the smallest Codespaces machine that plan section 12 names for the `api` profile. Keycloak and Grafana are forwarded.
- **Editor extensions.** Four are suggested (PowerShell, C#, markdownlint, and YAML). None is required, and the repository depends on none.

### The pipeline

- **`devcontainer.yml`.** It runs `devcontainers/ci` pinned by SHA, with `push: never` and a read-only token, and the command runs doctor, the build, the unit tests, and Lab 1 end to end. It runs when the
  Dev Container or what it runs changes, every Monday, and by hand. It is not a required check, and adding it to a ruleset is a settings change that the maintainer decides.
- **`ci.yml`.** The `Script tests` job now runs `scripts/Invoke-ScriptTests.ps1`, so it uses the pinned Pester and PSScriptAnalyzer and covers `scripts` and `tools/ci`.
- **Dependabot.** A `docker` entry for `/.devcontainer` proposes updates to the base image. The existing `devcontainers` entry covers the features, and the `docker-compose` entry covers `compose.yaml`.
- **Blast radius.** The map gains a `devcontainer` job, so a change to the scripts, the Dev Container, Compose, the realm, or the tool manifest runs the onboarding.

### Consequences

- Good: one command and one language on every host, with 278 script tests and no analyzer findings, and the shims are the only host-specific files.
- Good: a fresh Dev Container is built and used in CI, so a change that breaks onboarding fails before it merges.
- Good: a rebuild gives the same tools, because the base image, the features, and the scripts' tools are pinned.
- Bad: the Dev Container runs privileged, because docker-in-docker needs it. A compromised container could take over the host's Docker Desktop VM. Mitigation: the daemon is private to the container, no host socket is
  mounted, the stack binds only to loopback, and the threat model lists it. Codespaces runs the container in a throwaway virtual machine.
- Bad: the container pulls about 3 GB of images on the first run, because the inner Docker has its own image store. Mitigation: the volume survives a rebuild, and the lab says what to expect.
- Bad: PowerShell is an unusual choice for some contributors. Mitigation: it is the language that the plan chose, the code is small and tested, and the Dev Container supplies it.
- Neutral: Codespaces is configured and not yet exercised, so its inventory entry stays planned.

### Confirmation

- **WP1.4** delivers the command, its modules, 278 Pester tests, the shims, the Dev Container, the workflow, and the evidence record. The hosted run of the workflow is linked from the pull request.
- Pester tests check the shims, the command table against AGENTS.md, the doctor checks with a faked machine, and the Dev Container files (digest pins, the lock file, the lifecycle command).
- A local run built the Dev Container with the Dev Containers CLI 0.89.0 and `--frozen-lockfile`, ran `postCreateCommand`, and ran Lab 1 inside it with docker-in-docker (evidence record, 2026-10-10).
- **WP1.13** records the native Windows, Linux, and macOS smoke tests that the Phase 1 exit gate requires, and the first Codespaces run.

## Pros and cons of the options

### PowerShell behind thin shims, and a pinned Dev Container with docker-in-docker

- Good: one language, one test framework, and no prerequisite beyond the .NET SDK.
- Bad: privileged container, and a first run that pulls the images again.

### A task runner

- Good: familiar to many contributors, and short for simple tasks.
- Bad: another prerequisite on Windows, and the doctor, the installer, and the stack check are programs and not tasks, so they need a language anyway.

### Bash scripts and a Windows set

- Good: no runtime to install on Linux and macOS.
- Bad: two implementations to keep in step, and Bash tests need another framework. `REQ-DX-002` names one set of shims over one script.

### Docker-outside-of-docker

- Good: no privileged container, and images are shared with the host.
- Bad: the container controls the host's daemon through its socket, which is a larger trust boundary, and a kind cluster would run beside the host's other containers.

### Codespaces only

- Good: one x86-64 environment.
- Bad: it conflicts with the plan's native path and with the free-tier quota (about 30 hours a month on 4 cores).

### The Dev Containers CLI directly in CI

- Good: one less third-party action.
- Bad: the action already builds, starts, and runs a command with the lifecycle, and it is maintained under the `devcontainers` organization (MIT). It is pinned by SHA with a read-only token.

## Revisit when

- The `devcontainer` job is flaky or slower than 15 minutes, which would argue for image caching or a prebuild.
- A new major version of the docker-in-docker feature changes its privilege model.
- The maintainer opens the Dev Container in Codespaces, which turns that inventory entry to in-use.
- Phase 3 adds commands, which reopens the budget numbers.

## Further reading

- [Dev Container specification](https://containers.dev/implementors/json_reference/) (checked 2026-10-10)
- [Dev Container features and the lock file](https://containers.dev/implementors/features/) (checked 2026-10-10)
- [devcontainers/ci GitHub Action](https://github.com/devcontainers/ci/blob/main/docs/github-action.md) (checked 2026-10-10)
- [ADR-0019: Resource profiles and host support](0019-resource-profiles-and-host-support.md)
- [ADR-0024: The verified tool installer](0024-verified-tool-installer.md)
