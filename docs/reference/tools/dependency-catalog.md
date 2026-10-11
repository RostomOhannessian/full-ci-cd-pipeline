---
title: "Dependency catalog"
description: "The libraries, Dev Container features, images, and actions that this repository uses and that need no page of their own: what each is for, why it was chosen, how it is referenced, and where to read more."
audience: [learners, contributors]
last-verified: 2026-10-11
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
