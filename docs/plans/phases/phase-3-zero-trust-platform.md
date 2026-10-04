---
title: "Phase 3: Zero-trust platform with Terraform"
description: "A reproducible local Kubernetes platform where every hop is authenticated, encrypted, authorized, segmented, observable, and policy-checked."
type: phase-plan
phase: 3
audience: [maintainers, learners]
last-verified: 2026-10-04
owner: "@RostomOhannessian"
---

# Phase 3: Zero-trust platform with Terraform

Section numbers (§) in this file refer to [the implementation plan](../implementation-plan.md). Live progress is on [the status page](../../project/STATUS.md).

| Field | Value |
| --- | --- |
| Phase branch | `phase/3-zero-trust-platform` |
| Work-package branches | `wp/3.NN-<slug>` |
| Milestone | Phase 3: Zero-trust platform |
| Release at exit | `v0.3.0` |
| Depends on | Phase 2 merged |

## Goal

a reproducible local Kubernetes platform where every hop is authenticated, encrypted, authorized, segmented, observable, and policy-checked, with no static credentials in workloads.

## Learning outcomes

- Terraform state and stack design
- Kubernetes security primitives
- Gateway API
- PKI with cert-manager
- Vault auth methods and dynamic secrets
- OIDC workload identity
- Admission control
- Platform observability
- Disaster recovery

## Scope and non-goals

In scope:

- A deterministic kind cluster and platform, provisioned by Terraform in separate stacks.
- Network segmentation (public, private, data), a local certificate authority, and Gateway API ingress.
- Vault for secrets and workload identity: GitHub OIDC and Kubernetes service-account authentication, and dynamic SQL Server credentials.
- A private data tier (SQL Server and Valkey), Keycloak, Kyverno policy enforcement, and an observability platform.
- Hosting the API on the platform with a temporary deployment script, and Terraform CI with Trivy IaC scanning and plan comments on pull requests.

Non-goals:

- No Argo CD, Argo Rollouts, Kargo, GitOps reconciliation, canary analysis, or promotion (Phase 4). The deployment script in WP3.8 is deliberately temporary and is removed in WP4.3.
- No cloud-provider-specific code. Vault is the federation broker, and the extension seams for managed Kubernetes, managed SQL, and cloud IAM are documented in Phase 4.
- No multi-cluster setup, high-availability control plane, or service mesh.
- No self-hosted runner and no change to the host's trust store unless the operator asks for it.

## Decisions introduced

| ADR | Decision | Accepted in |
| --- | --- | --- |
| [0019](../../adr/0019-resource-profiles-and-host-support.md) | Resource profiles and host support | WP3.0 |
| [0014](../../adr/0014-iac-scanning-trivy-as-tfsec-successor.md) | Trivy IaC scanning as tfsec's successor | WP3.1 |
| [0015](../../adr/0015-gateway-api-with-envoy-gateway.md) | Gateway API with Envoy Gateway | WP3.2 |
| [0008](../../adr/0008-secrets-broker-vault-over-openbao.md) | Vault over OpenBao as the secrets broker | WP3.3 |
| [0009](../../adr/0009-workload-identity-oidc-federation.md) | Workload identity through OIDC federation | WP3.3 |

New ADRs expected: the SQL Server version path, sharing the SQL Server instance with Keycloak (blast radius), the observability backends per profile, and the residual egress risk of standard NetworkPolicy.

## Prerequisites and setup changes

- Phase 2 is merged.
- A host from the [support matrix](../../adr/0019-resource-profiles-and-host-support.md): `full` needs about 16 to 18 GB of memory and 8 CPUs allocated to Docker, and `lite` about 10 GB and 4 CPUs. Apple Silicon and Windows on Arm use Codespaces or a remote x86-64 host.
- Pinned tools installed from `tools/versions.yaml`: kind, kubectl, Terraform, Helm, Chainsaw, and the Kyverno CLI.
- Host ports 8080 and 8443 free, or the documented hosts-file fallback for `*.catalog.localtest.me`.
- A directory outside the repository for Vault unseal keys and generated local credentials. Nothing in it is ever committed or copied between machines.

## Work packages

