---
applyTo: "k8s/**/*.yml,k8s/**/*.yaml"
---

# Kubernetes manifest rules

Applies from Phase 3 (WP3.8), with promotion rules extending in Phase 4 (WP4.5).

- Treat everything under `k8s/` as Argo CD-owned desired state; do not duplicate Terraform-owned cluster or policy resources here.
- Preserve the Kustomize structure from plan section 7: `bootstrap`, app `base`, reusable `components`, and environment `overlays`.
- Use Gateway API resources, not Ingress, because ADR-0015 makes Envoy Gateway the only entry point.
- Keep manifests compatible with Pod Security `restricted`: non-root, no privilege escalation, dropped capabilities, read-only root filesystem where feasible, and `RuntimeDefault` seccomp.
- Set container and pod `securityContext` explicitly; never rely on permissive image defaults.
- Set resource requests and limits for every workload in application namespaces.
- Add startup, readiness, and liveness probes that follow the section 8.9 health model.
- Keep health traffic on the management port and out of the public gateway path.
- Do not define default-deny policies here, because Terraform owns them. Add only the explicit allow `NetworkPolicy` resources for your app that section 8.12 documents.
- Keep namespace-scoped app resources out of `bootstrap`; reserve bootstrap for Argo CD control-plane setup.
- Pin first-party images by digest in overlays and keep third-party image references compatible with the registry and digest rules enforced by policy.
- Use one dedicated `ServiceAccount` per workload with least privilege; do not share default service accounts across apps.
- Keep Vault delivery aligned with the plan: the API and migration job use the Vault Agent native sidecar pattern, while platform controllers use Vault Secrets Operator outside `k8s/`.
- Do not add static Kubernetes Secrets for application credentials that the secret-broker design is meant to supply.
- Keep environment differences in overlays, not by copying whole manifests.
- Let Kargo change only image digests through promotion pull requests; do not hand-edit promoted digests on long-lived branches.
- Keep third-party chart output or copied manifests out of `k8s/` unless the repository owns the rendered form intentionally.
- Keep HTTPRoutes, backend policies, and Services consistent with the re-encrypted flow the topology table describes.
- Keep environment-only overrides small; if an overlay rewrites most of a base, split the base or add a component instead.
- Keep rollout-ready shapes in place for Phase 4 so Deployments can become Rollouts without a manifest rewrite.
