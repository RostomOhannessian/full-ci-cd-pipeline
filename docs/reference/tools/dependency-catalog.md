---
title: "Dependency catalog"
description: "The libraries, Dev Container features, images, and actions that this repository uses and that need no page of their own: what each is for, why it was chosen, how it is referenced, and where to read more."
audience: [learners, contributors]
last-verified: 2026-11-03
owner: "@RostomOhannessian"
---

# Dependency catalog

This page holds one entry for each Tier C tool in [the tool inventory](inventory.yaml): a library, a Dev Container feature, an image, or an action with little to teach beyond usage. The
[Documentation.Auditor](documentation-auditor.md) checks that every Tier C tool that is in use has a level-2 heading here with the tool's name.
A library that carries real concepts, such as FusionCache or FluentValidation, gets a Tier A or B tool page instead.

The version of every library lives in `Directory.Packages.props` and nowhere else ([ADR-0020](../../adr/0020-dependency-intake-controls.md)), so a project
names a package without a version. The pin of each other entry is in the file that the entry names.

## System.CommandLine

**Purpose.** Parses the command line of the two auditors: commands, options, help text, and exit codes.

**Rationale.** It is a stable release from the .NET team with no dependencies on net10.0, and it is MIT licensed. The alternatives were
to parse the arguments by hand or to add a larger framework. [ADR-0021](../../adr/0021-governance-auditor-cli.md) records the choice. Version
2.0.12 was checked on nuget.org on 2026-10-08, a 3.0 release candidate exists, and the pin stays on 2.0.x until 3.0 is stable.

**Setup.** The project names the package, and the central file holds the version.

```xml
<PackageReference Include="System.CommandLine" />
```

**Used by.** `tools/Governance.Auditor` and `tools/Documentation.Auditor`.

**Sources.**

