---
title: "lychee"
description: "The link checker that keeps internal links and anchors valid on every pull request and reports broken external links nightly."
audience: [learners, contributors]
tier: B
tools: [lychee]
introduced: "phase-0"
prerequisites: ["Docker"]
estimated-time: "10 minutes"
last-verified: 2026-10-08
verified-against: { lychee: "0.24.2" }
owner: "@RostomOhannessian"
---

# lychee

## Overview

lychee finds the links in Markdown and other text files and checks that each one works. A link can be a relative path to a file, a heading
anchor inside a file, or a web address. This repository runs lychee in two modes, because the two kinds of link fail for different reasons:

| Mode | Checks | Configuration | When it runs | Effect of a failure |
| --- | --- | --- | --- | --- |
| Internal | Relative links, file references, and heading anchors. No network | [lychee.toml](../../../lychee.toml) | Every pull request, as `Internal links` | Blocks the merge |
| External | Web addresses in the Markdown and in the tool inventory | [lychee-external.toml](../../../lychee-external.toml) | Nightly, and by hand | Fails the nightly run, which blocks nothing |

A page on someone else's server can go down on any day, so an external link must never stop a change that has nothing to do with it. An
internal link is under this repository's control, so a broken one is a defect in the change.

## Decision rationale

[ADR-0005](../../adr/0005-documentation-system.md) names lychee as the link check. It was chosen for four reasons, and no other link checker was evaluated.

- It checks anchors as well as files, which catches a renamed heading.
- It runs offline, which makes the pull request check fast and independent of the network.
- It runs from a container image pinned by digest in [tools/lint/compose.yaml](../../../tools/lint/compose.yaml).
- Its license is permissive: the repository carries both `LICENSE-APACHE` and `LICENSE-MIT`, and GitHub reports Apache-2.0 (checked 2026-10-08 on the
  [repository](https://github.com/lycheeverse/lychee)). The inventory records Apache-2.0.

The trade-off is that the nightly check can be noisy. The external configuration accepts the status 429 ("too many requests"), because a host that
asks to slow down has not broken the link, and it limits itself to two requests at a time for each host.

## Setup tutorial

**Prerequisites:** Docker.

1. Check the internal links. CI runs this step, which is a workflow step, so the lines below are YAML.

   [!code-bash[Check relative links and anchors](../../../.github/workflows/docs.yml#links)]

   Expected output ends with a one-line summary of the totals, which reports `0 Errors` when every link works.

2. Check the external links. The nightly run does this, and you can run it by hand when you add many links. It takes about a minute and needs the network.

   [!code-bash[Check external links](../../../.github/workflows/docs.yml#external-links)]

3. Fix a finding by correcting the link. When a web address has moved for good, replace it with the new address, because the nightly report
   counts every redirect.

**Cleanup:** nothing is left behind. The repository is mounted read-only.

## Further research

- [lychee](https://lychee.cli.rs/): the project home (checked 2026-10-08)
- [The command line guide](https://lychee.cli.rs/guides/cli/): options such as `--offline` and `--include-fragments` (checked 2026-10-08)
- [The configuration guide](https://lychee.cli.rs/guides/config/): the `lychee.toml` keys (checked 2026-10-08)
- [The lychee repository](https://github.com/lycheeverse/lychee): releases and the license files (checked 2026-10-08)
