---
title: "ADR-0015: Use Gateway API with Envoy Gateway as the only entry point"
description: "Use Gateway API with Envoy Gateway for north-south traffic because ingress-nginx is retired and this stack aligns with the rollout and portability goals."
status: proposed
date: 2026-10-04
decision-makers: ["@RostomOhannessian"]
accepted-in: "WP3.2"
supersedes: []
superseded-by: []
tools: [gateway-api, envoy-gateway, kind, kubernetes, ingress-nginx, nginx-gateway-fabric, cilium, argo-rollouts-gatewayapi-plugin]
related-adrs: ["0011", "0019"]
evidence:
  - docs/research/2026-10-04-kubernetes-platform-verification.md
---

# ADR-0015: Use Gateway API with Envoy Gateway as the only entry point

## Context and problem statement

The platform topology in plan sections 4.4 and 8.12 and in [the Phase 3 plan](../plans/phases/phase-3-zero-trust-platform.md) requires one north-south entry point that works on kind, supports re-encrypted upstream traffic, and stays compatible with later progressive delivery in Phase 4. The original ingress-nginx direction no longer meets that bar.

The Kubernetes verification record says `kubernetes/ingress-nginx` is retired and archived read-only, with best-effort maintenance ended in March 2026 (checked 2026-10-04). The same record says Gateway API v1.5.x is GA, Envoy Gateway v1.9.2 supports Kubernetes 1.33 to 1.36, and the Argo Rollouts Gateway API plugin lists Envoy Gateway as a tested provider (checked 2026-10-04).

This repository also has portability constraints. Plan section 12 requires a local stack that intermediate developers can run without administrator rights. The Gateway layer therefore has to work on high host ports, through kind's `extraPortMappings`, on Windows, Linux, and macOS. Plan finding F12 adds another constraint: kind v0.33.0 defaults to a Kubernetes 1.37 node image, which is newer than Envoy Gateway 1.9's supported range, so the node image must be pinned.

The decision serves WP3.0, WP3.2, and WP4.0, plus the GitOps and rollout design in [ADR-0011](./0011-gitops-vs-traditional-cicd.md).

## Decision drivers

- The entry point must have an active upstream and a current Kubernetes compatibility story.
- The API layer must support Gateway API objects that Argo Rollouts can use later for weighted traffic shifts.
- Local exposure must work on developer machines without privileged ports.
- The solution must support re-encrypted traffic from the gateway to the API and leave room for rate limiting.

## Considered options

1. Keep ingress-nginx.
2. Adopt NGINX Gateway Fabric.
3. Adopt Cilium's Gateway implementation.
4. Adopt Gateway API with Envoy Gateway.

## Decision outcome

Chosen option: **Adopt Gateway API with Envoy Gateway**, because it is the best fit for the repository's current platform constraints and its later rollout model.

### Consequences

- Good: the repository uses the Kubernetes direction now recommended by upstream and can reuse the same Gateway API surface for Phase 4 traffic shaping.
- Bad: kind's default Kubernetes minor is too new for Envoy Gateway 1.9, so the cluster definition must pin a supported node image. Mitigation: WP3.0 publishes the compatibility matrix and WP3.2 enforces the pin in the kind stack.
- Neutral: the project still needs cert-manager and trust-manager for the TLS story; changing the gateway does not remove that platform work.

### Confirmation

- **WP3.0** proves Envoy Gateway on kind through `extraPortMappings` and records the node-image pin and host-specific findings.
- **WP3.2** creates the GatewayClass, Gateway, HTTPRoutes, BackendTLSPolicy, and optional rate-limit path, then proves allowed and denied flows with the Chainsaw probe matrix.
- **WP4.0** verifies the Argo Rollouts Gateway API plugin against the pinned Kubernetes minor and Envoy Gateway version.
- Plan section 10.1 keeps platform end-to-end, policy, and performance smoke suites relevant to this decision blocking when the touched paths require them.

## Pros and cons of the options

### Keep ingress-nginx

- Good: the controller is familiar and many tutorials exist.
- Bad: upstream has retired it, which makes it the wrong teaching choice for a new repository in 2026.

### Adopt NGINX Gateway Fabric

- Good: it speaks Gateway API and keeps the NGINX operational model many users know.
- Bad: the repository has stronger evidence for Envoy Gateway in this plan, especially around BackendTLSPolicy support and the tested Rollouts provider story.

### Adopt Cilium's Gateway implementation

- Good: it can pair L7 routing with a powerful CNI and policy model.
- Bad: it adds footprint and operational complexity that the plan explicitly avoids in favor of kindnet and a lighter local stack.

### Adopt Gateway API with Envoy Gateway

- Good: it aligns with Gateway API, supports Kubernetes 1.33 to 1.36, provides BackendTLSPolicy, and fits the rollout path the plan already selected (checked 2026-10-04).
- Bad: the repository must manage version pinning more carefully than a default kind install would suggest.

## Revisit when

- Envoy Gateway changes its supported Kubernetes window in a way that makes the current node-image pin obsolete or unsupported.
- WP3.0 or WP4.0 shows that `extraPortMappings` or the Rollouts plugin is unreliable on supported developer hosts.
- Another Gateway API implementation gains a materially better fit for this repository's resource profile without adding CNI complexity.
- Gateway API support for the specific traffic-management features this plan needs regresses or changes incompatibly.

## Further reading

- [Kubernetes platform verification record](../research/2026-10-04-kubernetes-platform-verification.md) (checked 2026-10-04)
- [Kubernetes blog, "ingress-nginx retirement"](https://kubernetes.io/blog/2025/11/11/ingress-nginx-retirement/) (checked 2026-10-04)
- [Envoy Gateway release matrix](https://gateway.envoyproxy.io/news/releases/matrix/) (checked 2026-10-04)
- [Argo Rollouts Gateway API provider status](https://rollouts-plugin-trafficrouter-gatewayapi.readthedocs.io/en/latest/provider-status/) (checked 2026-10-04)
