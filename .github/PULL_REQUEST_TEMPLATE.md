<!-- markdownlint-disable MD041 -->
## Summary

<!-- What changes and why, in two or three sentences. Link the Issue: "Closes #123" or "Part of #123". -->

## Work package

- Work package ID: <!-- for example WP1.8, or "none" for a hotfix or dependency update -->
- Target branch: <!-- phase/* for a work package, master for a phase PR, hotfix, or dependency update -->

## Definition of Done

<!-- Tick what applies. If an item does not apply, say why next to it. See CONTRIBUTING.md. -->

- [ ] Required checks pass, with no new analyzer warnings.
- [ ] Tests added or updated, tagged with requirement IDs, and thresholds met.
- [ ] Documentation updated: tool pages, tutorials, labs, and reference pages for anything new.
- [ ] `docs/project/status.yaml` and `docs/project/STATUS.md` updated, including **Resume here**.
- [ ] `CHANGELOG.md` Unreleased section updated.
- [ ] ADR created or updated for every decision made.
- [ ] Threat model updated if a trust boundary, data flow, or credential changed.
- [ ] Governance auditors pass (security and blast radius).
- [ ] Branch pushed, so another machine can resume.

## Evidence

<!-- Commands you ran and their results, links to workflow runs, screenshots for UI or diagrams. -->

## Risk and rollback

<!-- Blast radius: which layers, environments, or contracts this touches, and how to undo it. For migrations, say whether it is expand or contract. -->

## Security and privacy check

- [ ] No secrets, tokens, personal data, internal paths, or private email addresses in code, docs, logs, or test data.
- [ ] No new workflow permission, action, or dependency without a pinned version and a reason.
