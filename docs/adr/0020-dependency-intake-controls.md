---
title: "ADR-0020: Control dependency intake with central versions, lock files, signatures, and a license gate"
description: "Pin every NuGet package in one file, restore from committed lock files, require repository signatures, and gate licenses and forbidden packages in CI, because the project must be able to prove what it links."
status: accepted
date: 2026-10-08
decision-makers: ["@RostomOhannessian"]
supersedes: []
superseded-by: []
tools: [dotnet-sdk, nuget-license, dependabot, archunitnet, xunit-v3, ms-code-coverage]
related-adrs: ["0004", "0013", "0018"]
evidence:
  - docs/research/spikes/1.c-mtp-coverage-stryker.md
---

# ADR-0020: Control dependency intake with central versions, lock files, signatures, and a license gate

## Context and problem statement

[ADR-0004](0004-licensing-and-dependency-license-policy.md) forbids several libraries and requires a license allow-list, and the
threat model names dependency intake as trust boundary TB2: a vulnerable or non-compliant package could enter the build. WP1.1 adds
the first .NET projects, so it is the first work package that can enforce those policies. Requirements `REQ-SUP-003` (dependency
intake is license- and vulnerability-aware), `REQ-CI-001` (CI runs on master and phase branches), and `REQ-QUA-004` (tests are
reproducible) depend on it.

Four facts shape the choice (all checked 2026-10-08):

- The nuget.org service publishes its repository-signing certificates at a documented endpoint, and NuGet can require a trusted
  signature on every package.
- NuGet lock files record every direct and transitive package, so a locked restore detects any change in the resolved set.
- `nuget-license` 4.0.18 supports an allow-list and exact-version overrides, and reads the restore output, but has no forbidden-package
  option.
- Dependabot has an open upstream issue, [dependabot-core#13950](https://github.com/dependabot/dependabot-core/issues/13950) (opened
  2026-01-15), where it can leave a downstream `packages.lock.json` stale when Central Package Management and project references
  are combined, which fails a locked restore.

## Decision drivers

- Every package that enters a build is one a person chose, from a source the project trusts, in a version the project can name.
- A dependency change shows up in review as a diff, and never as a surprise during a restore.
- The license policy and the forbidden list are enforced by code, not by memory ([ADR-0004](0004-licensing-and-dependency-license-policy.md)).
- A rule that cannot fail is not a rule, so each gate needs a negative control.
- The same controls run on a developer machine and in CI.

## Considered options

1. Central Package Management with transitive pinning, lock files with a locked restore in CI, required repository signatures,
   NuGet audit, a `nuget-license` allow-list, and a forbidden list checked from the lock files (chosen).
2. Per-project package versions with no lock files, and a license check in CI only.
3. A private package feed that mirrors approved packages.
4. Rely on Dependabot and GitHub dependency review alone.

## Decision outcome

Chosen option: **Central Package Management with lock files, required signatures, and CI gates**, because it makes every
dependency change visible and checkable at the earliest point, with tools the .NET SDK already provides.

The controls are:

- **Central versions.** `Directory.Packages.props` holds every package version, and `CentralPackageTransitivePinningEnabled`
  lets a vulnerable transitive version be pinned from one place.
- **Lock files.** `RestorePackagesWithLockFile` is on, the `packages.lock.json` of every project is committed, and CI restores with
  `--locked-mode`. `Directory.Build.props` also turns on locked mode whenever `CI` or `GITHUB_ACTIONS` is set.
- **Source mapping and signatures.** `NuGet.config` clears the default sources, maps every package to nuget.org, sets
  `signatureValidationMode` to `require`, and trusts the three nuget.org repository certificates read from the service's own
  endpoint. The newest certificate expires on 2027-05-18, so the file comment says to re-read the endpoint before then.
- **Audit.** NuGet audit runs in `all` mode at the `low` level, and warnings are errors, so a package with a known advisory fails the
  build. This can make a build fail the day an advisory is published, which is the intended behavior for a security-first project.
- **License gate.** CI runs `nuget-license` over the solution with transitive packages, against
  `governance/policies/license-policy.json`. One package needs an exception: `Microsoft.Testing.Extensions.CodeCoverage`, whose
  Microsoft license is not an OSI license. The exception is an override for the exact version in
  `governance/policies/license-overrides.json`, so a version bump fails the gate until someone reads the new terms. A test also fails
  if a package with an exception appears in any production project, because the exception covers test-time tools only
  ([ADR-0004](0004-licensing-and-dependency-license-policy.md) tool-only rule).
