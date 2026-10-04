---
title: "Tool reference"
description: "How tools are inventoried, tiered, and documented, and what every tool page must contain."
audience: [learners, contributors, maintainers]
last-verified: 2026-10-04
owner: "@RostomOhannessian"
---

# Tool reference

Every tool, library, service, and standard this project uses, plans to use, or evaluated and rejected is listed in
[the tool inventory](inventory.yaml). The inventory is data: one entry per tool with its category, tier, lifecycle, license, version, sources, and
the ADRs that concern it. Tool pages (written when a tool first appears) are the human-readable side.

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

The `usage` field says where a tool is in practice: `planned`, `in-use`, `evaluated-only`, or `removed`. Today every adopted tool is `planned`.

## How the inventory is kept honest

- **Verified facts.** Licenses and versions were read from the source on the date in `last-verified`. A value of `unverified` means exactly that.
  Versions are a point-in-time record. The pins live in `Directory.Packages.props`, `tools/versions.yaml`, Terraform lock files, and manifests.
- **Discovery.** From WP1.3, the Documentation.Auditor scans package files, workflows, Terraform providers, Helm charts, Compose files, and Dev Container
  features. Anything it finds that is missing from the inventory, or has no page when its lifecycle requires one, fails the build.
- **Freshness.** Entries and pages more than 180 days past `last-verified` are flagged for review.
- **Licenses.** Linked dependencies follow [ADR-0004](../../adr/0004-licensing-and-dependency-license-policy.md). Every tool whose license is not permissive
  carries a `notes` entry that says why it is acceptable, and the Documentation.Auditor enforces that from WP1.3.

## Adding or changing a tool

1. Add or update the entry in the inventory, with verified license and version and primary sources.
2. If the choice is a significant decision, write or update an ADR, and link it from the entry.
3. Write the page when the tool is first used, from the template, at the right tier.
4. Add the tool's ID to the `tools:` list in the front matter of every page that depends on it.
