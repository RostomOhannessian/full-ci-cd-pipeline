---
title: "ADR-0010: Use Kargo for promotion pull requests"
description: "Use Kargo in the cluster to promote one verified image digest across environments because GitHub-hosted runners cannot verify a laptop-local cluster."
status: proposed
date: 2026-10-04
decision-makers: ["@RostomOhannessian"]
accepted-in: "WP4.5"
supersedes: []
superseded-by: []
tools:
  - kargo
  - argo-cd
  - argo-rollouts
  - github-apps
  - argocd-image-updater
  - gitops-promoter
related-adrs: ["0008", "0011"]
evidence:
  - docs/research/2026-10-04-decision-critical-addendum.md
  - docs/research/2026-10-04-github-supply-chain-verification.md
  - docs/research/2026-10-04-kubernetes-platform-verification.md
---

# ADR-0010: Use Kargo for promotion pull requests

## Context and problem statement

Phase 4 needs one verified image digest to move from dev to staging to prod through pull requests, canary analysis, and auditable rollback. The plan's promotion and rollback design is in sections 8.13 and 8.14, and WP4.5 is the acceptance point.

Two verified constraints make a traditional GitHub Actions promotion workflow a poor fit:

- GitHub-hosted runners cannot reach a laptop-local cluster, so they cannot observe whether a dev rollout is actually healthy before promoting it.
- Pull requests created with the workflow `GITHUB_TOKEN` do not trigger downstream workflows, which breaks a PR-driven promotion chain.

The GitHub verification record and plan finding F7 both make those constraints explicit (checked 2026-10-04). The same record also warns against self-hosted runners for public repositories.

The decision-critical addendum confirms the Kargo behavior that matters here (checked 2026-10-04):

- `git-open-pr` opens the promotion pull request.
- `git-merge-pr` merges synchronously and can retry with `wait: true` until the PR becomes mergeable.
- `git-wait-for-pr` supports the manual prod path.
- SSH repository URLs are deprecated in Kargo v1.10 and removed in v1.13, so the repository URL must be HTTPS.

The repository also needs a least-privilege write identity. Plan section 8.12 assigns Kargo authority only over image digests through promotion pull requests, not broad repository administration.

## Decision drivers

- Promote the same signed and attested digest across all environments.
- Verify health from inside the cluster, where the deployment actually runs.
- Keep Git authoritative through pull requests, reviews, and rulesets.
- Avoid self-hosted runners on a public repository.
- Use the smallest GitHub write scope that still supports promotion.

## Considered options

1. Kargo with a least-privilege GitHub App.
2. GitHub Actions-driven promotion with environments and approvals.
3. Self-hosted runners managed through Actions Runner Controller.
4. Argo CD Image Updater.
5. gitops-promoter.

## Decision outcome

Chosen option: **Kargo with a least-privilege GitHub App**, because it is the only evaluated option that can both observe in-cluster rollout health and keep promotion changes in Git as reviewed pull requests without relying on self-hosted runners.

The repository-specific design is:

- Kargo runs in the cluster and watches GHCR for new verified Freight.
- It opens pull requests that change only image digests under `/k8s`.
- The `catalog-promoter` GitHub App gets only `contents` and `pull-requests` write access on this repository.
- The App key lives in Vault and is synced only into the Kargo namespace.
- Dev and staging promotions auto-merge after required checks and verification pass.
- Prod promotion stays manual: Kargo opens the PR, then waits for a human merge.
- Merge queue is not part of the design because the GitHub Free personal-account path does not provide it.

### Consequences

- Good: promotion verification now happens where the rollout and analysis data live, not in a runner that cannot see the cluster.
- Good: each environment change remains a pull request, so learners can inspect the exact Git delta and the approval history.
- Good: Kargo promotes one digest, not one rebuilt image per stage.
- Bad: another controller must run, secure its credentials, and fit within the Phase 3 and 4 resource budgets. Mitigation: WP4.0 spikes the footprint and workflow semantics before the final pipeline lands.
- Bad: Kargo's merge step is synchronous and does not integrate with merge queue. Mitigation: do not use merge queue here, and keep prod as a human-merged PR.
- Neutral: Kargo writes only the desired Git change. Argo CD still owns reconciliation, and Rollouts still own rollout execution.

### Confirmation

This decision is accepted only when the following work packages pass:

- **WP4.0** proves Kargo under the real ruleset model, including `git-merge-pr wait` behavior and the manual prod path with `git-wait-for-pr`.
- **WP4.5** proves the end-to-end promotion flow from GHCR digest discovery through PR creation, merge, sync, and verification.
- **WP4.6** proves rollback drills still work when promotions are Kargo-driven.
- The evidence bundle must include promotion PRs, Kargo Freight history, and verification results.

## Pros and cons of the options

### Kargo with a least-privilege GitHub App

- Good: designed for in-cluster promotion orchestration and Git-based delivery.
- Good: verified PR steps exist and match the desired dev, staging, and prod flows.
- Good: supports a very small write scope through a dedicated GitHub App.
- Bad: adds platform complexity and another namespace to operate.
- Bad: requires Vault-managed app credentials and App governance.

### GitHub Actions-driven promotion with environments and approvals

- Good: fewer moving parts for teams that can reach their cluster from runners.
- Good: familiar GitHub-native approval surface.
- Bad: the runners in this project cannot verify the local cluster's live state.
- Bad: PRs created with `GITHUB_TOKEN` do not trigger follow-on workflows, which breaks a PR-based promotion chain.

### Self-hosted runners managed through Actions Runner Controller

- Good: would let Actions reach the cluster directly.
- Good: keeps the promotion logic in one workflow system.
- Bad: GitHub warns against self-hosted runners on public repositories, which conflicts with this repository's publication strategy.
- Bad: expands the attack surface and operational burden more than the chosen design.

### Argo CD Image Updater

- Good: smaller mental model than Kargo.
- Good: works for simple image-update flows.
- Bad: the verified tool is image-tag focused, not a full multi-stage promotion engine with PR choreography.
- Bad: it is not the close fit for the reviewed dev-to-staging-to-prod workflow.

### gitops-promoter

- Good: pursues the same broad problem space.
- Good: could remain interesting as the ecosystem matures.
- Bad: the research record calls it experimental, which is too much risk for a load-bearing teaching path.
- Bad: the repository already has a verified Kargo path with clearer documented steps.

## Revisit when

- GitHub-hosted runners gain a secure and supported way to verify the local-cluster rollout state directly.
- Kargo changes its PR step behavior or removes the needed synchronous merge flow.
- Merge queue becomes a hard repository requirement that Kargo cannot satisfy.
- WP4.0 or WP4.5 shows unacceptable complexity or footprint compared with the measured alternatives.

## Further reading

- Decision-critical addendum: [../research/2026-10-04-decision-critical-addendum.md](../research/2026-10-04-decision-critical-addendum.md) (checked 2026-10-04)
- Kubernetes platform verification: [../research/2026-10-04-kubernetes-platform-verification.md](../research/2026-10-04-kubernetes-platform-verification.md) (checked 2026-10-04)
- ADR-0011 GitOps control-plane choice: [./0011-gitops-vs-traditional-cicd.md](./0011-gitops-vs-traditional-cicd.md) (checked 2026-10-04)
