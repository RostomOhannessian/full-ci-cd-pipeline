namespace Documentation.Auditor.Tests.Support;

/// <summary>
/// A small repository that passes every check. A test changes one file and expects one finding, so each rule is shown to fail on a
/// repository that is otherwise correct. The policy is a reduced copy, and the inventory schema is the real one.
/// </summary>
internal static class Samples
{
    public const string Policy = """
schema-version: 1
inventory:
  path: docs/reference/tools/inventory.yaml
  schema: docs/reference/tools/inventory.schema.json
  adr-directory: docs/adr
  permissive-licenses: [MIT, Apache-2.0]
  no-license-terms: [service, n/a]
freshness:
  warn-after-days: 180
tool-pages:
  directory: docs/reference/tools
  catalog: docs/reference/tools/dependency-catalog.md
  sections:
    A: [Overview, Decision rationale, Setup tutorial, How this project uses it, Validation and troubleshooting, Security and operations, Lab, Further research]
    B: [Overview, Decision rationale, Setup tutorial, Further research]
discovery:
  unsupported-sources:
    - pattern: '**/*.tf'
      owner: WP3.1
      description: Terraform providers and modules
pages:
  exclude: ['docs/templates/**']
  front-matter:
    - id: adr
      paths: ['docs/adr/*-*.md']
      required: [title, description, status, date, decision-makers]
      present: [supersedes, superseded-by, tools, related-adrs, evidence]
    - id: tool-page
      paths: ['docs/reference/tools/*.md']
      exclude: ['docs/reference/tools/index.md', 'docs/reference/tools/dependency-catalog.md']
      required: [title, description, audience, tier, tools, introduced, prerequisites, estimated-time, last-verified, verified-against, owner]
    - id: page
      paths: ['docs/**/*.md']
      required: [title, description, audience, last-verified, owner]
  command-blocks:
    paths: ['docs/tutorials/**', 'docs/reference/**']
    languages: [bash, sh, powershell, pwsh]
    plain-languages: ['', text]
    commands: [dotnet, docker, git, './']
    marker: illustrative
  includes:
    allowed-roots: [scripts/, src/, tools/, .github/workflows/]
""";

    public const string Inventory = """
schema-version: 1
last-verified: 2026-10-01
entries:
  - id: acme-lint
    name: Acme Lint
    category: docs-tool
    tier: B
    lifecycle: adopt
    usage: in-use
    introduced: phase-1
    purpose: Lints the documentation.
    license: MIT
    license-source: https://example.test/acme-lint/license
    version: "1.0.0"
    sources:
      - https://example.test/acme-lint
    adrs: ["0001"]
    detect: ["image:registry.example.test/acme/lint"]
  - id: acme-lib
    name: Acme.Lib
    category: dotnet-library
    tier: C
    lifecycle: adopt
    usage: in-use
    introduced: phase-1
    purpose: A library.
    license: Apache-2.0
    license-source: https://example.test/acme-lib/license
    version: "1.2.3"
    sources:
      - https://example.test/acme-lib
    adrs: []
    detect: ["nuget:Acme.Lib"]
  - id: acme-platform
    name: Acme Platform
    category: kubernetes
    tier: A
    lifecycle: adopt
    usage: planned
    introduced: phase-3
    purpose: A platform that is planned.
    license: Apache-2.0
    license-source: https://example.test/acme-platform/license
    version: "2.0.0"
    sources:
      - https://example.test/acme-platform
    adrs: []
  - id: old-tool
    name: Old tool
    category: gitops
    tier: n/a
    lifecycle: hold
    usage: evaluated-only
    introduced: n/a
    purpose: A tool that was evaluated.
    license: BUSL-1.1
    license-source: https://example.test/old-tool/license
    version: "3.1.0"
    sources:
      - https://example.test/old-tool
    adrs: []
    notes: Held because it is not an open source license, and it is not linked or offered as a service.
""";

    public const string ToolPage = """
---
title: "Acme Lint"
description: "A linter for the documentation."
audience: [contributors]
tier: B
tools: [acme-lint]
introduced: "phase-1"
prerequisites: ["Docker"]
estimated-time: "10 minutes"
last-verified: 2026-10-01
verified-against: { acme-lint: "1.0.0" }
owner: "@owner"
---

# Acme Lint

## Overview

What it does.

## Decision rationale

Why it was chosen.

## Setup tutorial

How to run it.

## Further research

- [Acme Lint documentation](https://example.test/acme-lint)
""";

    public const string Catalog = """
---
title: "Dependency catalog"
description: "Libraries with little to teach beyond usage."
audience: [contributors]
last-verified: 2026-10-01
owner: "@owner"
---

# Dependency catalog

## Acme.Lib

A library that parses things.
""";

    public const string Status = """
schema-version: 1
phases:
  - id: 1
    work-packages:
      - id: WP1.3
        state: in_progress
      - id: WP1.13
        state: planned
""";

    public static string Page(string title = "A page", string verified = "2026-10-01", string body = "Text.") => $"""
---
title: "{title}"
description: "A page."
audience: [contributors]
last-verified: {verified}
owner: "@owner"
---

# {title}

{body}
""";

    public static string Adr() => """
---
title: "ADR-0001: A decision"
description: "A decision."
status: accepted
date: 2026-10-01
decision-makers: ["@owner"]
supersedes: []
superseded-by: []
tools: [acme-lint]
related-adrs: []
evidence: []
---

# ADR-0001: A decision

Text.
""";

    public static TestRepository Baseline() =>
        new TestRepository()
            .Add("governance/policies/documentation-policy.yaml", Policy)
            .CopyFromRepository("docs/reference/tools/inventory.schema.json")
            .Add("docs/reference/tools/inventory.yaml", Inventory)
            .Add("docs/reference/tools/acme-lint.md", ToolPage)
            .Add("docs/reference/tools/dependency-catalog.md", Catalog)
            .Add("docs/reference/tools/index.md", Page("Tool reference"))
            .Add("docs/adr/0001-a-decision.md", Adr())
            .Add("docs/index.md", Page("Documentation"))
            .Add("docs/project/status.yaml", Status)
            .Add("Directory.Packages.props", """
<Project>
  <ItemGroup>
    <PackageVersion Include="Acme.Lib" Version="1.2.3" />
  </ItemGroup>
</Project>
""")
            .Add("tools/lint/compose.yaml", """
services:
  lint:
    image: registry.example.test/acme/lint:v1.0.0@sha256:0000000000000000000000000000000000000000000000000000000000000000
""");
}
