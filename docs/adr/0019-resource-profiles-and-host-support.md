---
title: "ADR-0019: Define resource profiles and an explicit host support matrix"
description: "Define four resource profiles and a host support matrix so developers can choose a supported path without guessing hardware or platform expectations."
status: proposed
date: 2026-10-04
decision-makers: ["@RostomOhannessian"]
accepted-in: "WP3.0"
supersedes: []
superseded-by: []
tools: [docker-compose, kind, codespaces, dev-containers, registry-cache, aspire, sql-server, kubernetes]
related-adrs: ["0015"]
evidence:
  - docs/research/2026-10-04-kubernetes-platform-verification.md
  - docs/research/2026-10-04-dotnet-ecosystem-verification.md
  - docs/research/2026-10-04-github-supply-chain-verification.md
  - docs/research/spikes/1.a-pactnet-kestrel.md
  - docs/research/spikes/1.c-mtp-coverage-stryker.md
  - docs/research/spikes/1.d-testcontainers-mssql.md
---

# ADR-0019: Define resource profiles and an explicit host support matrix

## Context and problem statement

Plan findings F11, F12, and F17 show that "just run the whole stack locally" is not a real onboarding strategy for this repository. The platform spans SQL Server, Valkey, Keycloak, kind, Envoy Gateway, Vault, observability components, and later GitOps controllers. Different hosts can support different subsets of that stack, and the plan needs to say so directly.

Plan section 12 therefore defines four profiles: `api`, `lite`, `full`, and `ci`. Their budgets are indicative until WP3.0 measures them, but the profiles already structure the developer experience and the host support story. Plan section 4.3 adds the explicit support matrix for Linux x86-64, Windows x86-64, macOS Intel, Apple Silicon, Windows on Arm, and Codespaces.

The evidence explains why this is necessary. The Kubernetes verification record says SQL Server containers are x86-64 only and Microsoft does not support Rosetta 2, Prism, or QEMU for them (checked 2026-10-04). The same record says Codespaces on the Free plan includes 120 core-hours and 15 GB-month per month, about 30 hours on 4 cores or 15 on 8. The .NET ecosystem record (item 13) confirms the same quota, and the GitHub record (item 2) shows that public GitHub-hosted runners provide 4 vCPU and 16 GB RAM, which informs the `ci` profile budget (checked 2026-10-04). The .NET ecosystem verification record says Dev Container lockfiles are implemented, `devcontainers/ci` is active, and Aspire 13.6.0 is now just "Aspire" and remains a real local-orchestration alternative, though not the chosen one (checked 2026-10-04).

The decision also depends on [ADR-0015](./0015-gateway-api-with-envoy-gateway.md), because the pinned kind node image and high-port host exposure shape the `lite`, `full`, and `ci` profiles.

## Decision drivers

- Developers need a documented path that matches their hardware and operating system.
- The repository must stay teachable on laptops without promising unsupported combinations.
- CI and onboarding need a smaller platform subset than the full three-environment cluster.
- The stack should avoid repeated image pulls and long cluster rebuilds on local hosts.

## Considered options

1. Offer only one full local stack and require every contributor to meet it.
2. Support only the Compose path and drop the local Kubernetes platform.
3. Require cloud development environments for everyone.
4. Define multiple profiles and an explicit host support matrix.

## Decision outcome

Chosen option: **Define multiple profiles and an explicit host support matrix**, because it matches the verified host constraints while keeping the repository usable for learning and portfolio work.

### Profiles

|Profile|Runtime|Contents|Indicative budget|
|---|---|---|---|
|`api`|Docker Compose|SQL Server, Valkey, Keycloak, otel-lgtm, plus the API on host or in a container|about 4 to 5 GB RAM and 2 or more vCPU|
|`lite`|kind, single node|dev environment only, metrics-first observability, Kargo optional|about 10 GB RAM and 4 or more vCPU on a 16 GB host|
|`full`|kind, three nodes|dev, staging, and prod namespaces; Kargo; Tempo; Loki; Alertmanager|about 16 to 18 GB RAM and 8 vCPU on a 32 GB host|
|`ci`|kind on a GitHub-hosted runner|the subset needed by the test at hand, with Vault in dev mode and no persistence|at or below the public runner budget of 4 vCPU and 16 GB RAM|

### Consequences

