---
title: "ADR-0038: Build the catalog cache as a FusionCache adapter in CrossCutting over a Valkey connection in Infrastructure"
description: "Decide where the cache adapter and the Valkey connection live, how an entry is named, serialized, and expired, what the cache does when Valkey is down, how a removal reports that it did not reach the other nodes, which metrics it reports, and what the access control list of the Valkey user must name."
status: accepted
date: 2026-11-03
decision-makers: ["@RostomOhannessian"]
supersedes: []
superseded-by: []
tools: [fusioncache, valkey, stackexchange-redis, microsoft-extensions-caching-stackexchangeredis, testcontainers-dotnet]
related-adrs: ["0016", "0012", "0032", "0034"]
evidence:
  - src/Catalog.CrossCutting/Caching
  - src/Catalog.Infrastructure/Caching
  - tests/Catalog.Application.Tests/CatalogCacheTests.cs
  - tests/Catalog.Application.Tests/CacheContractTests.cs
  - tests/Catalog.Infrastructure.Tests/CacheOverValkeyTests.cs
  - tests/Catalog.Infrastructure.Tests/ValkeyAclTests.cs
  - docs/testing/evidence/2026-11-03-cache-adapter-run.md
  - docs/reference/caching.md
  - docs/research/spikes/1.b-fusioncache-valkey.md
---

# ADR-0038: Build the catalog cache as a FusionCache adapter in CrossCutting over a Valkey connection in Infrastructure

## Context and problem statement

[ADR-0016](0016-cache-implementation-fusioncache.md) chose FusionCache with a Valkey L2 and backplane behind `ICatalogCache`, and plan section 8.5 describes the stack, the keys, the write path, and the failure modes. [Spike 1.b](../research/spikes/1.b-fusioncache-valkey.md)
proved that the combination works against Valkey 8.1.10 and 9.1.2, and it left four things for WP1.9: set the circuit breakers, correct the ACL patterns of the plan, try to disable the commands that only the client handshake needs, and bound the first call after an outage begins. WP1.7 left the port and the decorator, and WP1.8 left the outbox that WP1.9 uses for the removal of entries (the second pull request of WP1.9 decides that part in a later ADR).

This ADR decides the adapter and the connection. It serves REQ-CACHE-001, REQ-CACHE-002, REQ-CACHE-003, REQ-ARC-003, and the trust boundary of Valkey in the threat model.

Facts that shape the decision (checked 2026-11-03):

- FusionCache 2.9.0 is the newest release, MIT licensed, and so are its serializer and its Redis backplane. `StackExchange.Redis` 2.10.1 and `Microsoft.Extensions.Caching.StackExchangeRedis` 10.0.14 are MIT too. A 3.x line of `StackExchange.Redis` exists, and the backplane package asks for 2.10.1.
- FusionCache computes the expiration of a memory entry and the timestamp of a tag removal from the wall clock, so no fake clock reaches them.
- `RemoveByTagAsync` is logical: it writes a marker for the tag to the shared cache and tells the other nodes, and an entry that is older than the marker is dropped when it is read. The entry itself stays in Valkey until its time to live ends.
- With a circuit breaker open, a call to the shared cache is skipped without an exception, and the first failure opens the breaker.
- `ConfigurationOptions.ToString()` of StackExchange.Redis 2.10.1 includes the password. Only `ToString(includePassword: false)` omits it.

## Decision drivers

- Plan section 8.1 puts the FusionCache adapter in CrossCutting and the Valkey connectivity in Infrastructure, and an architecture rule must be able to fail if that changes.
- A read never fails because of the cache, and a removal that did not reach the other nodes must say so, because the outbox is the guarantee (ADR-0012).
- The cache has no secret and no personal data in a key, a value, a log line, or a metric tag.
- The access control list of the application user names only what a test showed that the cache needs.
- Determinism: no test sleeps to make a time to live pass, and none depends on the order of the others.

## Considered options

1. The adapter and its settings in CrossCutting, the connection in Infrastructure, source-generated JSON, and circuit breaker events that tell a removal whether it is complete (chosen).
2. The whole cache, including the Valkey connection, in one project of CrossCutting.
3. The FusionCache `AsHybridCache()` adapter behind `ICatalogCache`.
4. Reflection-based JSON for the shared cache.
5. A removal that never reports failure, with the outbox as the only place that retries.

## Decision outcome

Chosen option: **the adapter in CrossCutting over a connection in Infrastructure, with source-generated JSON and a removal that throws when it is incomplete**, because it keeps the layers of plan section 8.1, lets a test prove the rules, and gives the outbox the signal it needs to try again.

### Layers

