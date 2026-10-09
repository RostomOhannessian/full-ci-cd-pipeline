---
title: "Tool reference"
description: "How tools are inventoried, tiered, and documented, and what every tool page must contain."
audience: [learners, contributors, maintainers]
last-verified: 2026-10-08
owner: "@RostomOhannessian"
---

# Tool reference

Every tool, library, service, and standard this project uses, plans to use, or evaluated and rejected is listed in
[the tool inventory](inventory.yaml). The inventory is data: one entry per tool with its category, tier, lifecycle, license, version, sources, and
the ADRs that concern it. Its shape is [the inventory schema](inventory.schema.json). Tool pages (written when a tool first appears) are the human-readable side.

## Tool pages today

A page is named after its inventory ID. Tier C libraries are in [the dependency catalog](dependency-catalog.md).

| Tool | Tier | What it does here |
| --- | --- | --- |
| [DocFX](docfx.md) | A | Builds the documentation site and the API reference |
| [cspell](cspell.md) | B | Checks spelling |
| [lychee](lychee.md) | B | Checks internal links on every pull request and external links nightly |
| [markdownlint-cli2](markdownlint-cli2.md) | B | Enforces the Markdown house style |
| [Mermaid CLI](mermaid-cli.md) | B | Proves that every diagram renders |
| [Documentation.Auditor](documentation-auditor.md) | B | Checks the inventory, the tool pages, and every page |

Eighteen tools that are in use have no page yet, and WP1.13 owes them. The audit reports each as a note, and it fails when WP1.13 is completed and a page is still missing.

## What a tool page contains

The four sections below are required for every tool. They are what a developer who knows the syntax but not the tool needs.

| Section | What it answers |
| --- | --- |
| Overview | What problem does the tool solve, what are its core concepts, and what is its role in this repository? |
| Decision rationale | What else was considered, why this one, what are the tradeoffs, and what are its license and maintenance posture? Links the ADR instead of repeating it. |
| Setup tutorial | Prerequisites, the pinned version, installation or containerized use, repository configuration, commands with expected output, and cleanup. |
| Further research | Curated, authoritative sources: official versioned documentation, standards, and primary specifications. |

## Tiers

The tier sets how much a page must say.

| Tier | Applies to | Required sections |
| --- | --- | --- |
| A | Services, platforms, and frameworks whose concepts a learner must understand, for example Vault, Kyverno, Argo CD, Terraform, EF Core, Testcontainers | The four sections above, plus **How this project uses it**, **Validation and troubleshooting**, **Security and operations**, and **Lab** |
| B | Supporting command-line and developer tools, for example linters and generators | The four sections above, in compact form |
| C | Libraries and standards with little to teach beyond usage | An entry in the dependency catalog with purpose, rationale, setup snippet, and sources. Libraries with real concepts also get an A or B page. |

Start a page from [the tool page template](../../templates/tool-page-template.md).

## Lifecycle

| Lifecycle | Meaning |
| --- | --- |
| `adopt` | Decided and planned. The page is written when the tool is first used. |
| `trial` | Adopted with explicit caveats: preview, beta, dormant upstream, or not yet proven by a spike. The page states the caveats. |
| `assess` | Not decided. A work package chooses, and an ADR records the choice. |
| `hold` | Evaluated and not adopted, or to avoid for new work. Kept so the reasoning is not lost, for example OpenBao for SQL Server credentials, ingress-nginx, and tfsec. |
| `retired` | Used once and removed. None yet. |

The `usage` field says where a tool is in practice: `planned`, `in-use`, `evaluated-only`, or `removed`. A tool becomes `in-use` in the work package that first runs it, so the field shows what the repository does today and not what the plan intends.

## How the inventory is kept honest

- **Verified facts.** Licenses and versions were read from the source on the date in `last-verified`. A value of `unverified` means exactly that.
  Versions are a point-in-time record. The pins live in `Directory.Packages.props`, `.config/dotnet-tools.json`, the workflows, `tools/lint/compose.yaml`,
  and later `tools/versions.yaml`, Terraform lock files, and manifests. The auditor compares each pin with the recorded version.
- **Discovery.** The [Documentation.Auditor](documentation-auditor.md) reads the files that name tools, and an entry tells it what to look for with `detect` rules,
  each written as `kind:pattern`. The kinds are `nuget` (a package ID), `dotnet-tool` (a tool package ID), `action` (`owner/repo`), `image` (a repository
  without its tag), and `file` (a glob). A trailing `*` matches any ending. The scanners today read package and project files, the local tool manifest,
  workflows, Compose files, and Dockerfiles. Anything the auditor finds that no entry detects fails the build, and so does an entry that says `planned` for a tool in use.
- **Formats not read yet.** Terraform, Helm charts, Kubernetes manifests, Kyverno policies, the Dev Container, and `tools/versions.yaml` arrive in later work packages. The
  policy lists each with the work package that adds it, and the audit fails as soon as one of these files exists without a scanner.
- **Pages.** A tool that is in use and has Tier A or B needs a page named `<id>.md` in this folder. A Tier C library needs a level-2 section in
  [the dependency catalog](dependency-catalog.md). A page that a later work package owes is listed in the policy with that work package.
- **Freshness.** Entries and pages more than 180 days past `last-verified` are flagged for review.
- **Licenses.** Linked dependencies follow [ADR-0004](../../adr/0004-licensing-and-dependency-license-policy.md). Every tool whose license is not permissive
  carries a `notes` entry that says why it is acceptable, and the Documentation.Auditor enforces that.

## Adding or changing a tool

1. Add or update the entry in the inventory, with verified license and version and primary sources. Add `detect` rules if the repository uses the tool, and set
   `usage` to `in-use` in the work package that first runs it.
2. If the choice is a significant decision, write or update an ADR, and link it from the entry.
3. Write the page when the tool is first used, from the template, at the right tier, and name it after the inventory ID.
4. Add the tool's ID to the `tools:` list in the front matter of every page that depends on it.
5. Run `dotnet run --project tools/Documentation.Auditor -- audit`. It fails when the inventory, the files, and the pages disagree.
