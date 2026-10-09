---
title: "Dependency catalog"
description: "The libraries that this repository uses and that need no page of their own: what each is for, why it was chosen, how it is referenced, and where to read more."
audience: [learners, contributors]
last-verified: 2026-10-08
owner: "@RostomOhannessian"
---

# Dependency catalog

This page holds one entry for each Tier C library in [the tool inventory](inventory.yaml): a library with little to teach beyond usage. The
[Documentation.Auditor](documentation-auditor.md) checks that every Tier C library that is in use has a level-2 heading here with the library's name.
A library that carries real concepts, such as FusionCache or FluentValidation, gets a Tier A or B tool page instead.

Every version lives in `Directory.Packages.props` and nowhere else ([ADR-0020](../../adr/0020-dependency-intake-controls.md)), so a project
names a package without a version.

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
