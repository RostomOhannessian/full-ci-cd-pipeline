---
title: "Phase 2: CI pipeline and shift-left security"
description: "Every change is built once, then tested, analyzed, scanned, inventoried, signed, attested, and verifiable, through least-privilege workflows."
type: phase-plan
phase: 2
audience: [maintainers, learners]
last-verified: 2026-10-04
owner: "@RostomOhannessian"
---

# Phase 2: CI pipeline and shift-left security

Section numbers (§) in this file refer to [the implementation plan](../implementation-plan.md). Live progress is on [the status page](../../project/STATUS.md).

| Field | Value |
| --- | --- |
| Phase branch | `phase/2-secure-ci` |
| Work-package branches | `wp/2.NN-<slug>` |
| Milestone | Phase 2: Secure CI |
| Release at exit | `v0.2.0` (the first signed and attested release) |
| Depends on | Phase 1 merged |

## Goal

every change is built once and then tested, analyzed, scanned, inventoried, signed, attested, and verifiable, with least-privilege workflows that can't be hijacked.

## Learning outcomes

- Container hardening
- The GitHub Actions security model
- SAST and SCA
- SBOM formats
- SLSA levels
- Sigstore keyless signing and transparency logs
- Vulnerability triage with VEX
- Supply-chain incident analysis

## Scope and non-goals

In scope:

- A secure, minimal container image and a least-privilege CI workflow architecture.
- CodeQL, dependency review, image scanning, SBOMs, provenance, signing, and verification, each proven on the events where it must run.
- Release engineering: SemVer tags, generated notes, an evidence bundle, and immutable releases.

Non-goals:

- No cluster admission enforcement. Phase 2 proves the verification with the Kyverno command-line tool; running Kyverno in a cluster is Phase 3.
- No Terraform, deployment, or promotion (Phases 3 and 4).
- No self-hosted runners. GitHub warns against them for public repositories, and nothing here needs one.
- No CodeQL on private copies of the repository. Capability-aware jobs skip it and say so.

## Decisions introduced

| ADR | Decision | Accepted in |
| --- | --- | --- |
| [0013](../../adr/0013-shift-left-security-toolchain.md) | The shift-left security toolchain | WP2.5 |

New ADRs expected: the container health-probe approach (WP2.1) and, if the WP2.0 spike requires it, the Cosign-native attestation fallback.

## Prerequisites and setup changes

- Phase 1 is merged. The repository is public (Phase 0), so CodeQL, dependency review, code scanning results, and artifact attestations are available.
- GitHub environments `publish` (deployment branch policy: `master` only) and `release` (`v*` tags, required reviewer).
- Actions settings from Phase 0 stay in force: read-only default token, fork approval, SHA pinning enforced. The allowlist grows only for the actions this phase adds.
- The `master` ruleset adds the new required checks, including code scanning results from WP2.3.
- The GHCR package is made public once, which is one-way and needs explicit confirmation when it happens.

## Work packages

