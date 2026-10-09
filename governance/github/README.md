# GitHub settings as code

This folder holds the inputs that configure the repository on GitHub, and a snapshot of what is live. The human-readable record, with the reason for every setting, is [docs/governance/github-settings.md](../../docs/governance/github-settings.md).

| File | What it is |
| --- | --- |
| `rulesets/master.json` | The ruleset for the default branch: pull request required, three required checks, code owner review, no force pushes or deletion, admin bypass for pull requests only |
| `rulesets/phase.json` | The ruleset for `phase/**`: pull request required, the same checks, no force pushes |
| `labels.json` | The label set (type, phase, and size labels) |
| `milestones.json` | The milestones: Phase 0 to Phase 4 and the v1.0 launch |
| `snapshot.json` | A normalized export of the live settings, captured on 2026-10-04, after the settings were applied |

## Apply

Run these with the GitHub CLI from the repository root, as the repository owner.

```text
gh api -X POST repos/OWNER/REPO/rulesets --input governance/github/rulesets/master.json
gh api -X POST repos/OWNER/REPO/rulesets --input governance/github/rulesets/phase.json
```

Replace `OWNER/REPO` with the repository. To change an existing ruleset, find its ID with `gh api repos/OWNER/REPO/rulesets` and use `-X PUT repos/OWNER/REPO/rulesets/ID`.

Labels and milestones are created from `labels.json` and `milestones.json` with `gh label create NAME --color COLOR --description TEXT --force` and `gh api -X POST repos/OWNER/REPO/milestones`, or from WP1.2 on with `dotnet run --project tools/Governance.Auditor -- github-sync --apply`, which shows the diff first. The Phase 1 work-package Issues carry `<!-- governance:id=WPn.m -->` on their first line so that tooling can find them again, and `github-sync` uses that marker.

The remaining settings (merge options, security features, and Actions permissions) are single API calls that the settings record lists, one per row. `snapshot.json` shows the resulting values.

## Detect drift

Re-capture the live values and compare them with `snapshot.json`:

1. Read the repository with `gh api repos/OWNER/REPO`, the Actions settings under `repos/OWNER/REPO/actions/permissions`, and each ruleset under `repos/OWNER/REPO/rulesets/ID`.
2. Remove volatile fields (IDs, timestamps, links) and compare the rest.

From WP1.2, `governance github-sync` compares and syncs the labels (from `labels.json`), the milestones (from `milestones.json` and the phase states in `docs/project/status.yaml`), and the work-package Issues (from `status.yaml`). It prints the diff first and writes only with `--apply`. It does not read the repository settings or the rulesets, so the comparison above stays manual until a later work package automates it.

## Rules for changing a setting

- Change the setting, then update the input file and `snapshot.json`, and the row in the settings record, in the same pull request.
- Never put a token, a key, or a personal email address in this folder.
- The `master` ruleset requires code owner review for this folder, so changes here need the owner's deliberate approval.
