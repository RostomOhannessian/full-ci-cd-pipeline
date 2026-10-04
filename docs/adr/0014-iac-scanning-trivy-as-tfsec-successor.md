---
title: "ADR-0014: Use Trivy for infrastructure-as-code scanning"
description: "Use Trivy's misconfiguration scanner instead of frozen tfsec components so Terraform scanning stays current and can emit SARIF."
status: proposed
date: 2026-10-04
decision-makers: ["@RostomOhannessian"]
accepted-in: "WP3.1"
supersedes: []
superseded-by: []
tools: [trivy, tfsec, tfsec-action, terraform, tflint, github-actions, sarif]
related-adrs: ["0013"]
evidence:
  - docs/research/2026-10-04-github-supply-chain-verification.md
---

# ADR-0014: Use Trivy for infrastructure-as-code scanning

## Context and problem statement

The brief asks for a GitHub Action that runs tfsec, but plan finding F5, plan section 3, and [the Phase 3 plan](../plans/phases/phase-3-zero-trust-platform.md) replace that literal tool choice with Trivy's infrastructure-as-code scanner. The reason is practical: this repository needs an actively maintained misconfiguration gate in Phase 3, not a frozen tool kept only to satisfy older wording.

The evidence in `../research/2026-10-04-github-supply-chain-verification.md` says `aquasecurity/tfsec` is now part of Trivy, the last tfsec release is v1.28.14, and `aquasecurity/tfsec-action` has had no release since 2023-01-26 (checked 2026-10-04). The same record also ties the repository's Trivy usage to the March 2026 action compromise discussed in [ADR-0013](./0013-shift-left-security-toolchain.md), so the Phase 3 gate must use known-safe Trivy tooling and SHA-pinned actions.

The official migration guide confirms the command migration from `tfsec <dir>` to `trivy config <dir>` (checked 2026-10-04). It does not by itself provide the repository's traceability story from brief wording to Trivy output, so WP3.1 still needs an explicit AVD mapping note alongside the workflow and this ADR.

This decision serves plan sections 8.13 and 10.1, plus WP3.1 and WP3.9. The constraints are: SARIF output is required, CI must stay teachable, the workflow must be supportable on a public repository, and the repository must not depend on abandoned security gates.

|Brief requirement|Repository implementation|Evidence|
|---|---|---|
|GitHub Action that runs tfsec|GitHub Action runs Trivy IaC as tfsec's approved successor, and docs explain the wording change|plan finding F5; WP3.1; WP3.9|
|Security findings in PR evidence|Trivy uploads SARIF for the Terraform workflow|plan section 10.1; WP3.9|
|Traceable successor story|ADR plus AVD mapping note tie tfsec-era wording to Trivy output|this ADR; WP3.1|

## Decision drivers

- The IaC gate must use a maintained scanner with current rules and releases.
- The scanner must fit the Phase 2 and Phase 3 security model in [ADR-0013](./0013-shift-left-security-toolchain.md): SHA-pinned actions, verified versions, and SARIF evidence.
- The implementation must preserve a clear compliance story for the brief's "tfsec" wording.
- The scanner must work cleanly with Terraform PR checks and the Phase 3 `terraform.yml` workflow.

## Considered options

1. Keep `aquasecurity/tfsec-action` as written in the brief.
2. Run a frozen tfsec CLI directly, checksum-verified, as the blocking gate.
3. Run Trivy's IaC misconfiguration scanner, with an AVD mapping note for tfsec-era wording.
4. Replace tfsec with another IaC scanner such as Checkov or KICS.

## Decision outcome

Chosen option: **Run Trivy's IaC misconfiguration scanner, with an AVD mapping note for tfsec-era wording**, because it is the only option that meets the maintenance, SARIF, and supply-chain drivers at the same time.

### Consequences

- Good: the repository uses one maintained Aqua scanner for both container and Terraform security work, which reduces tool sprawl and supports the shift-left model in [ADR-0013](./0013-shift-left-security-toolchain.md).
- Bad: the brief still says "tfsec," so readers can miss the change. Mitigation: keep a short brief-compliance table in the ADR, require a WP3.1 AVD mapping note, and make the workflow output and docs say "Trivy IaC (tfsec successor)."
- Neutral: the decision does not remove `tflint`; it keeps the split between style and correctness checks (`tflint`) and security and misconfiguration checks (Trivy).

### Confirmation

- **WP3.1** builds the Terraform lint and scan path, records the tfsec-to-Trivy mapping note, and proves a local run.
- **WP3.9** makes Trivy IaC blocking in CI, uploads SARIF, and records the Trivy DB version in evidence.
- **WP2.2** and **WP2.4** provide the workflow-hardening pattern: SHA pinning, least-privilege permissions, and known-safe Trivy action versions.
- The governance auditor from **WP1.2** and the workflow checks in plan section 10.1 verify that unpinned or unsafe action usage does not re-enter the repository.

## Pros and cons of the options

### Keep `aquasecurity/tfsec-action` as written in the brief

- Good: literal alignment with the brief wording.
- Bad: the action is abandoned in practice and teaches learners to depend on an unmaintained security gate.

### Run a frozen tfsec CLI directly, checksum-verified, as the blocking gate

- Good: preserves the tfsec name without relying on the stale action.
- Bad: it still locks the repository to a feature-frozen scanner and duplicates the Trivy toolchain already required elsewhere.

### Run Trivy's IaC misconfiguration scanner, with an AVD mapping note for tfsec-era wording

- Good: follows Aqua's documented migration path, emits SARIF, and keeps the scanner on an actively maintained line (checked 2026-10-04).
- Bad: the repository must explain the wording change carefully so that "tfsec" in the brief, "Trivy" in CI, and AVD identifiers in reports are obviously the same control family.

### Replace tfsec with another IaC scanner such as Checkov or KICS

- Good: these are real alternatives with active communities.
- Bad: they add a second migration step with no evidence advantage in this plan, and they break the clear "tfsec successor" story the brief already approved.

## Revisit when

- Aqua archives tfsec and removes or materially changes the migration path.
- A Phase 3 spike shows that Trivy IaC cannot produce the SARIF or rule evidence the workflow needs.
- A future Trivy release changes the misconfiguration identifier scheme in a way that breaks the mapping note or the evidence trail.
- The Trivy action line has another supply-chain event that changes the repository's trust decision in [ADR-0013](./0013-shift-left-security-toolchain.md).

## Further reading

- [Aqua Security, "Migrating from tfsec to Trivy"](https://github.com/aquasecurity/tfsec/blob/master/tfsec-to-trivy-migration-guide.md) (checked 2026-10-04)
- [GitHub and supply-chain verification record](../research/2026-10-04-github-supply-chain-verification.md) (checked 2026-10-04)
- [GitHub changelog, "GitHub Actions policy now supports blocking and SHA-pinning actions"](https://github.blog/changelog/2025-08-15-github-actions-policy-now-supports-blocking-and-sha-pinning-actions/) (checked 2026-10-04)