- [command-line-api repository](https://github.com/dotnet/command-line-api) (checked 2026-10-08)
- [System.CommandLine 2.0.12 on NuGet](https://www.nuget.org/packages/System.CommandLine/2.0.12) (checked 2026-10-08)

## YamlDotNet

**Purpose.** Reads the YAML files that drive the tools: the status file, the requirements and evidence registers, the policy files, the tool
inventory, workflow files, and the front matter of Markdown pages.

**Rationale.** It is the established YAML library for .NET, it is MIT licensed, and it has no dependencies on net10.0. The tools refuse YAML anchors
and aliases, because the library resolves an alias to the node it names, so a nested alias could multiply a small file into a huge tree.
[ADR-0021](../../adr/0021-governance-auditor-cli.md) records the choice. Version 18.1.0 was checked on nuget.org on 2026-10-08.

**Setup.** The project names the package, and the central file holds the version.

```xml
<PackageReference Include="YamlDotNet" />
```

**Used by.** `tools/Governance.Auditor` and `tools/Documentation.Auditor`.

**Sources.**

- [YamlDotNet repository](https://github.com/aaubry/YamlDotNet) (checked 2026-10-08)
- [YamlDotNet 18.1.0 on NuGet](https://www.nuget.org/packages/YamlDotNet/18.1.0) (checked 2026-10-08)

## Docker-in-Docker Dev Container feature

**Purpose.** Runs a Docker daemon inside the Dev Container, so Compose, kind, and Testcontainers work without the container sharing the host's Docker socket.

**Rationale.** A container that mounts the host's socket controls every container on the host, and a daemon of its own keeps the stack, its images, and its volumes inside the
container. The cost is that the feature runs the container privileged, and [ADR-0025](../../adr/0025-dev-command-and-dev-container.md) and
[the threat model](../../security/threat-model.md) record it. The feature is MIT licensed. Version 4.1.3 is locked (checked 2026-10-10).

**Setup.** The configuration names the feature with its major version, and the lock file pins the exact version and digest.

```json
"ghcr.io/devcontainers/features/docker-in-docker:4": { "moby": false }
```

**Used by.** `.devcontainer/devcontainer.json` and `.devcontainer/devcontainer-lock.json`.

**Sources.**

- [The feature's source and options](https://github.com/devcontainers/features/tree/main/src/docker-in-docker) (checked 2026-10-10)
- [Dev Container features](https://containers.dev/features) (checked 2026-10-10)

## GitHub CLI Dev Container feature

**Purpose.** Installs the GitHub CLI in the Dev Container, for the maintainer's pull request and Issue work.

**Rationale.** The command line is the supported way to script GitHub from the terminal, and the feature installs it without a line in the Dockerfile. The feature is MIT licensed. Version 1.1.3 is
locked (checked 2026-10-10). The feature installs the latest GitHub CLI release unless a version is set, and the tool itself is the `github-cli` entry in the inventory.

**Setup.** The configuration names the feature with its major version, and the lock file pins the exact version and digest.

```json
"ghcr.io/devcontainers/features/github-cli:1": {}
```

**Used by.** `.devcontainer/devcontainer.json` and `.devcontainer/devcontainer-lock.json`.

**Sources.**

- [The feature's source and options](https://github.com/devcontainers/features/tree/main/src/github-cli) (checked 2026-10-10)
- [Dev Container features](https://containers.dev/features) (checked 2026-10-10)

## Microsoft .NET Dev Container image

**Purpose.** Is the base of the Dev Container, with the .NET SDK, Git, PowerShell, and a non-root user.

**Rationale.** The image carries the SDK that `global.json` pins, so the Dockerfile needs no installer for it, and Microsoft maintains it. The image is MIT licensed, and it contains software under other
licenses, as every Linux distribution image does. The repository links none of it. The tag `10.0` carried SDK 10.0.401 on Ubuntu 24.04 when the digest was pinned (checked 2026-10-10).

**Setup.** The Dockerfile names the image with its tag and digest, so a rebuild gives the same tools, and Dependabot proposes updates to both.

```dockerfile
FROM mcr.microsoft.com/devcontainers/dotnet:10.0@sha256:694076b55f447d4b9f5b49bc6616c2e904d63e1a61e43680b2bbea46011b6783
```

**Used by.** `.devcontainer/Dockerfile`.

**Sources.**

- [The image's source and tags](https://github.com/devcontainers/images/tree/main/src/dotnet) (checked 2026-10-10)
- [The image on the Microsoft Artifact Registry](https://mcr.microsoft.com/en-us/artifact/mar/devcontainers/dotnet) (checked 2026-10-10)

## Microsoft .NET SDK image

**Purpose.** Runs the mutation tests in Linux with the SDK that `global.json` pins. Stryker.NET cannot start on a Windows host that enforces Smart App Control, because many of its files are not signed (spike 1.c), so the
mutation service runs it in this image, and a Windows developer needs only Docker.

**Rationale.** It is the image that spike 1.c ran Stryker.NET in, it carries SDK 10.0.401 as `global.json` requires, and Microsoft maintains it. The repository's own Dockerfile image is the Dev Container base, which has a non-root user and tools
that a one-shot mutation run does not need, so the service uses the plain SDK image. It is MIT licensed, and it contains software under other licenses, as every Linux distribution image does. The repository links none of it. Dependabot proposes updates to
the tag and the digest, because the Compose file is in a folder that it watches (checked 2026-10-12). [ADR-0029](../../adr/0029-domain-model-and-mutation-baseline.md) records the choice.

**Setup.** The Compose file names the image with its tag and digest, so a run gives the same tools.

```yaml
image: mcr.microsoft.com/dotnet/sdk:10.0.401@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317
```

**Used by.** The `mutation` service in `tools/lint/compose.yaml`.

**Sources.**

- [The image's source and tags](https://github.com/dotnet/dotnet-docker) (checked 2026-10-12)
- [The image on the Microsoft Artifact Registry](https://mcr.microsoft.com/en-us/artifact/mar/dotnet/sdk) (checked 2026-10-12)

## devcontainers/ci

**Purpose.** Builds the Dev Container on a hosted runner and runs the onboarding commands in it.

**Rationale.** It is the action that the Dev Containers project publishes for this job: it builds the image, starts the container, runs the lifecycle commands, and runs a command in it. The alternative
was to call the Dev Containers CLI from a script, which repeats what the action does. It is a third-party action, so it is pinned by commit SHA (release v0.3.1900000450, checked 2026-10-10), it runs with
`push: never` and a read-only token, and it is MIT licensed.

**Setup.** The workflow names the action by SHA, with the release in a comment, and passes the command to run.

[!code-bash[Run the onboarding in a Dev Container](../../../.github/workflows/devcontainer.yml#onboarding)]

**Used by.** `.github/workflows/devcontainer.yml`.

**Sources.**

- [The action's repository](https://github.com/devcontainers/ci) (checked 2026-10-10)
- [The GitHub Action guide](https://github.com/devcontainers/ci/blob/main/docs/github-action.md) (checked 2026-10-10)

## Microsoft.OpenApi

**Purpose.** Supplies the object model of an OpenAPI document, so code can read the authored contract as typed objects: paths, operations, parameters, schemas, and security requirements. The contract tests use it to check the
contract against the documents that must agree with it. The standard that it models is on [the OpenAPI page](openapi.md).

**Rationale.** It is the library that ASP.NET Core 10 uses for its own OpenAPI document, so the contract tests read the contract with the model that the API will write its document with, and WP1.10 can compare like with like
([ADR-0027](../../adr/0027-contract-linter-and-conformance-tooling.md)). It is MIT licensed, and version 2 supports OpenAPI 3.1. The pin is 2.12.2, and the latest release is 3.10.2 (checked 2026-10-11 on nuget.org). The repository stays on 2.x,
because Microsoft.AspNetCore.OpenApi 10.0.12 depends on Microsoft.OpenApi from 2.12.0 and below 3.0.0 (checked 2026-10-11 in the package metadata on nuget.org). [ADR-0017](../../adr/0017-contract-first-api-design.md) and ADR-0027 record the choice.

**Setup.** The project names the package, and the central file holds the version.

```xml
<PackageReference Include="Microsoft.OpenApi" />
```

**Used by.** `tests/Catalog.Contract.Tests`. WP1.10 adds the API's own reference (planned).

**Sources.**

- [OpenAPI.NET repository](https://github.com/Microsoft/OpenAPI.NET) (checked 2026-10-11)
- [Microsoft.OpenApi 2.12.2 on NuGet](https://www.nuget.org/packages/Microsoft.OpenApi/2.12.2) (checked 2026-10-11)
- [OpenAPI.NET release v2.12.2](https://github.com/Microsoft/OpenAPI.NET/releases/tag/v2.12.2) (checked 2026-10-11)
- [Upgrade guide to OpenAPI.NET 2.0](https://github.com/microsoft/OpenAPI.NET/blob/main/docs/upgrade-guide-2.md) (checked 2026-10-11)

## Microsoft.OpenApi.YamlReader

**Purpose.** Reads the authored YAML contract into the Microsoft.OpenApi object model for the contract tests. The core package of version 2 reads and writes JSON, and YAML needs this package (checked 2026-10-11 on the upgrade guide).

**Rationale.** The contract is YAML, so the tests need a YAML reader, and this package comes from the same repository, with the same version, as the core library. It is MIT licensed, and version 2.12.2 depends on Microsoft.OpenApi 2.12.2 and on SharpYaml 2.1.5, which is MIT licensed
(checked 2026-10-11). It stays on the 2.x line with the core library. Only the contract test project references it, and no production project does. [ADR-0027](../../adr/0027-contract-linter-and-conformance-tooling.md) records the choice.

**Setup.** The project names the package, and the central file holds the version. The code registers the reader on the reader settings before it parses YAML.

```xml
<PackageReference Include="Microsoft.OpenApi.YamlReader" />
```

```csharp
var settings = new OpenApiReaderSettings();
settings.AddYamlReader();
```

**Used by.** `tests/Catalog.Contract.Tests`, in `Support/Authored.cs`.

**Sources.**

- [OpenAPI.NET repository](https://github.com/Microsoft/OpenAPI.NET) (checked 2026-10-11)
- [Microsoft.OpenApi.YamlReader 2.12.2 on NuGet](https://www.nuget.org/packages/Microsoft.OpenApi.YamlReader/2.12.2) (checked 2026-10-11)
- [Upgrade guide to OpenAPI.NET 2.0](https://github.com/microsoft/OpenAPI.NET/blob/main/docs/upgrade-guide-2.md) (checked 2026-10-11)

## Microsoft.Extensions.TimeProvider.Testing

**Purpose.** Supplies `FakeTimeProvider`, a `TimeProvider` that a test controls. The domain model takes the time from a `TimeProvider` argument, and a test starts the fake at a fixed instant, moves it with `Advance` or `SetUtcNow`, and
checks the exact time stamp of each event. No test reads the system clock, so a result does not depend on the host or the moment (plan section 10.3, REQ-QUA-004).

**Rationale.** `TimeProvider` is part of .NET 8 and later, so the model needs no clock interface of its own, and this package is the maintained fake for it. It is MIT licensed, version 10.10.0 has no dependency beyond the framework for
`net10.0`, and it stays on the 10.x line with the framework (checked 2026-10-12 in the package metadata on nuget.org). [ADR-0029](../../adr/0029-domain-model-and-mutation-baseline.md) records the choice.

**Setup.** The test project names the package, and the central file holds the version. A test builds the fake at a fixed instant.

```xml
<PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" />
```

```csharp
var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 15, 9, 30, 0, TimeSpan.Zero));
clock.Advance(TimeSpan.FromMinutes(5));
```

**Used by.** `tests/Catalog.Domain.Tests`, in `Support/Make.cs`. Only test projects reference it.

**Sources.**

- [Microsoft.Extensions.TimeProvider.Testing 10.10.0 on NuGet](https://www.nuget.org/packages/Microsoft.Extensions.TimeProvider.Testing/10.10.0) (checked 2026-10-12)
- [The extensions repository](https://github.com/dotnet/extensions) (checked 2026-10-12)
- [TimeProvider and ITimer in .NET](https://learn.microsoft.com/dotnet/core/extensions/timeprovider) (checked 2026-10-12)

## Microsoft.Data.SqlClient.SNI.runtime

**Purpose.** Holds the native Windows networking library (the SQL Server Network Interface, SNI) that Microsoft.Data.SqlClient loads on a Windows host. The package contains three libraries and nothing else: `runtimes/win-x64`, `runtimes/win-x86`, and `runtimes/win-arm64`. On Linux, SqlClient does not use it. Its description says that it is an internal implementation package that no project should reference directly.

**Rationale.** Microsoft.Data.SqlClient 7.1.1 and 6.1.7 both depend on it (checked 2026-11-03 in their package metadata), so the repository cannot link the driver without it in the dependency graph. Its license is the Microsoft Software License Terms for the SNI library, which are not OSI-approved, and `nuget-license` cannot map them to an allowed license. The terms allow use "to develop and test your applications". They allow distribution of the object code only inside an application, with protective terms for distributors and users and an indemnity for Microsoft (checked 2026-11-03 in the package's `LICENSE.txt`). The repository accepts the package as a narrow exception, because no shipped artifact contains it: the Phase 2 images are Linux-only, and a publish for a Linux runtime identifier does not copy it. A publish with no runtime identifier does copy the Windows libraries, so the image build must publish for a Linux runtime identifier. [ADR-0004](../../adr/0004-licensing-and-dependency-license-policy.md#amendment-of-2026-11-03-a-native-windows-runtime-that-no-shipped-artifact-contains) records the decision, the conditions that end it, and which checks are enforced today and which are planned. The exception names version 7.1.0 exactly, so a SqlClient update that moves the package fails the license gate and the architecture tests until the new terms are read.

**Setup.** No project names the package, and `Directory.Packages.props` does not pin it. It arrives through Microsoft.Data.SqlClient. The exception lives in the license policy files, where the override records its kind, its scope, and its reason (excerpt).

```json
{ "Id": "Microsoft.Data.SqlClient.SNI.runtime", "Version": "7.1.0", "Kind": "windows-native-runtime" }
```

**Used by.** Planned in WP1.8: every project that restores Microsoft.Data.SqlClient, which is `src/Catalog.Infrastructure` and the projects that reference it, as a transitive dependency. Only a Windows host loads the library, for development and for tests. `governance/policies/license-overrides.json`, `governance/policies/license-policy.json`, and `governance/policies/security-policy.yaml` hold the exception, and `tests/Catalog.Architecture.Tests` and `tools/ci/Test-NoWindowsNativeRuntime.ps1` check it.

**Sources.**

- [Microsoft.Data.SqlClient.SNI.runtime 7.1.0 on NuGet](https://www.nuget.org/packages/Microsoft.Data.SqlClient.SNI.runtime/7.1.0) (checked 2026-11-03)
- [The license of the package on NuGet](https://www.nuget.org/packages/Microsoft.Data.SqlClient.SNI.runtime/7.1.0/license) (checked 2026-11-03)
- [The SqlClient project](https://aka.ms/sqlclientproject) (checked 2026-11-03)

## FluentValidation

**Purpose.** Checks the shape of a command or a query before its handler runs. A validator is a class with one rule for each field, and it reports every invalid field at once, so a caller fixes a request in one round trip. The validation decorator of `Catalog.CrossCutting`
runs the validators of a request and turns their failures into the `validation-failed` error of [the error catalog](../error-catalog.md) (REQ-APP-005).

**Rationale.** The plan names FluentValidation for the validators (plan section 17). It is Apache-2.0, which the allow-list of [ADR-0004](../../adr/0004-licensing-and-dependency-license-policy.md) accepts, and version 12.1.1 has no dependency on net8.0 or later (checked 2026-11-01 in the package metadata on nuget.org).
The package stays in `Catalog.CrossCutting`, because the application layer depends on the domain model and the base class library only ([ADR-0032](../../adr/0032-decorator-pipeline-and-application-results.md)). Version 12.2.0 was released on 2026-10-10, and the repository keeps the planned version until Dependabot proposes the update.

**Setup.** The project names the package, and the central file holds the version. A validator derives from `AbstractValidator<T>`.

```xml
<PackageReference Include="FluentValidation" />
```

```csharp
RuleFor(command => command.Name).NotEmpty().WithMessage("The name is required.");
```

**Used by.** `src/Catalog.CrossCutting`, in `Decorators/ValidationDecorators.cs`. The validators of the use cases arrive in the third pull request of WP1.7 (planned).

**Sources.**

- [FluentValidation 12.1.1 on NuGet](https://www.nuget.org/packages/FluentValidation/12.1.1) (checked 2026-11-01)
- [The FluentValidation documentation](https://fluentvalidation.net/) (checked 2026-11-01)

## Microsoft.Data.SqlClient

**Purpose.** The ADO.NET driver that speaks the SQL Server protocol (TDS). EF Core uses it for every connection of the catalog, and it keeps a pool of open connections for each distinct connection string. The pool is what the credential rotation of the persistence layer works with:
a new credential is a new connection string, so it gets a new pool, and `SqlConnection.ClearPool` closes the idle connections of the old one (REQ-DATA-005, [spike 1.e](../../research/spikes/1.e-rotating-credentials.md)).

**Rationale.** EF Core's SQL Server provider depends on it, and plan section 17 names version 7.1.1, which spikes 1.d and 1.e ran. It is MIT licensed, which the allow-list of [ADR-0004](../../adr/0004-licensing-and-dependency-license-policy.md) accepts. The provider 10.0.14 depends on 6.1.7, so `Directory.Packages.props` pins 7.1.1, and transitive pinning makes the pin win. Version 7.1.1
depends on `System.Security.Cryptography.Pkcs` 9.0.18, which has two advisories (GHSA-2fmv-rv2c-59x7 and GHSA-846f-f764-76mx, patched in 9.0.21 and 10.0.13), so the same file pins that package to 10.0.14 ([ADR-0020](../../adr/0020-dependency-intake-controls.md), [ADR-0033](../../adr/0033-ef-core-mapping-read-model-and-migrations.md), checked 2026-11-02).
The driver also depends on `Microsoft.Data.SqlClient.SNI.runtime`, a native Windows library under Microsoft Software License Terms. The license exception for it is the amendment of 2026-11-03 to [ADR-0004](../../adr/0004-licensing-and-dependency-license-policy.md) (see the section for the package above, checked 2026-11-03).

**Setup.** The project names the packages, and the central file holds the versions. SqlClient does not support the invariant globalization mode, so a project that opens a connection must run with ICU, and the persistence tests turn the mode off.

```xml
<PackageReference Include="Microsoft.Data.SqlClient" />
```

**Used by.** `src/Catalog.Infrastructure` (the persistence layer and the credential sources) and `tests/Catalog.Infrastructure.Tests` (the probes that read the database itself).

**Sources.**

- [Microsoft.Data.SqlClient 7.1.1 on NuGet](https://www.nuget.org/packages/Microsoft.Data.SqlClient/7.1.1) (checked 2026-11-02)
- [The SqlClient project](https://aka.ms/sqlclientproject) (checked 2026-11-02)

## System.Security.Cryptography.Pkcs

**Purpose.** A transitive dependency of Microsoft.Data.SqlClient that the repository pins and never calls. It holds the signed-message and certificate-request types of the .NET cryptography library.

**Rationale.** SqlClient 7.1.1 depends on version 9.0.18, which has two published advisories: GHSA-2fmv-rv2c-59x7 (moderate) and GHSA-846f-f764-76mx (high, quadratic CPU use in `SignedCms.CheckSignature`). NuGet audit fails the restore on both, and the fix that [ADR-0020](../../adr/0020-dependency-intake-controls.md) prescribes is a central pin to a patched version. The pin is 10.0.14, which matches the runtime of the solution and is above the patched 10.0.13 (checked 2026-11-02, [ADR-0033](../../adr/0033-ef-core-mapping-read-model-and-migrations.md)). The package is MIT licensed.

**Setup.** One line in `Directory.Packages.props`, and no project references the package.

```xml
<PackageVersion Include="System.Security.Cryptography.Pkcs" Version="10.0.14" />
```

**Used by.** Every project that restores Microsoft.Data.SqlClient, which is `src/Catalog.Infrastructure` and the projects that reference it. Remove the pin when a SqlClient release depends on a patched version.

**Sources.**

- [GHSA-846f-f764-76mx](https://github.com/advisories/GHSA-846f-f764-76mx) (checked 2026-11-02)
- [GHSA-2fmv-rv2c-59x7](https://github.com/advisories/GHSA-2fmv-rv2c-59x7) (checked 2026-11-02)
- [System.Security.Cryptography.Pkcs 10.0.14 on NuGet](https://www.nuget.org/packages/System.Security.Cryptography.Pkcs/10.0.14) (checked 2026-11-02)
