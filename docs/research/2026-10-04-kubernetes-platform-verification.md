---
title: "Research record: Kubernetes platform verification (2026-10-04)"
description: "Source-checked findings on ingress, Gateway API, Kyverno, Argo CD, Kargo, OpenBao, Keycloak, SQL Server containers, and laptop resource budgets."
type: research-record
date: 2026-10-04
status: raw
---

# Kubernetes platform verification (2026-10-04)

Compiled from a research agent report plus direct verification of decision-critical facts. Publish to `docs/research/` in Phase 0 and re-verify versions at each work-package start.

| # | Item | Status as of 2026-10-04 | Key facts | Primary sources | Confidence |
|---|---|---|---|---|---|
| 1 | ingress-nginx | Retired; archived read-only | Retirement announced 2025-11-11; best-effort maintenance ended March 2026. Kubernetes recommends Gateway API. F5/NGINX Inc. controller is a separate project. | kubernetes.io/blog/2025/11/11/ingress-nginx-retirement/ ; kubernetes.io/blog/2026/01/29/ingress-nginx-statement/ | High |
| 2 | Gateway API / Envoy Gateway | Gateway API v1.5.x (v1.6.1 listed as latest supported); Envoy Gateway v1.9.2 | GA: GatewayClass, Gateway, ListenerSet, HTTPRoute, GRPCRoute, TLSRoute, TCPRoute, UDPRoute, BackendTLSPolicy, ReferenceGrant. Envoy Gateway 1.9 supports Kubernetes 1.33–1.36 and reaches EOL 2027-02-14. kind exposure is via `extraPortMappings` or `cloud-provider-kind`. | github.com/kubernetes-sigs/gateway-api/releases ; gateway.envoyproxy.io/news/releases/matrix/ | High (versions); Medium (kind exposure specifics) |
| 3 | Argo Rollouts | v1.10.0 | Gateway API traffic-router plugin is maintained in argoproj-labs; Envoy Gateway is a tested provider (EG 1.7.2 / Gateway API 1.4.1 / plugin 0.13.0). No official guidance exists for low/no-traffic Prometheus analysis. | github.com/argoproj/argo-rollouts/releases ; rollouts-plugin-trafficrouter-gatewayapi.readthedocs.io/en/latest/provider-status/ | High |
| 4 | kind / CNI | kind v0.33.0; default node image `kindest/node:v1.37.0` | kindnetd enforces NetworkPolicy through kube-network-policies and supports Admin Network Policy. Cilium on kind is documented; no official Cilium resource minimum. **Risk:** the default 1.37 node image is newer than Envoy Gateway 1.9's supported range (1.33–1.36), so pin the node image. | github.com/kubernetes-sigs/kind/releases ; kindnet.sigs.k8s.io/docs/user/network-policies/ ; docs.cilium.io/en/stable/installation/kind/ | High |
| 5 | Kyverno | v1.19.1 | `ValidatingPolicy` and `ImageValidatingPolicy` are Stable in 1.19. ImageValidatingPolicy supports Cosign keyless (issuer/subject, Rekor) plus SBOM/SLSA-style attestations. The Cosign v3 verification bug (kyverno#15327) was fixed 2026-03-16; bundles are fetched twice per check (kyverno#17833, performance only). No ClusterPolicy removal timeline was found. PolicyException scope and CLI parity for the new policy types need a spike. | kyverno.io/docs/policy-types/image-validating-policy/ ; github.com/kyverno/kyverno/issues/15327 | High / Medium |
| 6 | Argo CD | v3.5.3 stable; v3.6.0-rc1 | Breaking changes in 3.x: fine-grained RBAC, `logs` RBAC always enforced, and default `resource.exclusions` (including cert-manager CertificateRequest and Kyverno report kinds). GitHub App repository credentials are supported. ApplicationSet is in core. | argo-cd.readthedocs.io/en/stable/operator-manual/upgrading/2.14-3.0/ | High |
| 7 | Promotion tooling | Kargo v1.12.1 (Apache-2.0); gitops-promoter experimental; Argo CD Image Updater v1.3.0; ARC chart 0.15.0 | Kargo steps verified directly: `git-open-pr`, `git-merge-pr` (`wait: true` retries until mergeable), `git-wait-for-pr`. Kargo SSH repository URLs are deprecated in v1.10 and removed in v1.13 (use HTTPS). GitHub warns against self-hosted runners on public repos. | docs.kargo.io/user-guide/reference-docs/promotion-steps/ ; docs.github.com/en/actions/reference/runners/self-hosted-runners | High |
| 8 | OpenBao | v2.7.1 | **No SQL Server database plugin.** Built-in database plugins are Cassandra, InfluxDB, MySQL, PostgreSQL, and Valkey; `openbao/openbao-plugins` adds only MongoDB (verified from both repository trees). Static and transit seals are built in; cloud KMS seals are external plugins. | github.com/openbao/openbao (tree `internal/builtin/database`) ; github.com/openbao/openbao-plugins | High |
| 8b | HashiCorp Vault | v2.1.1 (2026-09-16); BSL 1.1 (licensor IBM) | `plugins/database/mssql` is present. Vault Secrets Operator v1.6.0 and vault-k8s v1.7.6 are active. The Additional Use Grant permits production use that is not a competing paid offering. | github.com/hashicorp/vault ; raw LICENSE ; github.com/hashicorp/vault-secrets-operator | High |
| 9 | Bitnami | Catalog frozen 2025-08-28; legacy registry since 2025-09-29 | Free images moved to `bitnamilegacy` (frozen, unsupported). The maintained path is the paid Bitnami Secure Images. | community.broadcom.com (Bitnami changes post) ; github.com/bitnami/charts README | High |
| 10 | Keycloak | v26.8.0; keycloak-config-cli v6.5.1; Terraform provider keycloak/keycloak v5.9.0 (community tier) | The operator's `KeycloakRealmImport` is one-time ("does not update or delete"). MSSQL is a supported database vendor (TLS via JKS/PKCS12 truststore; no mTLS). | keycloak.org/operator/realm-import ; keycloak.org/server/db | High |
| 11 | SQL Server containers | 2022 mature; 2025 GA 2025-11-18 | x86-64 only. Microsoft states that Rosetta 2, Prism, and QEMU "aren't tested or supported". Pod Security restricted compatibility is not documented, so it needs a spike. | learn.microsoft.com/en-us/sql/linux/containers/deploy | High |
| 12 | Valkey | v9.1.2; LTS lines 8.1.10 and 7.2.14 | No first-party Helm chart was confirmed. | valkey.io/download/ | High (version) |
| 13 | cert-manager / trust-manager | v1.21.2 / v0.25.0 | cert-manager has Gateway API integration; trust-manager is active. | github.com/cert-manager/cert-manager/releases ; github.com/cert-manager/trust-manager/releases | High |
| 14 | Observability | Tempo 3.x; Jaeger v2 (CNCF graduated) | Tempo 3.x needs a Kafka-compatible queue only in microservices mode; monolithic mode does not need Kafka. Use `grafana/otel-lgtm` only for local smoke and Compose. | grafana.com/docs/tempo/latest/ (deployment modes, v3.0 notes) ; jaegertracing.io/docs/2.9/ | High |
| 15 | Codespaces | 2–32 core machines | Free plan: 120 core-hours and 15 GB-month per month (about 30 h on 4 cores, 15 h on 8 cores). | docs.github.com/en/billing/concepts/product-billing/github-codespaces | High |

## Laptop footprint estimate

This is the agent's synthesis, not an official figure; measure it in WP3.0. The full stack needs roughly 8 vCPU and 16 GB dedicated to Docker as a floor, with 12–16 vCPU and 32 GB on the host for comfortable dev, staging, and prod namespaces.
