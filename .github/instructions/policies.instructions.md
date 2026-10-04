---
applyTo: "policies/**/*.yml,policies/**/*.yaml,governance/policies/**/*.yml,governance/policies/**/*.yaml"
---

# Policy rules

Applies from Phase 3 (WP3.6).

- Keep runtime policy under `policies/`, not under `k8s/`, because Terraform applies it and Argo CD never syncs it.
- Write Kyverno policy using the CEL-based `ValidatingPolicy` and `ImageValidatingPolicy` direction named in the plan.
- Express the repository's actual controls: digest pinning, approved registries, first-party signature and attestation checks, Pod Security hardening, required labels, and required `NetworkPolicy` coverage.
- Keep third-party image handling separate from first-party verification; the plan allows registry and digest rules where signature verification is not feasible.
- Keep policy names, messages, and labels specific enough that failing admissions explain the violated control without extra guesswork.
- Add `kyverno test` coverage for every policy and exception, with at least one passing case and one failing case.
- Keep pass and fail fixtures tiny; each should isolate one control or one exception path.
- Keep policy tests deterministic; avoid fixtures that depend on live registries, clocks, or mutable external state.
- Roll out new enforcement in Audit first and switch to Enforce only after the reports are clean and the exception story is documented.
- Keep every `PolicyException` narrow, justified, and documented with owner, scope, and reason.
- Do not use exceptions as permanent defaults for `sandbox`, system namespaces, or third-party controllers; make the scope explicit.
- Respect the ownership matrix: Terraform owns policies and exceptions, and Argo CD never syncs them.
- Keep policy package boundaries readable: admission controls in `policies/`, governance metadata in `governance/policies/`.
- When a policy changes required checks, path sensitivity, or review scope, update the blast-radius expectations under `governance/policies` in the same change.
- Keep first-party signature, provenance, and SBOM checks tied to the trusted workflow identity from Phase 2.
- Keep human-facing denial messages aligned with the runbooks so operators know the next action.
- Keep system-namespace exclusions explicit and minimal, and revisit them when the policy set expands.
- Keep every policy file named for the control it enforces, not the team or tool that wrote it.