| Piece | Project | Why |
| --- | --- | --- |
| `FusionCatalogCache`, `CatalogCacheFactory`, `CatalogCacheOptions`, `CacheMetrics`, `CacheContract`, and the JSON context | `Catalog.CrossCutting`, namespace `Caching` | Plan section 8.1: CrossCutting holds the FusionCache adapter over the abstract shared cache and backplane |
| `ValkeyOptions` and `ValkeyConnection`, which build the `IDistributedCache` and the backplane | `Catalog.Infrastructure`, namespace `Caching` | Plan section 8.1: Infrastructure implements Valkey connectivity |
| The registration of both, and the cache of a node | `Catalog.Api` (WP1.10) | The composition root owns every registration. The Infrastructure and the CrossCutting expose factories, because an architecture rule keeps `IServiceProvider` out of them |

Three architecture rules hold the split, each with a violating fixture that proves it can fail: CrossCutting does not use the Valkey client types, Infrastructure uses no FusionCache type beyond the backplane namespace, and only the `Caching` namespace of CrossCutting names FusionCache. The Application and the Domain were already barred from all of them by the package rule and by the namespace rule of their types.

### Names, tags, and the contract version

- Every key is `catalog:{environment}:` plus `v{n}:` plus the name that the application gave the entry, as in `catalog:dev:v1:product:{id}`. The prefix is the FusionCache `CacheKeyPrefix`, and the version is `CacheContract.Version`. The environment is validated to lower-case letters, digits, and hyphens, so a key and an access control rule can carry it.
- FusionCache adds its own wire-format marker `v2:` to the key in the shared cache (`DistributedCacheKeyModifierMode`, kept at its default so a change of the wire format of a later FusionCache version cannot read an entry of an older one), so Valkey holds `v2:catalog:dev:v1:product:{id}`. A tag marker is `v2:catalog:dev:__fc:t:{tag}`.
- The tags are the ones that WP1.7 gave the queries: `product:{id}`, `products:list`, and `category:{id}`. A tag carries no version, so a removal reaches the entries of two versions of the application that run side by side.
- The version is raised when the shape of a cached type changes. Snapshot tests hold the JSON of `ProductView` and of `Page<ProductView>` as the shared cache stores it, and a test fails when a query returns a type that has no sample and no entry in the JSON context.

### Entries

- **Times to live.** An entry lives 30 seconds in memory (the bound of the staleness while the backplane is down, plan section 8.5) and 5 minutes in the shared cache. A list lives 10 seconds in both, because a list changes with every write. The memory holds at most 10,000 entries, each counted as one. The tag markers live for twice the longest time to live of an entry, so a marker outlives everything it can remove. The validator holds these relations.
- **Only a success is kept.** A refusal such as `not-found` is returned to the caller and written to neither level: the loader sets a duration of zero and skips both writes. The failure is the one of the caller's own load, so a concurrent caller never receives the refusal of another.
- **One load for one key on one node.** FusionCache's single flight holds the load. Nodes do not coordinate, so a stampede across nodes is bounded and documented, and no distributed lock is used (plan section 8.5).
- **Faults.** A fault of the loader reaches the caller unchanged and is never counted as a fault of the cache. A fault of the cache, such as a broken memory cache, is absorbed: the loader runs without the cache, the metric `catalog.cache.faults` counts it, and a warning is logged without a value of an entry. A cancellation is never absorbed.
- **No fail-safe and no HybridCache adapter.** FusionCache's fail-safe would serve an expired entry when the database fails, which widens the staleness that the consistency model states. The decision is recorded, and a later ADR can change it.

### Serialization

The shared cache holds System.Text.Json produced by a source-generated context (`CacheJsonContext`). It lists the two cached types and FusionCache's own wrappers, which are `FusionCacheDistributedEntry<T>` for each type and for the `long` of a tag marker. The names are camel case, and an enum is its name. A type that the context does not list cannot be written, and a test finds such a type before a deployment does.

### What the cache does when Valkey is down

- The settings validate that the timeout of the shared cache and both circuit breaker durations are positive. FusionCache leaves a breaker off at zero, and then every call waits for the timeouts (spike 1.b). The defaults are a timeout of 500 milliseconds and breakers of 5 seconds.
- The first failure opens the breaker, and a call to the shared cache is skipped while it is open. A read then runs on memory and the loader. FusionCache's auto-recovery sends the notifications that were missed once the backplane is back.
- `CacheMetrics` keeps the state of both breakers from the events that FusionCache raises, and the adapter reads it. FusionCache runs an event handler in the background unless `EnableSyncEventHandlersExecution` is set, and a removal reads the state right after its call, so the option is on. The handlers only flip a flag and count.
- **A removal says whether it is complete.** `RemoveByTagAsync` removes the entries of the node, writes the marker to the shared cache, and publishes to the backplane, awaiting both (`AllowBackgroundDistributedCacheOperations` and `AllowBackgroundBackplaneOperations` are off for the marker). When either breaker is open after the call, or the call threw, the adapter throws `CacheInvalidationIncompleteException`. This changes the documentation of the port, not its members. A caller that must guarantee the removal lets the exception propagate, and a caller that cannot wait catches it. The outbox handler and the best-effort dispatch of the second pull request use both.
- The first call after an outage can wait for the timeout. The likely cause is a read of a tag marker that the memory of the node does not hold yet, because FusionCache reads a marker from the shared cache and keeps it in memory afterward (an inference from the source of FusionCache 2.9.0, checked 2026-11-03, that spike 1.b did not isolate). The timeout of the shared cache bounds it, and the second pull request of WP1.9 asserts the bound.

