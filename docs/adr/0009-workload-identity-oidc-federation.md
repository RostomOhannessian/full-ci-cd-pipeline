---
title: "ADR-0009: Use Vault-backed OIDC federation for workloads and automation"
description: "Authenticate GitHub Actions and Kubernetes workloads through Vault so the platform runs without static credentials."
status: proposed
date: 2026-10-04
decision-makers: ["@RostomOhannessian"]
accepted-in: "WP3.3"
supersedes: []
superseded-by: []
tools:
  - vault
  - github-actions
  - keycloak
  - kubernetes
related-adrs: ["0004", "0008"]
evidence:
  - docs/research/2026-10-04-decision-critical-addendum.md
  - docs/research/2026-10-04-github-supply-chain-verification.md
  - docs/research/2026-10-04-kubernetes-platform-verification.md
---

# ADR-0009: Use Vault-backed OIDC federation for workloads and automation

## Context and problem statement

The platform needs one identity story for four actor types: GitHub Actions, Kubernetes workloads, human operators, and API clients. Plan sections 8.7, 8.8, and 8.12 require no static credentials in workloads or CI, least-privilege access, and a local cluster that remains cloud-agnostic. WP3.3 is the work package that must prove the trust relationships.

The connectivity model is the deciding constraint. Plan section 4.2 says GitHub-hosted runners cannot reach a developer's laptop-local cluster, so CI cannot prove a live trust path against the maintainer's cluster. Plan gap G20 closes this by requiring CI to validate the Vault JWT trust policy inside an ephemeral kind cluster that the workflow creates itself.

The plan already chooses Vault as the secrets and workload-identity broker in ADR-0008. This ADR decides how every actor reaches it:

- GitHub Actions needs short-lived auth without repository or environment secrets.
- Pods need short-lived auth bound to namespace and service account.
- Humans need browser-based sign-in for platform UIs.
- API clients need OAuth 2.0 client credentials for service-to-service access.

The target is a local, cloud-agnostic cluster, so provider-specific IAM federation does not solve the default case.

## Decision drivers

- Remove static credentials from GitHub Actions and from workload manifests.
- Bind trust to verifiable claims, not to broad shared tokens.
- Keep the design cloud-agnostic and runnable on a local kind cluster.
- Make the trust policy testable in CI with both positive and negative cases.
- Keep human sign-in and API client auth aligned with the rest of the identity model.

## Considered options

1. Vault JWT auth for GitHub OIDC and Vault Kubernetes auth for pods.
2. Long-lived tokens stored in GitHub secrets and Kubernetes Secrets.
3. Cloud-provider IAM federation.
4. SPIFFE and SPIRE for workload identity.

## Decision outcome

Chosen option: **Vault JWT auth for GitHub OIDC and Vault Kubernetes auth for pods**, because it satisfies the no-static-credentials goal on the actual target platform instead of on a different cloud target.

The identity split is:

- GitHub Actions authenticates to Vault with GitHub's OIDC token through Vault's JWT auth method.
- The JWT role binds on repository, workflow reference, ref, event, and audience claims, exactly as WP3.3 in [the Phase 3 plan](../plans/phases/phase-3-zero-trust-platform.md) requires.
- Pods authenticate through Vault's Kubernetes auth method, with one role per namespace and service account.
- Humans sign in to Vault, Grafana, Argo CD, and Kargo through Keycloak OIDC.
- API clients use Keycloak OAuth 2.0 client credentials.

This keeps Vault as the federation broker for automation and workloads, and Keycloak as the human and API identity provider.

CI proves the decision in an ephemeral cluster:

- A positive test logs in with a token that matches the bound claims.
- A negative test presents a token that is valid GitHub OIDC but bound to another ref and must be rejected.

### Consequences

- Good: the repository can honestly say no static credentials are required in GitHub Actions or workload manifests.
- Good: trust is scoped. A workflow on the wrong ref, or a pod on the wrong service account, does not inherit access by accident.
- Good: the design remains portable because the broker is Vault, not a cloud vendor's IAM surface.
- Bad: there are now two identity systems to explain: Vault for workload and automation federation, and Keycloak for user and API auth. Mitigation: keep their scopes explicit in the docs and labs.
- Bad: JWT claim bindings and Kubernetes auth roles are easy to misconfigure. Mitigation: WP3.3 makes the positive and negative tests mandatory and stores them as CI evidence.
- Neutral: cloud IAM federation is deferred, not rejected forever. If the deployment target changes later, this ADR can be superseded with evidence.

### Confirmation

The decision stands only if Phase 3 proves the policy behavior end to end:

- **WP3.3** configures Vault JWT auth for GitHub OIDC with bound claims and proves both acceptance and rejection paths in CI.
- **WP3.3** configures Vault Kubernetes auth roles per namespace and service account and proves least-privilege access from workloads.
- **WP3.5** proves Keycloak claim mapping for API scopes and platform SSO.
- The labs must show token exchange and failure cases, not only success screenshots.

## Pros and cons of the options

### Vault JWT auth for GitHub OIDC and Vault Kubernetes auth for pods

- Good: matches the local-cluster target and the connectivity model.
- Good: uses short-lived identity documents issued by the platform that already knows the actor.
- Good: gives one brokered trust surface for both CI and workloads.
- Bad: adds Vault policy and auth-method complexity.
- Bad: requires an ephemeral-cluster test harness because hosted runners cannot reach the maintainer's cluster.

### Long-lived tokens stored in GitHub secrets and Kubernetes Secrets

- Good: simple to understand and quick to wire up.
- Good: works even when other federation features are unavailable.
- Bad: violates the plan requirement that there be no secrets in GitHub Actions beyond the platform token.
- Bad: broad shared tokens are harder to scope, rotate, and audit.
- Bad: teaches the wrong operating model for a zero-trust platform.

### Cloud-provider IAM federation

- Good: strong fit for managed cloud platforms.
- Good: can remove one self-hosted broker from the path.
- Bad: does not solve the local-cluster teaching target.
- Bad: would make the repository examples provider-specific.

### SPIFFE and SPIRE for workload identity

- Good: purpose-built workload identity model with strong workload semantics.
- Good: could reduce some Kubernetes-auth coupling later.
- Bad: extra footprint and operational concepts are deferred by the plan.
- Bad: it still does not replace the GitHub OIDC to Vault trust path needed here.

## Revisit when

- The default target moves from a local cluster to a managed cloud where provider IAM federation becomes the simpler path.
- WP3.3 cannot produce stable positive and negative trust tests in CI.
- SPIFFE and SPIRE become acceptable within the measured profile budget and the maintainer wants a workload-identity-specific stack.
- GitHub materially changes the OIDC claims or limits in a way that breaks the bound-claims model.

## Further reading

- Kubernetes platform verification: [../research/2026-10-04-kubernetes-platform-verification.md](../research/2026-10-04-kubernetes-platform-verification.md) (checked 2026-10-04)
- GitHub and supply-chain verification: [../research/2026-10-04-github-supply-chain-verification.md](../research/2026-10-04-github-supply-chain-verification.md) (checked 2026-10-04)
- ADR-0008 secrets broker choice: [./0008-secrets-broker-vault-over-openbao.md](./0008-secrets-broker-vault-over-openbao.md) (checked 2026-10-04)
