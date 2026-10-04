---
title: "ADR-0008: Use Vault instead of OpenBao for the secrets broker"
description: "Use HashiCorp Vault Community Edition because this repository needs native dynamic SQL Server credentials and workload-identity brokering."
status: proposed
date: 2026-10-04
decision-makers: ["@RostomOhannessian"]
accepted-in: "WP3.3"
supersedes: []
superseded-by: []
tools:
  - vault
  - openbao
  - vault-agent
  - vault-secrets-operator
  - sql-server
  - valkey
related-adrs: ["0004", "0009"]
evidence:
  - docs/research/2026-10-04-decision-critical-addendum.md
  - docs/research/2026-10-04-kubernetes-platform-verification.md
---

# ADR-0008: Use Vault instead of OpenBao for the secrets broker

## Context and problem statement

Phase 3 needs one secrets broker for the API, migration job, platform controllers, and GitHub Actions trust flow. The plan requires dynamic SQL Server credentials, rotation and revocation inside the running app, Kubernetes and GitHub OIDC authentication, and no static credentials in workloads or CI. The load-bearing design points are plan sections 8.8 and 8.12, plus WP3.0, WP3.3, and WP3.4.

The hard constraint is SQL Server. The decision-critical addendum says OpenBao 2.7.1 has no SQL Server database plugin and that a repository-tree search for `mssql` and `sqlserver` found nothing (checked 2026-10-04). The same addendum says Vault 2.1.1 includes `plugins/database/mssql` (checked 2026-10-04).

This repository also needs two delivery modes:

- A native sidecar for the API and migration job that can render short-lived credentials to files and rotate them.
- A controller-friendly path for platform components such as Kargo and Keycloak, where syncing selected secrets into Kubernetes Secrets is acceptable.

License risk also matters. ADR-0004 allows non-OSI tools when they are used as tools only, are not linked into shipped code, and are documented honestly. The addendum records Vault under BSL 1.1 with an additional use grant that permits internal production use that is not a competing paid offering, as read on 2026-10-04.

Keycloak adds one more constraint. The addendum says Keycloak can use SQL Server with server-verified TLS but not mutual TLS, so its database credential must remain a password-managed path rather than client-certificate auth (checked 2026-10-04).

## Decision drivers

- Issue dynamic SQL Server credentials natively, without a custom database plugin.
- Support Kubernetes service-account auth and GitHub OIDC auth in the same broker.
- Rotate and revoke credentials in the real app without redeploying pods.
- Stay cloud-agnostic and keep the platform teachable on a local cluster.
- Keep the operational footprint small enough for WP3.0 measurements.
- Stay within ADR-0004's dependency and tool-license policy.

## Considered options

1. HashiCorp Vault Community Edition with Vault Agent and Vault Secrets Operator.
2. OpenBao with a dual-login password-rotation job.
3. OpenBao with a custom SQL Server plugin maintained in Go.
4. A cloud secret manager with cloud-native federation.

## Decision outcome

Chosen option: **HashiCorp Vault Community Edition with Vault Agent and Vault Secrets Operator**, because it is the only option here that already satisfies the primary driver: native dynamic SQL Server credentials, plus the auth methods and delivery patterns the plan needs, without giving up the local and cloud-agnostic target.

The concrete repository design is:

- Vault is the secrets broker and workload-identity broker.
- The API and migration job use Vault Agent as a native sidecar that renders short-lived files into memory-backed storage.
- Platform controllers use Vault Secrets Operator with namespace-scoped auth and narrowly scoped sync targets.
- Vault uses Shamir unsealing, with unseal keys held by the operator outside the repository.
- The SQL bootstrap flow disables `sa` after bootstrap and uses `rotate-root` for the broker's SQL admin account.
- Keycloak uses a Vault static role for its database credential because the verified SQL Server path is password plus TLS server verification, not mTLS.
- Dynamic Valkey credentials remain an open question until the WP3.0 spike proves Vault's Redis plugin works with Valkey ACLs; until then that part is explicitly not verified.

### Consequences

