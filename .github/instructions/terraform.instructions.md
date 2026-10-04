---
applyTo: "infra/**/*.tf,infra/**/*.tfvars,infra/**/*.hcl"
---

# Terraform rules

Applies from Phase 3 (WP3.1).

- Keep the layout from plan section 7: reusable modules under `infra/terraform/modules`, separate stacks under `infra/terraform/stacks`, and profile inputs under `infra/terraform/profiles`.
- Keep state separated by stack so kind, platform, security, and gitops concerns stay independently recreatable.
- Pin Terraform with `required_version`, pin every provider with `required_providers`, and commit the lock files.
- Use typed variables with validation blocks and add preconditions or postconditions when a module relies on non-obvious invariants.
- Keep modules small and purpose-specific; shared behavior belongs in a module, not in copied stack code.
- Mark secrets and tokens as `sensitive`; never put real secrets in defaults, examples, outputs, comments, or plan summaries.
- Do not store GitHub App keys, Vault unseal keys, generated local passwords, or other portable credentials in Terraform variables or state.
- Run `terraform fmt`, `validate`, and `terraform test`, and keep tflint green for every stack change.
- Use Trivy IaC for misconfiguration scanning; do not add tfsec or tfsec-action because ADR-0014 replaces them.
- Keep plan-comment output sanitized, size-bounded, and review-oriented; highlight destroys and replacements without dumping secrets or huge plans into PR comments.
- Respect the ownership matrix from sections 7 and 8.12: Terraform owns cluster-scoped and shared resources, platform services, and everything under `policies/`.
- Do not move Terraform-owned policy resources into `k8s/`; Argo CD never syncs `policies/`.
- Keep Kubernetes app desired state out of Terraform except for platform installation seams the plan explicitly assigns to Terraform.
- Keep `terraform.tfvars`-style local overrides out of the repository; checked-in profile files are teaching inputs, not secret stores.
- Prefer declarative resources over `local-exec` or shell-based drift fixes; use an escape hatch only when the plan already calls for it.
- Keep provider, module, and image versions traceable to the inventory and compatibility matrix when those files land.
- Keep environment-specific values in profile inputs or stack wiring, not in hard-coded branches inside shared modules.