### Metrics

The meter is `Catalog.Cache`, which follows the names of `Catalog.Outbox`. A tag of a measurement is a fixed word, never a key or an identifier.

| Instrument | Kind | Tags | Meaning |
| --- | --- | --- | --- |
| `catalog.cache.requests` | counter | `result` (`hit` or `miss`), `degraded` (`true` or `false`) | Reads. The hit ratio of the plan is the hits divided by the hits and the misses. A hit that a degraded node serves can be stale for the length of the memory time to live |
| `catalog.cache.invalidations` | counter | `outcome` (`complete` or `incomplete`) | Removals by tag |
| `catalog.cache.faults` | counter | none | Faults of the cache that a read absorbed |
| `catalog.cache.serialization_errors` | counter | none | Entries that could not be written or read |
| `catalog.cache.circuit_changes` | counter | `level` (`shared` or `backplane`), `state` (`open` or `closed`) | Changes of a breaker |
| `catalog.cache.shared.down` | gauge | none | 1 while the breaker of the shared cache is open |
| `catalog.cache.backplane.down` | gauge | none | 1 while the breaker of the backplane is open, which widens the staleness to the memory time to live |

The alerts that watch the gauges are WP1.11, and the canary analysis of Phase 4 reads the hit ratio.

### The connection and the access control list

- `ValkeyConnection` opens the shared cache and the backplane with the settings in `ValkeyOptions`. The connection never aborts when the server is down, so the host starts without Valkey (plan section 8.9). The settings print nothing but their name, and a problem names a setting and never repeats a value. No code prints a `ConfigurationOptions`, because its `ToString()` includes the password, and a test shows that a failed connection writes no password to a log or an exception.
- The handshake of StackExchange.Redis reads the cluster topology, reads the settings of the server, reads a key to choose between two masters, and subscribes to a channel for a change of master. The connection switches all four off (`CommandMap` for `CLUSTER` and `CONFIG`, and an empty `TieBreaker` and `ConfigurationChannel`), so the access control list names none of them.
- `ValkeyAclTests` reads the rules of the application user from `compose.yaml`, creates the user on a real Valkey, and runs the whole scenario, which is a write, a read through the shared cache from a second node, an overwrite, two removals by tag, and the backplane message between two nodes. The log of denied commands has no entry. A second test shows that the user reaches nothing outside its keys and its channel, and that it cannot list keys, flush, change the list, change a setting, or stop the server.

### Where the plan and Valkey differ (section 8.5)

| Plan section 8.5 says | What a real Valkey needs and the tests verified (checked 2026-11-03) |
| --- | --- |
| The ACL lets each environment user access `~catalog:{env}:*` | `~v2:catalog:{env}:*`, because FusionCache writes `v2:` before the prefix |
| and `&catalog:{env}:*` | `&catalog:{env}.Backplane:v2`, the one channel of the backplane. No other channel is needed |
| Product keys are `catalog:{env}:v{n}:product:{id}` | The logical key is that, and the key in Valkey has the `v2:` marker in front |
| The writing node's L2 removal is awaited | A tag removal writes a marker and does not delete an entry. An entry is dropped when a node reads it and finds it older than the marker |

The plan is not amended here. If the maintainer corrects it, the correction is its own `docs(plan):` pull request. The stack in `compose.yaml` now allows the two verified patterns and no other.

### Consequences

- Good: the layers hold and a rule fails when they do not, a read never fails because of the cache, and a removal tells the outbox when it must try again.
- Good: the access control list of the user is the smallest that a test could show, with no wildcard command, key, or channel.
- Good: nothing in a key, a value, a log line, or a metric tag carries a secret or personal data, and a test checks each.
- Bad: `EnableSyncEventHandlersExecution` is a setting that FusionCache's documentation advises against in general. Mitigation: the handlers only flip a flag and count, and a test shows the state after each call.
- Bad: the shared cache holds the view types of the Application, so a change to a view changes the contract. Mitigation: the version in the key and the snapshot tests, which fail until someone raises the version.
- Bad: the credential of the Valkey user is static in this work package. Mitigation: the broker that rotates it arrives with the platform (Phase 3), and the settings and the connection are the one place that changes.
- Neutral: FusionCache stays on 2.9.0 and StackExchange.Redis on 2.10.1 until Dependabot proposes an update that its backplane package supports.

