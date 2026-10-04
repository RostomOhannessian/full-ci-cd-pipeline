---
title: "ADR-0011: Adopt pull-based GitOps with Argo CD"
description: "Use Argo CD to reconcile the cluster from Git so CI never holds cluster credentials and the local-cluster topology remains observable and auditable."
status: proposed
date: 2026-10-04
decision-makers: ["@RostomOhannessian"]
accepted-in: "WP4.3"
supersedes: []
superseded-by: []
tools:
  - argo-cd
  - flux
  - kustomize
  - terraform
  - kargo
related-adrs: ["0010"]
evidence:
  - docs/research/2026-10-04-github-supply-chain-verification.md
  - docs/research/2026-10-04-kubernetes-platform-verification.md
---

# ADR-0011: Adopt pull-based GitOps with Argo CD

## Context and problem statement

Phase 4 needs a delivery model that works with a laptop-local cluster, enforces separation between build and deploy, and leaves a readable audit trail for learners. Plan sections 4.2, 8.12, 8.13, and 8.14 define the constraints and ownership boundaries, and WP4.3 is where the control plane is accepted.

The core problem is reachability. GitHub-hosted runners cannot reach the local cluster, so a push-based CD pipeline cannot both apply changes and verify the cluster honestly. The plan therefore separates responsibilities:

- CI builds, tests, scans, signs, attests, and publishes.
- The cluster pulls desired state and reconciles it.
- Promotion changes happen through pull requests in Git.

Plan section 8.12 also sets an explicit ownership matrix:

| Owner | Scope |
| --- | --- |
| Terraform | Shared and cluster-scoped resources, plus platform services |
| Argo CD | Everything under `k8s/` |
| Kargo | Image digests written through promotion pull requests |

This ADR decides whether that delivery model should be push-based CI/CD or pull-based GitOps, and which GitOps controller best fits the repository.

## Decision drivers

- CI must not hold cluster credentials.
- The local cluster must remain deployable even when GitHub cannot reach it inbound.
- Drift detection and self-healing must be part of the default model.
- Rollback must have a clear Git and cluster story.
- The repository should teach the delivery state through a first-class UI and auditable objects.

## Considered options

1. Pull-based GitOps with Argo CD.
2. Push-based CD from GitHub Actions with `kubectl apply` or `helm upgrade`.
3. Pull-based GitOps with Flux.
4. Terraform-managed application delivery.

## Decision outcome

Chosen option: **pull-based GitOps with Argo CD**, because it best fits the local-cluster reachability model, keeps credentials out of CI, and gives the clearest teaching surface for the repository's progressive-delivery flow.

Argo CD is chosen over Flux for this repository for three concrete reasons:

- It gives a strong UI for learners, operators, and evidence capture.
- ApplicationSet is in core, which fits the `k8s/apps/*/overlays/*` model in the plan.
- It integrates directly with the rest of the selected toolchain: Argo Rollouts for canaries and Kargo for promotion pull requests.

The Kubernetes verification record also notes Argo CD 3.x changes that the implementation must absorb (checked 2026-10-04):

- `logs` RBAC is always enforced.
- Fine-grained RBAC applies.
- Default `resource.exclusions` include kinds such as cert-manager `CertificateRequest` and Kyverno report objects.

### Consequences

- Good: CI never stores or uses cluster credentials for normal delivery.
- Good: the cluster keeps reconciling toward Git even when GitHub cannot reach it inbound.
- Good: drift detection, sync history, rollback evidence, and promotion history become native parts of operations and teaching.
- Bad: GitOps adds controller footprint and control-plane concepts that a smaller project might skip. Mitigation: WP4.0 spikes the defaults and WP3.0 measures the platform budget before Phase 4 finalizes the stack.
- Bad: Argo CD ownership boundaries must stay explicit or Terraform and Argo CD will fight. Mitigation: enforce the plan's ownership matrix and keep platform policies out of `k8s/` where Terraform owns them.
- Neutral: Flux remains a credible alternative. This ADR chooses Argo CD for fit and teaching value, not because Flux is weak.

### Confirmation

The decision is accepted only if the following work packages verify it:

- **WP4.0** proves the Argo CD 3.x defaults, Rollouts integration, and Kargo interaction on the pinned Kubernetes version.
- **WP4.3** proves the root Application, AppProjects, ApplicationSet flow, automated sync, self-heal, prune, and deployment reporting.
- **WP4.6** proves rollback drills under the GitOps model, including emergency rollback and Git reconciliation.
- The resulting control plane must obey the ownership matrix from plan section 8.12.

## Pros and cons of the options

### Pull-based GitOps with Argo CD

- Good: matches the connectivity model and keeps deploy authority inside the cluster.
- Good: strong UI and ecosystem fit for Rollouts, ApplicationSet, and Kargo.
- Good: makes drift and sync state first-class operational evidence.
- Bad: larger platform footprint than a simple push pipeline.
- Bad: requires careful RBAC and resource-exclusion tuning on Argo CD 3.x.

### Push-based CD from GitHub Actions with `kubectl apply` or `helm upgrade`

- Good: simpler for small internet-reachable clusters.
- Good: fewer in-cluster controllers.
- Bad: requires CI to hold cluster credentials, which conflicts with the security doctrine.
- Bad: does not fit the laptop-local cluster because hosted runners cannot reach it.
- Bad: self-healing and drift correction are extra work, not default behavior.

### Pull-based GitOps with Flux

- Good: credible GitOps controller with a strong declarative model.
- Good: smaller surface can appeal to teams that prefer a CLI-first workflow.
- Bad: this repository values the UI and the first-class Argo ecosystem integrations more highly.
- Bad: would weaken the direct teaching story around Rollouts and Kargo that the plan already assumes.

### Terraform-managed application delivery

- Good: one tool could own both cluster-scoped resources and application deployment.
- Good: clear plan-and-apply workflow for infrastructure teams.
- Bad: blurs the boundary between long-lived platform resources and rapidly changing app overlays.
- Bad: weaker fit for progressive delivery, reconciliation loops, and promotion pull requests.

## Revisit when

- The repository stops targeting a local cluster and can safely use inbound-reachable managed environments.
- WP4.0 or WP4.3 shows that Argo CD's control-plane cost is not acceptable on the supported host profiles.
- Flux gains a materially better fit for the teaching and promotion flow than Argo CD.
- The ownership matrix in plan section 8.12 cannot be kept clean in practice.

## Further reading

- Kubernetes platform verification: [../research/2026-10-04-kubernetes-platform-verification.md](../research/2026-10-04-kubernetes-platform-verification.md) (checked 2026-10-04)
- GitHub and supply-chain verification: [../research/2026-10-04-github-supply-chain-verification.md](../research/2026-10-04-github-supply-chain-verification.md) (checked 2026-10-04)
- ADR-0010 promotion engine choice: [./0010-promotion-engine-kargo.md](./0010-promotion-engine-kargo.md) (checked 2026-10-04)