- Good: contributors can choose a path that fits their host instead of discovering unsupported combinations through failed setup attempts.
- Bad: the project must document and maintain more than one onboarding path. Mitigation: keep `dev doctor`, profile checks, and the support matrix in sync, and publish WP3.0 measurements before treating the budgets as settled.
- Neutral: the profiles describe supported operating envelopes; they do not change the architecture goals of the repository.

### Confirmation

- **WP1.4** ships the first host support matrix, the Dev Container, `dev doctor`, and the Compose-based `api` path.
- **WP3.0** measures the actual CPU and RAM footprints for `lite`, `full`, and `ci`, verifies the pinned kind node image, and updates the published budgets.
- **WP3.10** publishes teardown, host-difference notes, and the measured profile guidance.
- Onboarding and devcontainer tests in plan section 10.1 keep the setup paths exercised from fresh environments.

Execution record (WP1.0, 2026-10-06): [spike 1.d](../research/spikes/1.d-testcontainers-mssql.md) tested the SQL Server container path
hands-on on one host only, Windows x86-64 with Docker Desktop, where it started in about 6 seconds and used about 0.7 to 1.0 GB at idle
by the container's own memory counter. Linux x86-64 and macOS Intel were not verified hands-on, and Linux evidence is deferred to the
first Linux CI run in WP1.1. For Apple Silicon and Windows on Arm, Microsoft's container documentation says SQL Server images are
supported only on Linux hosts with Intel and AMD x86-64 CPUs and that Rosetta 2, Prism, and QEMU are not tested or supported
(checked 2026-10-06), which matches the best-effort entries in plan section 4.3. Nothing in the spikes tested those hosts.
Two further host limits surfaced. PactNet 5.0.1 ships no `win-arm64` native library
([spike 1.a](../research/spikes/1.a-pactnet-kestrel.md)), and Stryker.NET could not start natively on a Windows host that enforces
Smart App Control because of unsigned DLLs, so Windows contributors run mutation testing through Docker or the Dev Container
([spike 1.c](../research/spikes/1.c-mtp-coverage-stryker.md)). WP1.4 should also size the `api` profile from the container's cgroup memory
rather than from SQL Server's own memory views, which overstate it inside a container. This ADR stays proposed and WP3.0 still accepts it.

## Pros and cons of the options

### Offer only one full local stack and require every contributor to meet it

- Good: simplest documentation and one operational path.
- Bad: excludes hosts that cannot run the full stack reliably, especially Apple Silicon and Windows on Arm for the SQL Server-dependent platform path.

### Support only the Compose path and drop the local Kubernetes platform

- Good: lowers local resource requirements.
- Bad: abandons the zero-trust platform, Gateway API, GitOps, and rollout goals that define Phases 3 and 4.

### Require cloud development environments for everyone

- Good: gives one x86-64 baseline and avoids local host drift.
- Bad: conflicts with the repository's portability goals, quota limits, and the plan's primary preference for native Docker or a local Dev Container.

### Define multiple profiles and an explicit host support matrix

- Good: makes support boundaries honest, preserves local learning paths, and lets CI and onboarding use smaller subsets of the platform.
- Bad: requires disciplined measurement and documentation so the profile guidance does not drift from reality.

## Revisit when

- WP3.0 measurements differ materially from the indicative budgets in plan section 12.
- Microsoft changes SQL Server container host support in a way that improves or reduces Apple Silicon or Windows on Arm support.
- Codespaces quota or machine availability changes enough to affect the documented fallback path.
- A later ADR adopts a different local-orchestration story that makes one or more profiles unnecessary.

## Further reading

- [Kubernetes platform verification record](../research/2026-10-04-kubernetes-platform-verification.md) (checked 2026-10-04)
- [GitHub-hosted runner specs and quotas](https://docs.github.com/en/actions/reference/runners/github-hosted-runners) (checked 2026-10-04)
- [GitHub Codespaces billing](https://docs.github.com/en/billing/concepts/product-billing/github-codespaces) (checked 2026-10-04)
- [.NET ecosystem verification record](../research/2026-10-04-dotnet-ecosystem-verification.md) (checked 2026-10-04)
- [Spike 1.d: Testcontainers SQL Server 2022 on supported hosts](../research/spikes/1.d-testcontainers-mssql.md) (checked 2026-10-06)
