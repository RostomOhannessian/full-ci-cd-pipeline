---
title: "ADR-0013: Adopt a layered shift-left security toolchain"
description: "Use a layered toolchain that answers code, dependency, image, provenance, workflow, and admission questions before a change can ship."
status: proposed
date: 2026-10-04
decision-makers: ["@RostomOhannessian"]
accepted-in: "WP2.5"
supersedes: []
superseded-by: []
tools:
  - codeql
  - dependency-review
  - trivy
  - openvex
  - syft
  - spdx
  - cyclonedx
  - slsa
  - github-attestations
  - cosign
  - sigstore
  - kyverno
  - zizmor
  - actionlint
  - gitleaks
  - github-secret-scanning
  - scorecard
  - slsa-github-generator
related-adrs: ["0002", "0014"]
evidence:
  - docs/research/2026-10-04-decision-critical-addendum.md
  - docs/research/2026-10-04-github-supply-chain-verification.md
  - docs/research/2026-10-04-kubernetes-platform-verification.md
---

# ADR-0013: Adopt a layered shift-left security toolchain

## Context and problem statement

Phase 2 must make every change pass through build, test, workflow hardening, vulnerability review, inventory, signing, attestation, and verification before it can ship. Plan sections 8.13 and 9 and [the Phase 2 plan](../plans/phases/phase-2-secure-ci.md) define the target architecture, and WP2.0 through WP2.5 are the proving path.

One tool cannot answer every security question in this repository. The project ships C# code, GitHub workflows, container images, SBOMs, and signed artifacts into a GitOps platform that later enforces policy at admission time. The toolchain therefore needs layered answers:

| Question | Chosen tool |
| --- | --- |
| Does the code or workflow contain known dangerous patterns? | CodeQL, zizmor, actionlint |
| Did this pull request add a vulnerable or disallowed dependency? | dependency-review |
| Does the built image contain fixable critical vulnerabilities? | Trivy |
| What exactly is in the artifact? | Syft with SPDX and CycloneDX |
| What build produced this artifact? | GitHub artifact attestations with SLSA provenance |
| Was the artifact signed by the trusted workflow identity? | Cosign keyless signing |
| Should the cluster admit this image? | Kyverno |
| Did a secret leak into source or history? | gitleaks plus GitHub secret scanning and push protection |
| Is the repository following supply-chain best practice broadly? | OpenSSF Scorecard |

Two case studies from the GitHub verification record are design inputs, not trivia:

- The **tj-actions/changed-files** compromise, GHSA-mrrh-fwg8-r2c3 / CVE-2025-30066, ran from 2025-03-14 to 2025-03-15. Attackers force-pushed tags to a malicious commit that dumped runner memory and printed base64-encoded secrets to logs, with about 23,000 affected repositories reported in the record (checked 2026-10-04).
- The **aquasecurity/trivy-action** and **aquasecurity/setup-trivy** compromise, GHSA-69fq-xp46-6x23 / CVE-2026-33634, ran on 2026-03-19 and 2026-03-20. The record says attackers published a malicious Trivy v0.69.4 release, force-pushed 76 of 77 `trivy-action` tags, and replaced all 7 `setup-trivy` tags. Safe paths start at `trivy-action` v0.35.0 and `setup-trivy` v0.2.6 (checked 2026-10-04).

Those incidents motivate non-negotiable repository controls:

- SHA pinning enforced by repository policy.
- Immutable releases.
- `persist-credentials: false` on checkout.
- Least-privilege workflow tokens.
- No `pull_request_target` jobs that execute pull-request code.

Privacy also matters. The GitHub verification record says keyless Cosign signing against the public Sigstore instance publishes workflow path, workflow ref, workflow commit, source commit, run link, and repository visibility in the public Rekor transparency log (checked 2026-10-04). ADR-0002 accepts that disclosure because the repository becomes public after Phase 0, but this ADR must state it plainly.

## Decision drivers

- Catch workflow, code, dependency, image, and provenance problems before deployment.
- Keep the control set understandable enough to teach, not just to enforce.
- Reuse the public-repo capabilities unlocked by ADR-0002 without inventing private-repo workarounds.
- Produce evidence that the cluster can verify at admission time.
- Prefer maintained tools and documented migration paths over frozen or superseded tools.

## Considered options

1. A layered toolchain combining GitHub-native and ecosystem tools.
2. A lighter GitHub-native-only stack with fewer external verifiers.
3. An alternate stack centered on Semgrep, Grype, Notation, and `slsa-github-generator`.
4. Deferring provenance and admission verification until later phases.

## Decision outcome

Chosen option: **a layered toolchain combining GitHub-native and ecosystem tools**, because no single vendor surface here covers all of the repository's questions with the required evidence path from pull request to cluster admission.

The chosen set is:

