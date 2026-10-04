# Security policy

This repository is a teaching and portfolio project. It demonstrates secure engineering practices, and its own security matters:
the build, signing, and delivery pipeline is part of what it teaches. It is not production software, and you should not deploy it as-is
without reviewing every credential, policy, and network rule for your environment.

## Supported versions

| Version | Supported |
| --- | --- |
| `master` (pre-1.0 development) | Yes. Fixes land on `master`. |
| Tagged `v0.x` releases | Latest tag only |
| `v1.0` and later | Latest minor release |

## Reporting a vulnerability

**Report privately. Do not open a public Issue, Discussion, or pull request for a vulnerability.**

1. Open a [private vulnerability report](https://github.com/RostomOhannessian/full-ci-cd-pipeline/security/advisories/new).
   Only the maintainer can see it.
2. Describe what you found, where, how to reproduce it, and what an attacker could do with it.
   Include versions, the resource profile you used, and any proof of concept. Do not include real credentials or personal data.
3. Expect an acknowledgment within 7 days and a status update at least every 14 days until the report is resolved.
   This is a single-maintainer project, so these are targets, not guarantees.

If private reporting is unavailable for any reason, open a Discussion that says only that you need to report a security problem,
without details, and the maintainer will arrange a private channel.

## What is in scope

- Source code, scripts, and tests in this repository.
- GitHub Actions workflows, reusable workflows, and their permissions.
- Terraform, Kubernetes manifests, Kyverno policies, and other configuration committed here.
- Published artifacts: container images, SBOMs, provenance, and signatures, once they exist (the first signed release is planned for `v0.2.0`).
- Documentation that would lead a reader into an insecure setup.

## What is out of scope

- Vulnerabilities in third-party tools and images this project uses. Report those upstream. If a vulnerable version is pinned here, tell us and we will update it.
- Demonstration credentials that are generated locally for a disposable environment, as long as they are never committed and never reused.
- Findings that require physical access to a developer's machine, or that depend on a local cluster being exposed to the internet against the documented setup.
- Denial of service through resource exhaustion of a local laptop environment.

## Coordinated disclosure

Please give the maintainer a reasonable time to fix a problem before you disclose it publicly. The default is 90 days from the report,
shorter if a fix ships sooner, and the maintainer will tell you if more time is needed. With your agreement, fixes are credited in the release notes
and in a published security advisory.

## Safe harbor

Good-faith research that follows this policy, avoids privacy violations and data destruction, and stays within the scope above will not be pursued
as a violation of this policy. There is no bug bounty.

## How this repository protects itself

The controls are documented, and the evidence is linked as it is built: secret scanning and push protection, dependency and license checks,
workflow hardening with pinned actions and least-privilege tokens, signed and attested artifacts, and admission policy in the cluster.
See [the threat model](docs/security/threat-model.md) and [ADR-0013](docs/adr/0013-shift-left-security-toolchain.md).
