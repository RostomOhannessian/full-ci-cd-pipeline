---
title: "Native setup"
description: "Set up a Windows, Linux, or macOS machine without the Dev Container, with only Git, Docker, and the .NET SDK, and prove it with dev doctor."
audience: [learners, contributors]
type: tutorial
tools: [dev-command, powershell, docker-compose, dotnet-sdk, pester, psscriptanalyzer]
introduced: "phase-1"
prerequisites: ["A machine that the host support page lists", "Permission to install Git, Docker, and the .NET SDK"]
estimated-time: "30 minutes, plus the first image pull"
last-verified: 2026-10-10
owner: "@RostomOhannessian"
---

# Native setup

The [Dev Container](../reference/tools/dev-containers.md) is the first choice, because it needs only Docker and an editor. This guide is the second path, for a machine where you want the tools on the machine itself. It
has three prerequisites and no more, and the repository supplies everything else.

## What you install, and what the repository supplies

| You install | Why | Version |
| --- | --- | --- |
| Git | Clone the repository and commit | A current release |
| Docker | Run the API-only stack | Docker Desktop (Windows and macOS) or Docker Engine (Linux), with Compose 2.23 or newer |
| The .NET SDK | Build and test, and start PowerShell | 10.0.401, the version that `global.json` pins |

| The repository supplies | How |
| --- | --- |
| PowerShell 7 | A local .NET tool, restored by `dotnet tool restore` from `.config/dotnet-tools.json` (7.6.6) |
| Pester and PSScriptAnalyzer | Installed by `dev bootstrap` from `tools/versions.yaml`, after a checksum check |
| Docs tools, linters, and the secret scanner | Containers and local tools that the commands start |

Nothing else is needed: no Node, no Python, and no separate PowerShell install.

## Install the prerequisites

### Windows 11

1. Install Git for Windows. Leave line-ending conversion at its default. `.gitattributes` fixes the repository's line endings, and the clone is LF on every host.
2. Install Docker Desktop with the WSL2 backend, start it, and raise its memory to at least 4 GB in Settings, under Resources.
3. Install the .NET SDK 10.0.401 from the [.NET download page](https://dotnet.microsoft.com/download/dotnet/10.0) (checked 2026-10-10).

### Linux

1. Install Git, Docker Engine, and the Docker Compose plugin from your distribution or from Docker's repository, and add your user to the `docker` group so that no command needs `sudo`.
2. Install the .NET SDK 10.0.401 from Microsoft's packages or the install script on the [.NET download page](https://dotnet.microsoft.com/download/dotnet/10.0) (checked 2026-10-10).

### macOS

1. Install Git, and Docker Desktop, and raise its memory to at least 4 GB.
2. Install the .NET SDK 10.0.401.
3. On Apple Silicon, SQL Server runs under emulation that Microsoft does not support, so the stack is best effort. Use a Codespace or an x86-64 host if you need it to be reliable ([host support](../reference/host-support.md)).

## Set up the repository

1. Clone the repository and open a terminal at its root.
2. Set the commit identity for this repository to your GitHub noreply address, as [ADR-0006](../adr/0006-commit-identity-and-privacy.md) describes. Replace the placeholders.

   ```bash illustrative
   git config --local user.email "<id>+<login>@users.noreply.github.com"
   git config --local user.name "<your name>"
   ```

3. Check the SDK. It must satisfy `global.json`.

   ```bash illustrative
   dotnet --version
   ```

   Expected output: `10.0.401`, or a later patch of the same feature band.

4. Install the pinned tools and create `.env`. On Windows, `./dev` in PowerShell and `.\dev` in cmd.exe both run `dev.cmd`.

   ```bash illustrative
   ./dev bootstrap
   ```

   Expected output: `pester 5.9.0: installed`, `psscriptanalyzer 1.25.0: installed`, and `.env created with 10 generated secret(s)`.

5. Check the machine.

   ```bash illustrative
   ./dev doctor
   ```

   Expected output: every line starts with `[ok]`, and the last line says `0 failure(s)`. A warning about the Git identity means that step 2 is still to do. Each line that does not pass prints the command that fixes it.

6. Run the quick start from [Lab 1](../labs/quick-start-api-stack.md).

## If you cannot use the shims

The shims need only `dotnet`. If you prefer an installed PowerShell 7.4 or newer, run the script directly:

```bash illustrative
pwsh -NoProfile -File scripts/dev.ps1 doctor
```

## Troubleshooting

| Symptom | Cause | Fix |
| --- | --- | --- |
| `dev: the .NET SDK is not installed` | `dotnet` is not on the `PATH` | Install the SDK, and open a new terminal |
| `doctor` fails on the .NET SDK | The installed SDK does not satisfy `global.json` | Install 10.0.401 or a later patch of the 10.0.4xx band |
| `doctor` fails on Docker | Docker is not running, or it runs Windows containers | Start Docker Desktop, and switch it to Linux containers |
| `doctor` fails on Docker memory | The VM has less than 3 GB | Raise the memory in Docker Desktop's Resources settings |
| `doctor` fails on Ports | Another program uses a port of the stack | Stop it, or change the port variable that doctor names in `.env` |
| A .NET test cannot load an assembly on Windows | Smart App Control blocks unsigned local builds | Run the tests in the Dev Container |

## Further reading

- [The dev command](../reference/tools/dev-command.md) for every command
- [Host support](../reference/host-support.md) for what each host can run
- [Machine migration](../runbooks/machine-migration.md) for moving to a new machine without copying secrets