- **CodeQL** for C# and workflow static analysis on the public repository only, because the addendum records that private-repo use is excluded by the CLI terms as read on 2026-10-04.
- **dependency-review** for pull-request vulnerability and license deltas.
- **Trivy** for image and IaC scanning, with any fixable critical vulnerability failing the build.
- **OpenVEX** for time-limited exceptions to unfixed critical findings.
- **Syft** for SPDX and CycloneDX SBOMs.
- **GitHub artifact attestations** for SLSA v1 provenance, using a reusable trusted workflow so the design can claim Build Level 3 as recorded in the GitHub verification record (checked 2026-10-04).
- **Cosign keyless signing** for the image signature that Kyverno can verify.
- **Kyverno** for admission verification of signatures, provenance, and SBOM attestations.
- **zizmor** and **actionlint** for workflow security and correctness.
- **gitleaks** plus **GitHub secret scanning** and **push protection** for secret exposure control.
- **OpenSSF Scorecard** for an external posture signal.

The closest alternatives are kept explicit:

- **Semgrep** is the nearest alternative to CodeQL, but this repository already depends on GitHub code scanning and needs a workflow-language path in the same policy surface.
- **Grype** is the nearest alternative to Trivy, but Trivy already covers image scanning and the tfsec-successor IaC path selected in ADR-0014, which keeps the tool count lower.
- **Notation** is the nearest alternative to Cosign, but the verified Kyverno path in the research records is framed around Cosign keyless verification and Sigstore bundles.
- **`slsa-github-generator`** is the nearest alternative to GitHub artifact attestations, but the GitHub verification record says the container builder is no longer actively maintained and points new integrations to GitHub's native attestation path.

### Consequences

- Good: every important question gets a purpose-built control, and the controls line up with the cluster's future admission policy instead of stopping at CI.
- Good: the same evidence chain supports teaching, release evidence bundles, and policy enforcement.
- Good: the case-study controls are concrete and testable, not generic advice.
- Bad: the toolchain is broad and increases CI time and maintenance. Mitigation: phase the rollout through WP2.0 to WP2.5 and keep ownership of each gate explicit.
- Bad: some controls depend on public-repo capabilities or public transparency logs. Mitigation: capability-aware workflows must print `SKIPPED: <capability> unavailable` on private copies, and the public Rekor disclosure is documented instead of hidden.
- Neutral: the design uses both GitHub-native and non-GitHub tools. That is intentional because the repository needs verification across source, artifact, and cluster boundaries.

### Confirmation

This decision is accepted only if the following work packages succeed:

- **WP2.0** proves Kyverno 1.19 can verify the production signature and attestation shapes, including the Cosign v3 path recorded in the research.
- **WP2.2** proves workflow hardening gates with SHA pinning, least-privilege permissions, and linting.
- **WP2.3** proves CodeQL, dependency review, and Scorecard on the public repository path.
- **WP2.4** proves the Trivy gate, OpenVEX exception handling, and SBOM generation.
- **WP2.5** proves provenance, keyless signing, `gh attestation verify`, `cosign verify`, `cosign verify-attestation`, and Kyverno CLI verification against the production policy.

## Pros and cons of the options

### A layered toolchain combining GitHub-native and ecosystem tools

- Good: answers distinct questions with tools that match those questions.
- Good: aligns CI evidence with later admission control.
- Good: includes concrete safeguards learned from recent supply-chain incidents.
- Bad: more tools to document, pin, and maintain.
- Bad: requires careful explanation so learners understand why each layer exists.

### A lighter GitHub-native-only stack with fewer external verifiers

- Good: simpler setup and fewer dependencies.
- Good: more of the experience stays inside one product surface.
- Bad: does not cover the future admission path as directly.
- Bad: leaves weaker answers for SBOM generation, keyless signing, or workflow-specific hardening depth.

### An alternate stack centered on Semgrep, Grype, Notation, and `slsa-github-generator`

- Good: each tool is a recognizable alternative in its niche.
- Good: could work in other repositories with different policy goals.
- Bad: it weakens the direct fit to the verified GitHub and Kyverno paths used here.
- Bad: the GitHub verification record says the container-focused `slsa-github-generator` path is no longer the recommended path for new integrations.

### Deferring provenance and admission verification until later phases

- Good: shortens early CI and lowers initial complexity.
- Good: might suit a prototype that never publishes artifacts.
- Bad: this repository's brief explicitly wants shift-left supply-chain controls, not post-hoc hardening.
- Bad: deferral would miss the whole teaching value of tracing one artifact from source to admission.

## Revisit when

- WP2.0 cannot prove the attestation and Kyverno verification path with the chosen predicates.
- CodeQL terms or public-repo capabilities change materially.
- A maintained alternative clearly replaces one of the chosen tools without losing an evidence path.
- The incident-driven controls become obsolete because GitHub ships a stronger built-in mechanism that is verified and available on the repository's target plan.

## Further reading

- GitHub and supply-chain verification: [../research/2026-10-04-github-supply-chain-verification.md](../research/2026-10-04-github-supply-chain-verification.md) (checked 2026-10-04)
- Kubernetes platform verification: [../research/2026-10-04-kubernetes-platform-verification.md](../research/2026-10-04-kubernetes-platform-verification.md) (checked 2026-10-04)
- Decision-critical addendum: [../research/2026-10-04-decision-critical-addendum.md](../research/2026-10-04-decision-critical-addendum.md) (checked 2026-10-04)
