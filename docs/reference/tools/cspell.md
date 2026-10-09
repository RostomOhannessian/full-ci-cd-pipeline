---
title: "cspell"
description: "The spell checker that catches typos in the documentation, the prompts, and the configuration before they are published."
audience: [learners, contributors]
tier: B
tools: [cspell]
introduced: "phase-1"
prerequisites: ["Docker"]
estimated-time: "10 minutes"
last-verified: 2026-10-08
verified-against: { cspell: "10.3.6" }
owner: "@RostomOhannessian"
---

# cspell

## Overview

cspell is a spell checker made for code and the text around it. It splits `camelCase` and `snake_case` names into words before it checks them,
so it can read identifiers, Markdown, YAML, and JSON in one pass. Three concepts cover most of what you need:

| Concept | What it is | Where it lives here |
| --- | --- | --- |
| Configuration | A JSON file that sets the language, the dictionaries, the files to check, and what to ignore | [cspell.json](../../../cspell.json) |
| Project dictionary | A list of words that are correct here and missing from the bundled dictionaries, one lowercase word per line | [.cspell/project-words.txt](../../../.cspell/project-words.txt) |
| Ignore rules | Regular expressions for text that is not prose: URLs, hashes, inline code, and all-capital names such as `NOLOGO` | `ignoreRegExpList` in `cspell.json` |

The `Spelling` job in [docs.yml](../../../.github/workflows/docs.yml) runs cspell on every pull request. It checks the Markdown files and the YAML and
JSON files under `docs`, `governance`, and `.github`.

## Decision rationale

[ADR-0005](../../adr/0005-documentation-system.md) names cspell as one of the documentation gates. No other spell checker was evaluated, so this is
a choice made on four facts, not a comparison:

- It understands code identifiers, which a prose-only checker flags by the hundreds in this repository.
- It runs from an official container image (`ghcr.io/streetsidesoftware/cspell`), so the version is pinned by digest in
  [tools/lint/compose.yaml](../../../tools/lint/compose.yaml) like the other lint tools, and nothing needs installing.
- Its license is MIT (checked 2026-10-08 on the [repository license](https://github.com/streetsidesoftware/cspell/blob/main/LICENSE)).
- The trade-off is noise: every new tool name is "wrong" until someone adds it to the project dictionary, and that cost is paid in the pull
  request that introduces the word.

## Setup tutorial

**Prerequisites:** Docker. The image is pulled on first use.

1. Run the check from the repository root. CI runs this step, which is a workflow step, so the lines below are YAML.

   [!code-bash[Check spelling](../../../.github/workflows/docs.yml#spelling)]

   Expected output ends with a line such as the one below. With no file arguments, cspell checks the `files` list in `cspell.json`.

   ```text
   CSpell: Files checked: 114, Issues found: 0 in 0 files.
   ```

2. When a word is flagged, decide whether it is a typo or a real term. Fix a typo in the file. Add a real term to
   `.cspell/project-words.txt` as one lowercase word on its own line, and run the check again.

**Cleanup:** nothing is left behind. The container is removed when it exits, and the repository is mounted read-only.

## Further research

- [cspell](https://cspell.org/): the project home and its documentation (checked 2026-10-08)
- [cspell configuration](https://cspell.org/docs/Configuration): every setting in `cspell.json` (checked 2026-10-08)
- [Getting started](https://cspell.org/docs/getting-started): the command line and the first run (checked 2026-10-08)
- [The cspell container image](https://github.com/streetsidesoftware/cspell/pkgs/container/cspell): the published tags (checked 2026-10-08)