- **Forbidden packages.** `governance/policies/forbidden-packages.json` lists the packages and versions that
  [ADR-0004](0004-licensing-and-dependency-license-policy.md) forbids. The architecture tests check it against every lock file, which
  includes transitive packages. Tests with synthetic lock files prove that each forbidden package is rejected and that the first
  allowed version is accepted.
- **Dependabot.** The `nuget` ecosystem is configured with a cooldown, with the test stack updated together.

### Consequences

- Good: a dependency change is a reviewable diff in a lock file, and CI fails on an unreviewed change.
- Good: the signature requirement, the license gate, and the forbidden list were each shown to fail on purpose, not only to pass.
  Wrong trusted-signer fingerprints made a restore fail on Windows and on Linux (the Linux error was `NU3034`, "signed but not by a trusted
  signer"), removing the exception made the license gate exit with 1, and the forbidden-list tests reject each forbidden version.
- Bad: Dependabot can leave a downstream lock file stale (upstream issue above), which fails a locked restore with `NU1004` on its
  pull request. Mitigation: run `dotnet restore --force-evaluate` on that branch and commit the lock files. The Dependabot comment in
  `.github/dependabot.yml` says so, and the cost is small while only a few packages exist.
- Bad: the repository-signature requirement depends on certificates that expire. Mitigation: the expiry date is in `NuGet.config`,
  and a failed restore after rotation names the signer.
- Bad: lock files add noise to dependency pull requests. Mitigation: they are marked as generated in `.gitattributes`.
- Neutral: the forbidden list lives in the architecture tests because `nuget-license` cannot express it. WP1.2's security auditor and
  WP2.3's dependency review can read the same policy files later.

### Confirmation

- **WP1.1** delivers the files above and the `ci` workflow that runs the locked restore, the license gate, and the architecture
  tests that include the dependency tests.
- **WP2.3** adds dependency review on pull requests, which covers vulnerabilities and licenses at review time.
- **WP1.2** adds the `governance security` rule for license policy and reads the same policy files.
- The first Dependabot NuGet pull request shows whether the lock-file caveat appears in practice.

## Pros and cons of the options

### Central Package Management with lock files, required signatures, and CI gates

- Good: every control is built into the SDK or a small tool, and each one runs locally.
- Good: lock files catch transitive changes, which license checks and review would miss.
- Bad: lock-file churn, and the Dependabot caveat above.

### Per-project package versions with no lock files, and a license check in CI only

- Good: the least configuration.
- Bad: versions are scattered across projects, transitive versions float, and a restore can resolve a different set tomorrow.

### A private package feed that mirrors approved packages

- Good: the strongest control over what can be restored.
- Bad: it needs a service to run and secure, which is out of proportion for a single-maintainer teaching repository, and nuget.org
  repository signatures already give a verifiable origin.

### Rely on Dependabot and GitHub dependency review alone

- Good: no local configuration.
- Bad: both act on pull requests, so a local build, a Dev Container, and a Linux container would not enforce the same rules, and
  neither can express the forbidden list or the tool-only exception.

## Revisit when

- Dependabot closes dependabot-core#13950, or its behavior makes lock-file regeneration routine to automate.
- `nuget-license` gains a forbidden-package option, which would let the forbidden list move out of the tests.
- nuget.org publishes a new repository certificate, or NuGet changes how trusted signers are configured.
- A second package source is needed, which turns the single `*` mapping into explicit patterns.
- The coverage extension changes license terms, or a replacement under an OSI license supports Microsoft.Testing.Platform.

## Further reading

- [NuGet.org repository signatures](https://api.nuget.org/v3-index/repository-signatures/5.0.0/index.json) (checked 2026-10-08)
- [nuget-license README](https://github.com/sensslen/nuget-license) (checked 2026-10-08)
- [dependabot-core#13950](https://github.com/dependabot/dependabot-core/issues/13950) (checked 2026-10-08)
- [Spike 1.c: Microsoft.Testing.Platform, Code Coverage, and the Stryker.NET preview runner](../research/spikes/1.c-mtp-coverage-stryker.md) (checked 2026-10-06)