- Good: the repository gets one broker that can handle GitHub OIDC, Kubernetes auth, dynamic SQL Server credentials, revocation, and audited access with one operator model.
- Good: the API design in plan section 8.8 stays intact because Vault Agent can rotate credentials without rebuilding the pod or storing them in the Deployment spec.
- Bad: Vault adds real operational footprint and bootstrap ceremony. Mitigation: WP3.0 measures the cost before the platform hardens around it, and the plan keeps the unseal and break-glass paths explicit.
- Bad: Vault is under BSL 1.1, not an OSI license. Mitigation: treat it as a tool-only dependency under ADR-0004, document the license honestly, and revisit if the terms change.
- Bad: platform controllers receive copied Kubernetes Secrets for compatibility. Mitigation: keep scope narrow, sync only the paths each controller needs, and keep the API and migration job on the sidecar path instead of copied secrets.
- Neutral: this decision does not settle Valkey dynamic credentials. WP3.0 decides whether to use Vault's Redis-compatible path or rotated static ACL users.

### Confirmation

This decision is accepted only if the Phase 3 work packages prove it:

- **WP3.0** measures the footprint, tests Vault's Redis-compatible path against Valkey ACLs, and proves the sidecar pattern in the real app.
- **WP3.3** proves GitHub OIDC auth, Kubernetes auth, least-privilege policies, and break-glass procedures.
- **WP3.4** proves SQL Server credential rotation, revocation, session kill behavior, pool clearing, and successful reconnection by the application.
- The labs and runbooks from WP3.3 and WP3.4 must show the operator workflow, not just unit tests.

## Pros and cons of the options

### HashiCorp Vault Community Edition with Vault Agent and Vault Secrets Operator

- Good: verified `mssql` database support exists in the product tree (checked 2026-10-04).
- Good: matches both secret-delivery patterns in the plan: native sidecar for workloads and synchronized secrets for controllers.
- Good: keeps the design cloud-agnostic and local-cluster friendly.
- Bad: more operational work than a hosted manager.
- Bad: BSL 1.1 needs an explicit policy exception as a tool-only dependency.

### OpenBao with a dual-login password-rotation job

- Good: stays on an MPL-2.0 project and avoids the BSL issue.
- Good: can still centralize static credentials and rotate them with custom automation.
- Bad: does not produce per-workload leases, so it misses the main security and teaching goal.
- Bad: rotation becomes a separate job design, not a native database engine capability.
- Bad: the repository still has no native path for dynamic SQL Server credentials.

### OpenBao with a custom SQL Server plugin maintained in Go

- Good: would preserve the OpenBao preference while keeping dynamic credentials.
- Good: could become a teaching artifact for plugin authoring.
- Bad: this repository would now own and secure a custom auth-critical plugin.
- Bad: testing, upgrades, and support become permanent maintenance work outside the portfolio's scope.
- Bad: Phase 3 would start with a custom extension before the platform baseline is proven.

### A cloud secret manager with cloud-native federation

- Good: reduces the self-hosted operational burden.
- Good: some managed offerings have mature rotation workflows.
- Bad: conflicts with the repository's cloud-agnostic local-cluster target.
- Bad: would make the GitHub OIDC and workload-identity story provider-specific instead of portable.

## Revisit when

- OpenBao ships a supported SQL Server database plugin and the addendum can be updated with a direct verification.
- Vault's license terms change in a way that no longer fits ADR-0004's tool-only rule.
- WP3.0 shows that the sidecar, operator, or resource budget is not acceptable on the supported host profiles.
- WP3.0 shows that the Valkey credential path is materially weaker or more fragile than documented here.

## Further reading

- Decision-critical addendum: [../research/2026-10-04-decision-critical-addendum.md](../research/2026-10-04-decision-critical-addendum.md) (checked 2026-10-04)
- Kubernetes platform verification: [../research/2026-10-04-kubernetes-platform-verification.md](../research/2026-10-04-kubernetes-platform-verification.md) (checked 2026-10-04)
- ADR-0004 license policy: [./0004-licensing-and-dependency-license-policy.md](./0004-licensing-and-dependency-license-policy.md) (checked 2026-10-04)
