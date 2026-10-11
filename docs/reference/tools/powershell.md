---
title: "PowerShell"
description: "The cross-platform shell and scripting language that runs the dev command, restored as a local .NET tool so no separate install is needed."
audience: [learners, contributors]
tier: B
tools: [powershell, dev-command, pester, psscriptanalyzer]
introduced: "phase-1"
prerequisites: ["The .NET SDK 10.0.401", "A clone of the repository"]
estimated-time: "10 minutes"
last-verified: 2026-10-10
verified-against: { powershell: "7.6.6" }
owner: "@RostomOhannessian"
---

# PowerShell

## Overview

PowerShell is a shell and a scripting language. Its commands pass objects and not text, so a script can read a property of a result instead of cutting a column out of a line. PowerShell 7 runs on Windows, Linux,
and macOS from the same code, and that is why this repository uses it for [the dev command](dev-command.md) and for its script tests.

Four concepts cover what you need here:

| Concept | What it is | Where it lives here |
| --- | --- | --- |
| Script | A `.ps1` file that you run | `scripts/dev.ps1`, `scripts/Invoke-ScriptTests.ps1`, `tools/ci/*.ps1` |
| Module | A `.psm1` file that exports functions, loaded with `Import-Module` | `scripts/modules/Dev.*.psm1` |
| Local .NET tool | A tool that `dotnet tool restore` installs for one repository, at the version a manifest pins | `.config/dotnet-tools.json`, entry `powershell` |
| Strict mode | `Set-StrictMode -Version Latest`, which turns a misspelled variable into an error | The first line of every module |

The repository does not ask you to install PowerShell. It restores PowerShell 7.6.6 as a local tool, and the `dev` shims start it with `dotnet pwsh`. The only native prerequisites stay Git, Docker, and the .NET SDK
([ADR-0025](../../adr/0025-dev-command-and-dev-container.md)).

## Decision rationale

The plan chooses PowerShell 7 for the developer scripts, because one language and one test framework serve every host ([plan section 12](../../plans/implementation-plan.md)). [ADR-0025](../../adr/0025-dev-command-and-dev-container.md) compares it with Make or Task runners and
with Bash scripts, and [ADR-0024](../../adr/0024-verified-tool-installer.md) records why the script tools are installed by a verified installer.

| Criterion | What was found |
| --- | --- |
| License | MIT (checked 2026-10-10 on the [repository license](https://github.com/PowerShell/PowerShell/blob/master/LICENSE.txt)) |
| Maintenance | Version 7.6.6 was the latest on nuget.org (published 2026-09-08, checked 2026-10-10), and Microsoft maintains the project |
| Cost | The tool restores from nuget.org with a required repository signature ([ADR-0020](../../adr/0020-dependency-intake-controls.md)), so there is nothing to install by hand |
| Trade-off | PowerShell is not every contributor's first language. The scripts are small, tested, and run the same commands that CI runs |

## Setup tutorial

**Prerequisites:** the .NET SDK that `global.json` pins, and a clone of the repository.

1. Restore the local tools. The version of PowerShell is pinned in `.config/dotnet-tools.json`, and the restore installs exactly that version.

   ```bash illustrative
   dotnet tool restore
   ```

2. Check the version.

   ```bash illustrative
   dotnet pwsh --version
   ```

   Expected output: `PowerShell 7.6.6`.

3. Run the dev command through a shim, which does both steps for you.

   ```bash illustrative
   ./dev help
   ```

   On Windows, type `dev help`, which runs `dev.cmd`.

If you prefer an installed PowerShell 7.4 or newer, run `pwsh -File scripts/dev.ps1 help` instead. The scripts need 7.4 or newer, and they check for it.

**Cleanup:** none. A local tool lives in the NuGet cache, and `dotnet tool uninstall` is not needed.

## How this project uses it

- **The dev command.** `scripts/dev.ps1` dispatches to the modules in `scripts/modules`, and the shims `dev` and `dev.cmd` start it. See [the dev command](dev-command.md).
- **Script tests.** [Pester](https://pester.dev/) runs the tests under `scripts/tests` and `tools/ci`, and PSScriptAnalyzer checks the scripts. Both are pinned in `tools/versions.yaml` and installed by `dev bootstrap`.
  CI runs one script for both:

  [!code-bash[Script tests in CI](../../../.github/workflows/ci.yml#script-tests)]

- **Strict mode and exit codes.** Every module sets strict mode, and every command returns an exit code that a caller can test.

## Validation and troubleshooting

`dev doctor` checks the PowerShell version. If a shim fails, run `dotnet tool restore` and read its message.

| Symptom | Cause | Fix |
| --- | --- | --- |
| `dotnet pwsh` says that the command is not available | The local tools are not restored | Run `dotnet tool restore` |
| dotnet reports that no compatible SDK was found | The installed SDK does not satisfy `global.json` | Install the SDK that it pins (`dev doctor` names it) |
| The script "cannot be run because it contained a #requires statement" | You ran it with Windows PowerShell 5.1 | Use `dotnet pwsh` or `pwsh`, which are PowerShell 7 |

## Further research

- [PowerShell documentation](https://learn.microsoft.com/en-us/powershell/) (checked 2026-10-10)
- [The PowerShell repository and releases](https://github.com/PowerShell/PowerShell) (checked 2026-10-10)
- [The PowerShell package on NuGet](https://www.nuget.org/packages/PowerShell) (checked 2026-10-10)
- [about_Modules](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_modules) (checked 2026-10-10)
- [Set-StrictMode](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/set-strictmode) (checked 2026-10-10)
