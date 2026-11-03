---
title: "Evidence record: the SqlClient Windows runtime license exception (2026-11-03)"
description: "A dated record of the real license gate before and after the exception for Microsoft.Data.SqlClient.SNI.runtime 7.1.0, and of two real publishes that show which one holds the native Windows libraries."
type: evidence-record
date: 2026-11-03
status: verified
audience: [maintainers, contributors]
last-verified: 2026-11-03
owner: "@RostomOhannessian"
---

# Evidence record: the SqlClient Windows runtime license exception (2026-11-03)

This record is the evidence for the [amendment of ADR-0004](../../adr/0004-licensing-and-dependency-license-policy.md#amendment-of-2026-11-03-a-native-windows-runtime-that-no-shipped-artifact-contains), and it supports `REQ-SUP-003`. The branch of this change is based on `phase/1-api-core`, where SqlClient is not a dependency, so the real failure cannot appear there. The runs below used a throwaway local branch that was created from the unpushed WP1.8 persistence branch (commit `a2ac582`), which has EF Core, SqlClient, and their lock files. The policy commit of this change was cherry-picked onto it, and the branch was deleted afterward. No WP1.8 branch changed.

## Scope and limits

- The runs were made on the author's Windows x86-64 machine with .NET SDK 10.0.403 and `nuget-license` 4.0.18 from `.config/dotnet-tools.json`.
- The gate is the command of the `license` job: `dotnet nuget-license -i ProductCatalog.slnx -t -a governance/policies/license-policy.json -override governance/policies/license-overrides.json -err`, after `dotnet restore ProductCatalog.slnx --locked-mode`.
- The license terms in this record were read from the `LICENSE.txt` in the package `Microsoft.Data.SqlClient.SNI.runtime` 7.1.0 on 2026-11-03.

## The gate before and after

| State | Result |
| --- | --- |
| Original policy, WP1.8 persistence branch | The locked restore passed. The gate exited with **1**, and the only package it reported was `Microsoft.Data.SqlClient.SNI.runtime` 7.1.0, with the error "Unable to determine license from the given license file" (reported four times) |
| Policy commit cherry-picked | The gate exited with **0** and its table of errors was empty. The Release build had 0 warnings and 0 errors |
| Same state, `tests/Catalog.Architecture.Tests` | 135 passed, 0 failed |
| Same state, `tests/Governance.Auditor.Tests` | 624 passed, 0 failed |
| Same state, `governance security --base origin/phase/1-api-core` | Passed with 0 warnings, over the real lock files, in which the package appears in `src/Catalog.Api`, `src/Catalog.Infrastructure`, `tests/Catalog.Architecture.Tests`, and `tests/Catalog.Infrastructure.Tests` |

The old rule had to change. With the new override in place and the tests of the base commit restored (`DependencyPolicy.cs` and `DependencyPolicyTests.cs`), one of 107 architecture tests failed: `A_license_exception_for_a_tool_never_reaches_a_production_project`. The package is in a production lock file, and the old rule treated every override as tool-only.

## The two publishes

| Command | Files | Native Windows files | `Catalog.Api.deps.json` mentions the package | `Test-NoWindowsNativeRuntime.ps1` |
| --- | --- | --- | --- | --- |
| `dotnet publish src/Catalog.Api -c Release --no-restore -o <folder>` (no runtime identifier) | not counted | `runtimes/win-arm64/native/Microsoft.Data.SqlClient.SNI.dll`, `runtimes/win-x64/native/Microsoft.Data.SqlClient.SNI.dll`, and `runtimes/win-x86/native/Microsoft.Data.SqlClient.SNI.dll` | yes | Exit **1**, and it named the three files |
| `dotnet restore src/Catalog.Api -r linux-x64 -p:RestoreLockedMode=false -p:UseAppHost=false`, then `dotnet publish src/Catalog.Api -c Release -r linux-x64 --self-contained false -p:UseAppHost=false --no-restore -o <folder>` | 53 | none. The managed `Microsoft.Data.SqlClient.dll` and its resource assemblies are there | no | Exit **0** |

A publish for a Linux runtime identifier does not contain the package, and a publish with no runtime identifier does. The image build of WP2.1 must therefore publish for a Linux runtime identifier. The Linux restore changed the lock files of the throwaway branch, and the branch was deleted with those changes.

## What these runs do not show

- No image build exists yet, so nothing shows that a Phase 2 image has no file of the package. WP2.1 (the image build) and WP2.4 (the SBOM) are planned to check it, and the amendment says so.
- `Test-NoWindowsNativeRuntime.ps1` ran by hand on two publishes, and no pipeline step runs it yet.
- The launcher executable was turned off for the Linux publish (`-p:UseAppHost=false`), because it needs the launcher pack of the target. The launcher holds no file of the package.
- The exact-version pin was shown with synthetic lock files and overrides in the tests. No SqlClient update was applied.
- The persistence suites were not run on the throwaway branch, because they need SQL Server and belong to WP1.8.
