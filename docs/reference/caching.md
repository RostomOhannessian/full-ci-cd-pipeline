---
title: "Caching"
description: "How the catalog cache is built: the layers, the settings, the keys and tags, the times to live, what is kept and what is not, the circuit breakers, the metrics, the connection to Valkey, and how the code is tested."
audience: [learners, contributors, maintainers]
type: reference
tools: [fusioncache, valkey, stackexchange-redis, microsoft-extensions-caching-stackexchangeredis, testcontainers-dotnet]
last-verified: 2026-11-03
owner: "@RostomOhannessian"
---

# Caching

The cache of the catalog keeps the results of the queries `GetProduct` and `ListProducts` on two levels, in the memory of each API node (L1) and in Valkey (L2), and a backplane tells the other nodes to drop an entry. The Application layer knows only the port `ICatalogCache`. The adapter is in [`src/Catalog.CrossCutting/Caching`](../../src/Catalog.CrossCutting/Caching/FusionCatalogCache.cs), and the connection to Valkey is in [`src/Catalog.Infrastructure/Caching`](../../src/Catalog.Infrastructure/Caching/ValkeyConnection.cs)
([ADR-0038](../adr/0038-catalog-cache-adapter-and-valkey-connection.md), [ADR-0016](../adr/0016-cache-implementation-fusioncache.md)). This page describes what exists today. The Api composes the pieces in WP1.10, and the section "The composition" lists what it registers.

## The layers

| Piece | Project | Role |
| --- | --- | --- |
| `ICatalogCache`, `ICachedQuery`, `CacheKey`, `CacheInvalidationIncompleteException` | `Catalog.Application` | The port and the key of a query. Nothing here names a library |
| `FusionCatalogCache`, `CatalogCacheFactory`, `CatalogCacheOptions`, `CacheMetrics`, `CacheContract`, `CacheJsonContext` | `Catalog.CrossCutting`, namespace `Caching` | The adapter over FusionCache, its settings, its metrics, and the contract of the shared entries |
| `ValkeyOptions`, `ValkeyConnection` | `Catalog.Infrastructure`, namespace `Caching` | The shared cache (`IDistributedCache`) and the backplane over StackExchange.Redis |
| `QueryCacheDecorator` | `Catalog.CrossCutting`, namespace `Decorators` | Serves a marked query from the cache. It was delivered in WP1.7 |

Three architecture rules hold the split, each with a violating fixture: CrossCutting does not use the Valkey client types, Infrastructure uses no FusionCache type beyond the backplane namespace, and only the `Caching` namespace of CrossCutting names FusionCache.

## The settings

`CatalogCacheOptions` (section `Catalog:Cache`) has these settings. The validator runs when the host starts, names a setting in each problem, and never repeats a value.

| Setting | Default | Meaning |
| --- | --- | --- |
| `Environment` | `dev` | Part of every key and of the channel. One to 32 lower-case letters, digits, or hyphens, starting with a letter |
| `MemoryDuration` | 30 seconds | How long an entry stays in the memory of a node. This is the bound of the staleness while the backplane is down (plan section 8.5) |
| `SharedDuration` | 5 minutes | How long an entry stays in Valkey. It must not be shorter than `MemoryDuration` |
| `ListDuration` | 10 seconds | How long a list stays in both levels. It must not be longer than `MemoryDuration` |
| `MemoryMaxEntries` | 10,000 | The size limit of the memory. Every entry counts as one |
| `SharedTimeout` | 500 milliseconds | The longest time of one call to Valkey. A call that takes longer counts as a failure |
| `SharedCircuitBreakerDuration` | 5 seconds | How long the breaker of the shared cache stays open after a failure. It must be positive, because zero turns it off |
| `BackplaneCircuitBreakerDuration` | 5 seconds | The same for the backplane |

`ValkeyOptions` (section `Catalog:Valkey`) has the `Endpoint` as `host:port`, the `UserName` and the `Password` of an access control list user, `UseTls`, `ConnectTimeout`, `CommandTimeout`, and `ClientName`. Both classes print nothing but their name.

## Keys and tags

| Part | Example | Where it comes from |
| --- | --- | --- |
| Prefix of the environment | `catalog:dev:` | `CatalogCacheOptions.KeyPrefix`, which FusionCache adds to every key |
| Version of the contract | `v1:` | `CacheContract.Version` |
| Name of the entry | `product:{id}` or `products:list:{hash}` | The query (`ICachedQuery.CacheKey`) |
| Wire-format marker | `v2:` | FusionCache, in front of the key in Valkey |

An entry in Valkey is a hash under `v2:catalog:dev:v1:product:{id}`, and a tag marker is a key under `v2:catalog:dev:__fc:t:{tag}`. The tags that the queries give are `product:{id}` for one product, `products:list` for every list, and `category:{id}` for a list of one category.
A tag carries no version, so a removal reaches the entries of two versions of the application that run side by side. The hash of a list is the SHA-256 of the filter and the cursor, so a key holds no text that a caller chose.

## What is kept

- **A success is kept.** The next read on any node is served from memory or from Valkey, and the loader does not run.
- **A refusal is not kept.** A `not-found` or any other failed result is returned to the caller and written to neither level, so it never outlives the change that fixes it.
- **One load for one key on one node.** Concurrent reads share one run of the loader. Nodes do not coordinate, and no distributed lock is used.
- **A fault of the loader** reaches the caller unchanged and is never counted as a fault of the cache. **A fault of the cache** is absorbed: the loader runs without the cache, `catalog.cache.faults` counts it, and a warning is logged.
- **A product** lives 30 seconds in memory and 5 minutes in Valkey, and **a list** lives 10 seconds in both. A tag marker lives for twice the longest time to live of an entry.