### Confirmation

- `CatalogCacheTests` and `CacheContractTests` (no Docker) prove the reads, the refusals, the single flight, the faults, the expiration that is set, the metrics, and the shape of the shared entries.
- `CacheOverValkeyTests` and `ValkeyAclTests` run the adapter against the Valkey image that `compose.yaml` pins, on Windows and in the Linux SDK image ([the evidence record](../testing/evidence/2026-11-03-cache-adapter-run.md)).
- The architecture rules and their violating fixtures hold the layers.
- The second pull request of WP1.9 adds the two-host invalidation, the Valkey outage, and the convergence through the outbox (a later ADR), and the close-out accepts ADR-0016 on the evidence of both.

## Pros and cons of the options

### The adapter in CrossCutting over a connection in Infrastructure, with source-generated JSON and a removal that throws when it is incomplete

- Good: follows plan section 8.1 and the rules that enforce it.
- Good: the outbox gets the signal that it needs without a fourth operation on the port.
- Bad: two projects share the settings of one environment, which the composition root must pass to both.

### The whole cache in one project of CrossCutting

- Good: fewer moving parts.
- Bad: CrossCutting would name the Valkey client, which plan section 8.1 gives to Infrastructure, and the rule that keeps the connection in one place could not hold.

### The FusionCache `AsHybridCache()` adapter behind `ICatalogCache`

- Good: the Microsoft abstraction.
- Bad: ADR-0016 records that HybridCache has no cross-node invalidation by itself, and its API has no way to report that a removal did not reach the other nodes.

### Reflection-based JSON for the shared cache

- Good: nothing to list.
- Bad: a type that no one thought of is written, and a change of a view changes a stored entry without a test that sees it.

### A removal that never reports failure

- Good: the simplest contract.
- Bad: the outbox would mark the message done when Valkey was down, and the other nodes would keep the old entry until its time to live ended, which is the failure that ADR-0012 asks WP1.9 to close.

## Revisit when

- FusionCache or StackExchange.Redis change what the handshake sends, or a new feature needs a command that is not in the list.
- The maintainer decides on the correction of plan section 8.5.
- A measured hit ratio or stampede shows that the times to live or the single flight of a node are not enough, which calls for eager refresh or a distributed lock.
- Valkey credentials rotate (Phase 3), which changes `ValkeyOptions` and the connection.
- The tests show that `EnableSyncEventHandlersExecution` costs more than it gives.

## Further reading

- FusionCache documentation: [https://github.com/ZiggyCreatures/FusionCache/blob/main/docs/CacheLevels.md](https://github.com/ZiggyCreatures/FusionCache/blob/main/docs/CacheLevels.md) (checked 2026-11-03)
- FusionCache tagging: [https://github.com/ZiggyCreatures/FusionCache/blob/main/docs/Tagging.md](https://github.com/ZiggyCreatures/FusionCache/blob/main/docs/Tagging.md) (checked 2026-11-03)
- Spike 1.b: [../research/spikes/1.b-fusioncache-valkey.md](../research/spikes/1.b-fusioncache-valkey.md) (checked 2026-10-06)
- StackExchange.Redis configuration: [https://stackexchange.github.io/StackExchange.Redis/Configuration](https://stackexchange.github.io/StackExchange.Redis/Configuration) (checked 2026-11-03)
- Valkey ACL: [https://valkey.io/topics/acl/](https://valkey.io/topics/acl/) (checked 2026-10-10)

## Update of 2026-11-03 (WP1.9, second pull request)

The text above is unchanged. The second pull request measured two things that this ADR left as inferences, and decided the removal path in [ADR-0039](0039-cache-invalidation-by-immediate-dispatch-and-the-outbox.md).

- **The first call after an outage is bounded.** `CacheOutageTests` stop the server and time the first read of a new key. It costs at most the timeout of the shared cache plus a margin for a slow machine, and the breaker answers every later call at once, so no read waits for the server on each call. The cause that this ADR guessed (a read of a tag marker that the memory of the node does not hold) stays an inference, and the bound is what the test proves.
- **The backplane gauge opens when a publish fails, not when the shared cache fails.** With the server stopped, the breaker of the shared cache opens and the breaker of the backplane stays closed, so `catalog.cache.backplane.down` reads 0 (measured, checked 2026-11-03). A server that refuses the channel while it accepts the keys makes the publish fail, the backplane breaker opens, the gauge reads 1, and `RemoveByTagAsync` throws `CacheInvalidationIncompleteException`. A removal is incomplete in both cases, and the alert of WP1.11 watches both gauges.
