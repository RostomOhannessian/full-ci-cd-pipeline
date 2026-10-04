---
title: "ADR-0016: Implement caching with FusionCache behind ICatalogCache"
description: "Use FusionCache with a Valkey-backed L2 and backplane because bare HybridCache leaves a cross-node correctness gap in this topology."
status: proposed
date: 2026-10-04
decision-makers: ["@RostomOhannessian"]
accepted-in: "WP1.9"
supersedes: []
superseded-by: []
tools: [fusioncache, hybridcache, valkey, testcontainers-dotnet, opentelemetry-dotnet]
related-adrs: ["0012"]
evidence:
  - docs/research/2026-10-04-dotnet-ecosystem-verification.md
---

# ADR-0016: Implement caching with FusionCache behind ICatalogCache

## Context and problem statement

Plan section 8.5 and [the Phase 1 plan](../plans/phases/phase-1-api-core.md) make caching a first-class part of the API design. The repository needs an in-memory L1 for read performance, a distributed L2 for shared state, and cross-node invalidation so canary or multi-replica reads do not silently serve stale data after writes.

The .NET ecosystem verification record says Microsoft's HybridCache is stable and attractive in isolation, but it still has a load-bearing gap for this topology: there is no built-in cross-node L1 invalidation in .NET 10 (checked 2026-10-04). The record verifies that `dotnet/extensions#5517` is still open and unmilestoned, and that the follow-up `dotnet/runtime#125602` targets .NET 11. It also records that `RemoveByTagAsync` is logical only, not a physical purge, and that single-flight stampede protection is per process.

That matters here because plan section 8.5 promises bounded staleness across replicas and uses canary delivery later in Phase 4. With bare HybridCache, node A can invalidate its own L1 and L2 while nodes B and C keep serving stale in-memory entries until their local TTL expires. That is a correctness bug, not just a performance tradeoff.

The same research record says FusionCache 2.9.0 provides a mature StackExchange.Redis backplane, an official `.AsHybridCache()` adapter, and OpenTelemetry support (checked 2026-10-04). Valkey compatibility is an inference from the Redis protocol rather than a first-party FusionCache certification, so the plan requires a proof in WP1.0 and WP1.9.

## Decision drivers

- Cross-node invalidation must be explicit and testable across multiple API replicas.
- The application layer must depend on an internal port, `ICatalogCache`, not on concrete cache infrastructure.
- Degradation when Valkey is down must preserve availability with bounded staleness, as defined in plan section 8.5.
- The cache stack must support observability and teachable tests, not just a happy-path benchmark.

## Considered options

1. Build a custom cache layer directly over `IMemoryCache` and `IDistributedCache`.
2. Use Microsoft HybridCache alone.
3. Use HybridCache plus a custom pub/sub invalidation layer.
4. Use FusionCache with a Valkey-backed L2 and backplane behind `ICatalogCache`.
5. Use only a distributed L2 cache, with no L1.

## Decision outcome

Chosen option: **Use FusionCache with a Valkey-backed L2 and backplane behind `ICatalogCache`**, because it closes the verified multi-replica correctness gap without forcing custom invalidation infrastructure into the application layer.

### Consequences

- Good: the repository gets explicit cross-node invalidation, L1 plus L2 performance, and cache telemetry that fits the Phase 1 and Phase 4 observability plan.
- Bad: Valkey compatibility with the FusionCache Redis backplane is still an inference. Mitigation: WP1.0 and WP1.9 must prove it with two hosts against shared Valkey Testcontainers before the decision is accepted.
- Neutral: the application still owns its consistency contract. FusionCache helps implement that contract, but plan section 8.5 remains the source of truth for staleness limits and failure behavior.

### Confirmation

- **WP1.0** spikes FusionCache's backplane against Valkey 8.1 and 9.1, and records the result.
- **WP1.9** adds the two-host invalidation test, the Valkey-down degradation test, the key and tag strategy, and the cache metrics.
- **WP4.4** checks cache hit ratio and staleness metrics during canary analysis, which validates the production-shaped behavior described in plan section 8.5.
- The traceability mechanism in plan section 10.2 ties these tests back to the cache requirements.

## Pros and cons of the options

### Build a custom cache layer directly over `IMemoryCache` and `IDistributedCache`

- Good: keeps dependencies minimal and gives full control over behavior.
- Bad: recreates backplane, invalidation, telemetry, and failure-mode logic that a mature library already provides.

### Use Microsoft HybridCache alone

- Good: simple integration and a Microsoft-supported abstraction with familiar APIs.
- Bad: leaves the verified cross-node L1 invalidation gap from `dotnet/extensions#5517` and `dotnet/runtime#125602`, which is unacceptable for this topology.

### Use HybridCache plus a custom pub/sub invalidation layer

- Good: preserves the Microsoft abstraction while closing the biggest gap.
- Bad: the repository would still own the hardest part of the cache implementation and would need to teach bespoke invalidation code instead of a documented library capability.

### Use FusionCache with a Valkey-backed L2 and backplane behind `ICatalogCache`

- Good: provides the needed backplane now, exposes an adapter path through `.AsHybridCache()`, and includes OpenTelemetry support (checked 2026-10-04).
- Bad: it adds a third-party dependency and a Valkey compatibility assumption that the repository must prove explicitly.

### Use only a distributed L2 cache, with no L1

- Good: avoids cross-node invalidation because every read goes through the shared cache.
- Bad: gives up the local read-path performance the plan wants and does not teach the L1 and L2 consistency tradeoff that this project is designed to show.

## Revisit when

- `dotnet/extensions#5517` is closed with shipped cross-node invalidation support on the repository's target .NET line.
- `dotnet/runtime#125602` ships in a stable release and closes the same gap without custom add-ons.
- WP1.0 or WP1.9 shows that FusionCache's Redis backplane does not behave correctly with Valkey.
- FusionCache changes its licensing or maintenance posture materially.

## Further reading

- [.NET ecosystem verification record](../research/2026-10-04-dotnet-ecosystem-verification.md) (checked 2026-10-04)
- [`dotnet/extensions#5517`, "Cache Synchronization for Hybrid Caching in Multi-Node Environments"](https://github.com/dotnet/extensions/issues/5517) (checked 2026-10-04)
- [`dotnet/runtime#125602`](https://github.com/dotnet/runtime/issues/125602) (checked 2026-10-04)
- [FusionCache documentation, "MicrosoftHybridCache"](https://raw.githubusercontent.com/ZiggyCreatures/FusionCache/main/docs/MicrosoftHybridCache.md) (checked 2026-10-04)
