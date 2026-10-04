# Governance

This project has one maintainer and is run through written decisions, so that its direction, its rules, and its history are inspectable
by anyone reading the repository.

## Roles

| Role | Who | Responsibilities |
| --- | --- | --- |
| Maintainer | [@RostomOhannessian](https://github.com/RostomOhannessian) | Owns the plan and the roadmap, reviews and merges changes, publishes releases, enforces the Code of Conduct, and handles security reports |
| Contributor | Anyone who opens an Issue, a Discussion, or (after v1.0) a pull request | Follows [CONTRIBUTING.md](CONTRIBUTING.md) and the [Code of Conduct](CODE_OF_CONDUCT.md) |

## How decisions are made

- **Direction** is set by the [implementation plan](docs/plans/implementation-plan.md). Changing it means amending the plan in a pull request,
  adding a revision entry, and updating the [status page](docs/project/STATUS.md).
- **Architecture, tooling, dependency, and process decisions** are recorded as ADRs in [docs/adr](docs/adr/README.md),
  following [ADR-0001](docs/adr/0001-record-architecture-decisions.md). Accepted ADRs are not rewritten; a new ADR supersedes the old one.
- **Everything else** is decided in the pull request that makes the change, by the maintainer, after discussion where useful.

## How changes are accepted

The rules that protect `master` and the phase branches are in [ADR-0007](docs/adr/0007-solo-maintainer-governance.md) and are recorded, with the exact
settings, in [docs/governance/github-settings.md](docs/governance/github-settings.md). In short: every change goes through a pull request,
required checks must pass, and owners of sensitive paths must review. Because a maintainer cannot approve their own pull request,
the maintainer may bypass the review requirement for pull requests only, and only when owner review is the sole unmet rule. GitHub records every bypass.

Outside pull requests are accepted after the v1.0 release. Issues and Discussions are open now.

## Releases

Releases follow semantic versioning. Pre-1.0 releases (`v0.x`) mark phase milestones. From `v0.2.0` on, releases carry signed and attested artifacts
and an evidence bundle (test reports, coverage, SBOMs, scan results, and verification transcripts). Only the maintainer publishes releases.

## Conduct and security

- Conduct reports follow the process in the [Code of Conduct](CODE_OF_CONDUCT.md).
- Vulnerability reports follow [SECURITY.md](SECURITY.md).

## Continuity

Everything needed to continue the project is in the repository: the plan, the phase plans, the live status with a "Resume here" section, the ADRs,
and the runbooks. Another maintainer can take over by following the resume protocol in [AGENTS.md](AGENTS.md). Credentials and local environments are
disposable by design and are never part of the handover; they are recreated from the documented bootstrap procedure.

## Changing this document

Amend it in a pull request and record the reason in an ADR when the change alters how decisions are made or who may approve them.