## The contract of the shared entries

The shared cache holds System.Text.Json from a source-generated context, `CacheJsonContext`. It lists `ProductView`, `Page<ProductView>`, and the wrappers that FusionCache adds (`FusionCacheDistributedEntry<T>` for each of the two types and for the `long` of a tag marker). The names are camel case, and an enum is its name.
`CacheContractTests` holds the JSON of each type as a snapshot and fails when a query returns a type that the contract does not list. Raise `CacheContract.Version` when a shape changes, so the old entries are never read.

## When Valkey is down

The first failure opens the circuit breaker, and a call to the shared cache is skipped for the duration of the breaker. A read then runs on memory and the loader, and no read throws. After the duration, the next call tries again, and a success closes the breaker. `CacheMetrics` keeps the state of both breakers from the events of FusionCache, and the adapter reads it after a removal.

`RemoveByTagAsync` removes the entries of the node, writes the marker to Valkey, and publishes on the backplane, awaiting both. It throws `CacheInvalidationIncompleteException` when the call failed or when either breaker is open afterward, because the other nodes may then serve the old entry until their memory time to live ends. The outbox handler and the best-effort dispatch of the second pull request of WP1.9 use both outcomes.

## The metrics

The meter is `Catalog.Cache`. A tag of a measurement holds a fixed word and never a key or an identifier.

| Instrument | Kind | Tags | Meaning |
| --- | --- | --- | --- |
| `catalog.cache.requests` | counter | `result`, `degraded` | Reads, by hit or miss, and by whether the node was degraded. The hit ratio is the hits divided by the hits and the misses |
| `catalog.cache.invalidations` | counter | `outcome` | Removals by tag, `complete` or `incomplete` |
| `catalog.cache.faults` | counter | none | Faults of the cache that a read absorbed |
| `catalog.cache.serialization_errors` | counter | none | Entries that could not be written or read |
| `catalog.cache.circuit_changes` | counter | `level`, `state` | Changes of a breaker |
| `catalog.cache.shared.down` | gauge | none | 1 while the breaker of the shared cache is open |
| `catalog.cache.backplane.down` | gauge | none | 1 while the breaker of the backplane is open |

The alerts that watch the gauges are planned in WP1.11, and the canary analysis of Phase 4 reads the hit ratio.

## The connection to Valkey

`ValkeyConnection.CreateSharedCache` and `CreateBackplane` open one connection each, from the same settings. The connection never aborts when the server is down, so the host starts without Valkey (plan section 8.9). It switches off the four parts of the StackExchange.Redis handshake that the cache does not need, which are the reads of the cluster topology and of the server settings, the read of a key to choose a master, and the channel for a change of master.
The access control list of the application user is in [compose.yaml](../../compose.yaml): the keys `~v2:catalog:dev:*`, the channel `&catalog:dev.Backplane:v2`, and 14 commands ([the Valkey page](tools/valkey.md)).

## Where plan section 8.5 and Valkey differ

The plan says the ACL names `~catalog:{env}:*` and `&catalog:{env}:*`. FusionCache writes `v2:catalog:{env}:…` and names the channel `catalog:{env}.Backplane:v2`, so the user needs `~v2:catalog:{env}:*` and `&catalog:{env}.Backplane:v2`. The plan also says that the removal of L2 is awaited, and FusionCache writes a marker and leaves the entries to expire. The plan is not amended, and the correction is the maintainer's decision ([ADR-0038](../adr/0038-catalog-cache-adapter-and-valkey-connection.md)).

## The composition

The Api is the composition root (WP1.10), and an architecture rule keeps `IServiceProvider` out of the other layers, so the two projects hold no registration code. The Api registers:

- the options `CatalogCacheOptions` and `ValkeyOptions` with their validators and start-up validation;
- one `CacheMetrics` as a singleton, which it disposes;
- the shared cache and the backplane from `ValkeyConnection`, as singletons that it disposes;
- the adapter from `CatalogCacheFactory.Create` as a singleton that it disposes, and `ICatalogCache` as that instance.

`Catalog.Infrastructure.Tests` builds the nodes of its tests the same way with `CacheNode`.

## How it is tested

Each test names the requirement that it proves, and `governance trace` reads the names ([the traceability report](../testing/traceability.md)).

- `CatalogCacheTests` and `CatalogCacheOptionsTests` need no Docker. They prove the reads, the refusals, the single flight, the faults, the expiration that is set, the metrics, the removals, and the settings, with the in-process shared cache and a shared cache that fails on every call (REQ-CACHE-001, REQ-CACHE-002, REQ-CACHE-003).
- `CacheContractTests` holds the shape of the shared entries and the types of the cached queries (REQ-CACHE-001).
- `ValkeyOptionsTests` proves the settings and the switches of the connection (REQ-CACHE-001, REQ-CACHE-003).
- `CacheOverValkeyTests` runs two nodes against the Valkey image that compose.yaml pins: a second node reads what the first stored, the keys follow the scheme, the times to live reach the server, a refusal never does, the marker of a removal is written, and no key, value, or log line holds a secret (REQ-CACHE-001, REQ-CACHE-003).
- `ValkeyAclTests` creates the application user with the rules that it reads from compose.yaml, runs the whole scenario without a denied command, and proves what the user cannot reach (REQ-CACHE-001).
- `tests/Catalog.Architecture.Tests` holds the three rules of the layers (REQ-CACHE-001).