| WP | Title | Size | Key deliverables |
| --- | --- | --- | --- |
| 3.0 | Risk spikes and measurements | M | Kubernetes compatibility matrix and node-image pin; kindnet enforcement of ingress and egress NetworkPolicy, plus probes from the kubelet; SQL Server under Pod Security `restricted` (or `baseline` with scoped exceptions); Vault MSSQL dynamic credentials with the Agent native sidecar and pool clearing in the real app; Vault Redis plugin against Valkey ACLs; Keycloak Operator with idempotent `keycloak-config-cli` reconciliation and Vault static-role rotation; Envoy Gateway on kind through `extraPortMappings` on Windows, macOS, and Linux; pull-through caches and rate limits; measured RAM and CPU for the `lite`, `full`, and `ci` profiles |
| 3.1 | Terraform foundations and kind stack | M | Module and stack layout (§7); pinned versions and lock files; `terraform test`; tflint; Trivy IaC (the tfsec successor) with an AVD mapping doc and the *tfsec mapping* ADR; kind cluster (nodes by profile, high-port mappings, containerd registry mirrors, etcd encryption at rest, audit policy in the `full` profile); local, ignored state for the kind stack |
| 3.2 | Platform baseline | L | Namespaces and tiers (§8.12); Pod Security admission levels; ResourceQuotas and LimitRanges; least-privilege RBAC; default-deny NetworkPolicies plus explicit allows; cert-manager and trust-manager (local root CA, issuers, CA bundle distribution); Envoy Gateway (GatewayClass, a TLS Gateway on 8443, HTTPRoutes, BackendTLSPolicy, ClientTrafficPolicy, optional global rate limiting); CoreDNS rewrite for issuer consistency; Chainsaw probe matrix proving allowed and denied flows |
| 3.3 | Secrets and workload identity | L | Vault through Helm (Raft, TLS, audit device) with Shamir unsealing; `dev platform unseal`; key custody outside the repository; Terraform `vault` provider configuration covering Kubernetes auth roles per namespace and service account, JWT auth for GitHub OIDC with bound claims (repository, workflow ref, ref, event, audience), least-privilege policies, KV v2 paths, the audit device, and Vault Secrets Operator with namespace-scoped `VaultAuth`; a CI test in an ephemeral cluster that logs in with GitHub OIDC (positive) and is rejected by a role bound to another ref (negative); rotation and break-glass runbooks; the *OIDC choice* ADR is accepted |
| 3.4 | Data tier | M | SQL Server 2022 StatefulSet (manifests kept in this repo and applied by Terraform; cert-manager TLS; PVC; probes; PDB; resource limits) bootstrapped from a Vault KV `sa` secret; bootstrap job creates `vault-db-admin` and the per-environment databases, then disables `sa`; Vault database engine with `rotate-root`; roles `catalog-runtime-<env>` (DML), `catalog-migrator-<env>` (DDL), and static role `keycloak`; backup CronJob and a rehearsed restore drill; Valkey per environment (TLS, ACL users and channels, persistence disabled for cache semantics); Kyverno rules forbidding external Services in `data` |
| 3.5 | Identity | M | Keycloak Operator and Keycloak (database through the Vault static role; hostname set to the external issuer); `keycloak-config-cli` job reconciling `catalog` and `platform` from the realm files, with secrets injected from Vault; tests of token claims against the permission matrix; SSO for Vault and Grafana; local admins removed after bootstrap |
| 3.6 | Policy enforcement | M | Kyverno installation; `ValidatingPolicy` and `ImageValidatingPolicy` set (see below); scoped `PolicyException`s; rollout from Audit to Enforce once reports are clean; `kyverno test` plus Chainsaw admission tests; policy-report dashboard; break-glass doc (failurePolicy and excluded system namespaces) |
| 3.7 | Observability platform | M | OTel Collector (gateway mode); trimmed kube-prometheus-stack; Grafana (SSO, dashboards as code); Tempo (monolithic) and Loki (single binary) in the `full` profile; alert rules and SLO recording rules; exemplars that link metrics to traces |
| 3.8 | Application hosting | M | `k8s/apps/catalog/base` and `overlays/dev`: a Deployment for now, becoming a Rollout in Phase 4; Vault Agent native sidecar; migration Job; ServiceAccount, NetworkPolicies, and HTTPRoute; `sandbox` namespace for locally built images; temporary `dev platform deploy` script, removed in Phase 4; end-to-end smoke test (Keycloak token → gateway → API → SQL and Valkey) as a k6 Job, using a Vault-delivered client secret; rotation and revocation demo |
| 3.9 | Terraform CI and plan comments | M | `terraform.yml`: fmt, validate, tflint, `terraform test`, and Trivy IaC (SARIF) on every infrastructure PR. The fork-safe ephemeral-baseline plan creates a `ci`-profile kind cluster, applies `master`, then runs `terraform plan` for the PR per stack. `terraform-plan-comment.yml` (`workflow_run`) posts one sticky, sanitized, size-bounded comment summarizing actions per stack, with destroys and replacements highlighted and the full plan in a collapsed section. Also: job summaries; an optional operator-run live plan (`dev tf plan --comment <pr>`) using the operator's own `gh` authentication |
| 3.10 | Learning content and phase exit | L | Tool pages, labs, infrastructure test plan, host-difference notes, published profile measurements, teardown and disaster-recovery runbooks, retrospective, phase PR, `v0.3.0` |

### Policy set (WP3.6)

- Digest pinning everywhere except `sandbox` and system namespaces.
- An allowlist of registries.
- First-party image verification in `catalog-*`: a Cosign keyless signature from the trusted workflow identity, plus SLSA provenance and SBOM attestations.
- Mutation of third-party image tags to digests at Pod admission.
- Non-root users, read-only root filesystems, all capabilities dropped, the `RuntimeDefault` seccomp profile, no privilege escalation, and no host access.
- Resource requests and limits, and probes in application namespaces.
- Required `app.kubernetes.io/*` labels.
- No `NodePort` or `LoadBalancer` Services outside `edge`.
- No `latest` tags.
- A required NetworkPolicy per namespace.