| WP | Title | Size | Key deliverables |
| --- | --- | --- | --- |
| 2.0 | Risk spikes | S | A sandbox workflow publishes a test image. Prove that Kyverno 1.19's `ImageValidatingPolicy` (through the CLI) verifies (a) a Cosign v3 keyless signature, (b) a GitHub SLSA provenance attestation stored as an OCI referrer, and (c) an SBOM attestation. Prove that the reusable-workflow identity yields a Build L3 claim. If (b) or (c) fails, add Cosign-native attestations carrying the same predicates. |
| 2.1 | Secure container image | M | Multi-stage Dockerfile: SDK and runtime images pinned by digest; restore, build, test, and publish stages; BuildKit cache mounts; deterministic publish with `SOURCE_DATE_EPOCH`; chiseled non-root runtime; read-only-filesystem compatible; no shell or build tools. Health is checked by orchestrator probes, plus an exec-form self-probe for Compose (decided by ADR). `.dockerignore`; multi-arch images (amd64 and arm64) built on native runners and merged into a manifest list; `Catalog.Image.Tests` (non-root user, ports, labels, no SDK, read-only startup, readiness); image size budget |
| 2.2 | Workflow architecture and hardening | M | `ci.yml` orchestrates the reusable workflows `_build-test`, `_trusted-image` (trusted builder), `_scan`, and `_verify`. Triggers follow §5.2. Each job gets least-privilege permissions; actions are SHA-pinned; `persist-credentials: false`; concurrency groups; caches keyed on lock files; job selection driven by blast radius; capability and license detection (§4.1); timeouts and artifact retention; actionlint and zizmor gates; `publish` environment (deployment branch policy: `master` only) and `release` environment (`v*` tags; required reviewer) |
| 2.3 | CodeQL and SCA | S | CodeQL advanced setup (C# with manual build mode, plus the `actions` language) using the security-extended suite; the master ruleset requires code scanning results (blocks on high or above); dependency review (vulnerabilities and licenses); OpenSSF Scorecard workflow and badge |
| 2.4 | Image scanning and SBOM | M | Trivy (pinned and verified, at or above the safe versions) scans the exact digest and fails on any fixable `CRITICAL`; unfixed criticals need an OpenVEX justification with expiry, which the auditor validates; SARIF upload; the Trivy DB version is recorded as evidence; Syft produces SPDX and CycloneDX SBOMs per platform; nightly re-scan of released images opens Issues |
| 2.5 | Provenance, attestations, and signing | M | `actions/attest` provenance (SLSA v1) and an SBOM attestation, both with `push-to-registry`; keyless Cosign signature bound to the trusted workflow identity; GHCR package set to public (a one-way change, confirmed explicitly); a verification job runs `gh attestation verify`, `cosign verify`, and `cosign verify-attestation`; Kyverno CLI contract test using the production policy from `policies/kyverno`; the *shift-left tools* ADR is accepted |
| 2.6 | Release engineering | S | `release.yml`: SemVer tag, git-cliff notes, an evidence bundle attached to the release (test reports, coverage, SBOMs, scans, verification transcripts), immutable releases, and verification instructions in the release notes; `v0.2.0` is the first signed, attested release |
| 2.7 | Learning content and phase exit | M | Tool pages, CI threat model, labs, test plan, troubleshooting, retrospective, phase PR |

## Test plan

| Check | What it proves | Gate |
| --- | --- | --- |
| Image tests (`Catalog.Image.Tests`) | Non-root user, ports, labels, no SDK or shell, read-only startup, readiness, size budget | Blocking |
| Workflow security (actionlint, zizmor, auditor) | Permissions, SHA pinning, injection resistance, no `secrets.*` | Blocking |
| Negative controls | An unpinned action, broad permissions, a `secrets.*` reference, and a fixable CRITICAL vulnerability each fail the gate | Demonstrated once, kept as fixtures |
| Trivy gate and VEX | Fixable CRITICAL fails; an expired VEX exception is rejected | Blocking |
| SBOM checks | SPDX and CycloneDX documents exist per platform and validate | Blocking |
| Verification job | `gh attestation verify`, `cosign verify`, and `cosign verify-attestation` succeed against the published digest with the exact identities | Blocking on publish |
| Kyverno CLI contract test | The production image policy admits the published image and rejects a tampered or unsigned one | Blocking |
| Trusted versus untrusted events | Pull requests build and scan but never publish, sign, or attest | Blocking |
| Nightly re-scan | Released images are re-scanned and new CRITICALs open Issues | Scheduled |

## Tool pages introduced

- Docker and BuildKit, and chiseled images
- GitHub Actions security (permissions, OIDC, reusable workflows, environments)
- actionlint and zizmor
- CodeQL
- Dependency review
- OpenSSF Scorecard
- Trivy (image scanning) and OpenVEX
- Syft, SPDX, and CycloneDX
- SLSA and GitHub artifact attestations
- Sigstore and Cosign (Fulcio, Rekor, the bundle format)
- GHCR
- SARIF
- Immutable releases

## Labs

1. Inspect the image layers and the user.
2. Read an SBOM and find a transitive dependency.
3. Reproduce the Trivy gate and write a time-limited VEX exception.
4. Verify the signature, provenance, and SBOM attestation by hand.
5. Trace a commit to its digest, attestation, and Rekor entry, and see exactly what identity metadata is public.
6. Run a supply-chain incident tabletop exercise: the March 2026 trivy-action compromise mapped to our controls.
7. Break a workflow rule (an unpinned action, excessive permissions, or `secrets.*`) and watch the gates block it.

## Evidence required

- A workflow run for each control on the event where it must run, plus the untrusted-event runs that show nothing was published.
- Verification transcripts and the public transparency-log entry for the signed digest.
- The Scorecard result and the `v0.2.0` release with its evidence bundle.
- Spike report from WP2.0 and the updated ADR-0013.

## Risks and rollback

| Risk | Mitigation |
| --- | --- |
| Signature or attestation incompatibility with the admission policy (R4) | WP2.0 spike first; Kyverno CLI contract test in CI; Cosign-native fallback |
| Tool churn (R5) | Inventory lifecycle, nightly freshness report |
| Compromised action or tool (R6) | SHA pinning enforced, zizmor, minimal third-party actions, verified tool versions |
| GitHub policy or pricing changes (R16) | No self-hosted runners; capability detection |

Rollback: revert the workflow pull request. A bad image is never promoted: delete the package version, publish a patched version, and issue a new tag. Immutable releases cannot be altered, so a correction is a new release.

## Exit gate

- The image runs non-root and becomes ready.
- CI demonstrates every control on the correct events:
  - Tests, CodeQL, and dependency review
  - The critical-vulnerability gate and the SBOM
  - Provenance and the signature
- Untrusted PRs never publish or sign anything.
- The Kyverno CLI verifies the published `v0.2.0` image.
- Scorecard results are published.
- Docs, labs, test plan, status, and Milestone are complete.
