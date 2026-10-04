---
title: "v1.0 launch gate"
description: "Turn the public repository into a polished, versioned portfolio release, with every publishing step confirmed explicitly."
type: phase-plan
phase: launch
audience: [maintainers, learners]
last-verified: 2026-10-04
owner: "@RostomOhannessian"
---

# v1.0 launch gate

Section numbers (§) in this file refer to [the implementation plan](../implementation-plan.md). Live progress is on [the status page](../../project/STATUS.md).

| Field | Value |
| --- | --- |
| Branch | None. Release only |
| Milestone | v1.0 launch |
| Release | `v1.0.0` |
| Depends on | Phase 4 merged |

## Goal

The repository has been public since Phase 0. The launch turns it into a polished, versioned portfolio release. Each publishing step requires explicit confirmation.

## Preconditions

- Phase 4 is merged and the `v0.4.0` release exists with its evidence bundle.
- Every row of the validation matrix in [the implementation plan](../implementation-plan.md) has current evidence.
- No open Issue is labeled as a security or privacy problem.
- The maintainer is available to confirm each publishing step.

## Steps

1. Run gitleaks and manual review across the full history, tree, release assets, Issues, and workflow logs, checking for credentials, personal data, and internal paths.
2. Confirm every commit uses the noreply identity.
3. Run the license review: Apache-2.0 `LICENSE`/`NOTICE`, `THIRD-PARTY-NOTICES` generated from the SBOMs, and attribution for any vendored external skills.
4. Review community health files, labels, Discussions, and the vulnerability reporting process.
5. Run the stranger test: someone unfamiliar with the repo follows the quick start, the full-platform path, the tests, and cleanup from a fresh clone, and any friction gets fixed.
6. Publish the docs as a versioned `v1.0` snapshot.
7. Publish the README badges and Scorecard.
8. Run the final Copilot and human review against `REVIEW.md`.
9. Tag and publish the signed, attested `v1.0.0` release with its evidence bundle.
10. Pin the repository on the profile and add a social preview image.

## Evidence required

- A launch checklist record in `docs/portfolio/` with the result of each step below.
- The privacy and secret review results, including the full-history scan and the Issue and workflow-log review.
- The generated third-party notices, and the license review of dependencies and any vendored skill.
- The stranger test report: who ran it, what friction they hit, and what was fixed.
- The versioned documentation snapshot, the Scorecard result, and the signed, attested `v1.0.0` release with its evidence bundle.

## Risks and rollback

| Risk | Mitigation |
| --- | --- |
| Something sensitive was missed in history, Issues, or logs | Full-history and tree scans, a manual review of Issues and workflow logs, and a stranger test before the announcement |
| The replaced initial commit can still be retrieved by its ID | Revisited here with the facts at that time; options are recorded in [ADR-0006](../../adr/0006-commit-identity-and-privacy.md) |
| Release errors | Immutable releases cannot be edited, so a correction is a new release such as `v1.0.1` |

Rollback: supersede a bad release with a fixed one and mark the old one as withdrawn in its notes. A published repository can be made private again, but forks, clones, and cached copies cannot be recalled, so treat publication as one-way.