## Test plan

| Check | What it proves | Gate |
| --- | --- | --- |
| Chainsaw probe matrix | Allowed flows work and every other flow is denied, including cross-environment and data-tier access | Blocking |
| `kyverno test` and admission tests | Every policy admits compliant workloads and denies the documented violations, including exceptions | Blocking |
| `terraform fmt`, `validate`, `test`, tflint | Modules are correct and consistent | Blocking |
| Trivy IaC | No unaddressed misconfiguration; SARIF uploaded | Blocking |
| Vault authentication tests | GitHub OIDC login succeeds for the bound claims and fails for another ref; Kubernetes roles bind to exact service accounts | Blocking |
| Credential lifecycle | A dynamic SQL credential rotates, is revoked, and the API reconnects with no failed requests beyond the documented grace | Blocking |
| Keycloak claim tests | Tokens from the reconciled realm map to the permission matrix | Blocking |
| Data recovery drill | The catalog database is backed up and restored | Rehearsed |
| Lifecycle | Clean create, apply, destroy, and re-apply (idempotence); certificate renewal; state loss and recovery | Rehearsed and in CI where feasible |
| End-to-end smoke | A token obtained from Keycloak reaches the API through the gateway and reads and writes SQL and Valkey | Blocking |
| Profile measurements | Measured memory and CPU for `lite`, `full`, and `ci` | Published; budgets amended by ADR if exceeded |

## Tool pages introduced

- Terraform (testing, tflint, state)
- kind
- Kubernetes security primitives
- NetworkPolicy and kindnet (Cilium as an alternative)
- Gateway API and Envoy Gateway
- cert-manager and trust-manager
- HashiCorp Vault (auth methods, KV, database engine, policies, audit, seal and unseal)
- Vault Agent
- Vault Secrets Operator
- SQL Server on Kubernetes
- Valkey on Kubernetes
- Keycloak Operator and `keycloak-config-cli`
- Kyverno
- Chainsaw
- OpenTelemetry Collector
- Prometheus and kube-prometheus-stack
- Grafana
- Tempo
- Loki
- Trivy IaC
- Registry pull-through caches
- etcd encryption at rest

## Labs

1. Stack boundaries and state.
2. Prove segmentation with probes, including denied flows.
3. Exchange GitHub OIDC for a Vault token (CI) and a Kubernetes service-account token for a Vault token (cluster).
4. Watch a dynamic SQL credential rotate, get revoked, and the app reconnect.
5. Decode a Keycloak token and map it to policies.
6. Trigger Kyverno denials: an unsigned image, a `NodePort` in `data`, a root container.
7. Unseal Vault and run break-glass.
8. Back up and restore the catalog database.
9. Read a Terraform plan PR comment.

## Evidence required

- Probe matrix output, `kyverno test` output, and the OIDC login transcripts (positive and negative).
- The rotation and revocation transcript, and the restore drill record.
- A Terraform plan comment on a pull request, including a fork pull request.
- The published profile measurements and the host-difference notes.
- Spike report from WP3.0, the `v0.3.0` release, and the retrospective.

## Risks and rollback

| Risk | Mitigation |
| --- | --- |
| Laptop resources are insufficient (R2) | `lite` and `ci` profiles, registry caches, measured budgets, Codespaces fallback |
| Apple Silicon blocked by x86-only SQL Server (R3) | Support matrix and documented fallback |
| Vault unseal and trust-anchor custody (R7) | Operator-custody runbook, disposable environments, rotation drills |
| Kubernetes version skew across components (R12) | Compatibility matrix, pinned node image, scheduled check |
| Third-party DNS for `localtest.me` is unavailable (R17) | Documented hosts-file fallback |

Rollback: `dev platform destroy` removes the cluster, and `dev platform create` rebuilds it. Local environments are disposable by design; lost Vault keys mean recreating the environment, not recovering it.

## Exit gate

- **Clean apply:**
  - A clean host on the documented prerequisites creates kind and applies every stack, with no committed credentials and no secrets in Actions.
  - Teardown is clean.
- **Proofs:**
  - The probe matrix proves public → private → data segmentation, and that cross-environment and data-tier exposure are denied.
  - Vault leases, rotation, and revocation work inside the running app.
  - GitHub OIDC login passes the positive and negative tests.
  - Keycloak claims enforce the permission matrix.
  - Kyverno denies non-compliant workloads in Enforce mode.
- **CI:**
  - Trivy IaC is gating.
  - Plan comments appear on PRs, including fork PRs.
- **Resource budget:** profile measurements are published and within budget, or the budget is amended by ADR.
- **Docs and tracking:** docs, test plan, status, and Milestone are complete, and `v0.3.0` is tagged.
