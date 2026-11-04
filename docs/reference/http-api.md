---
title: "HTTP API host"
description: "How the catalog API starts and protects itself: the composition root, the order of the middleware, the two ports, the settings, the token validation, the endpoint policies, the limits, the security headers, the errors, the probes, and how it is tested."
audience: [learners, contributors, maintainers]
type: reference
tools: [aspnet-core, microsoft-aspnetcore-authentication-jwtbearer, microsoft-extensions-diagnostics-healthchecks-abstractions, microsoft-aspnetcore-mvc-testing, keycloak]
last-verified: 2026-11-03
owner: "@RostomOhannessian"
---

# HTTP API host

The HTTP API is the project [`src/Catalog.Api`](../../src/Catalog.Api/Catalog.Api.csproj). It is the composition root of the catalog: it registers every adapter of the persistence layer, the cache, and the application layer, and it holds the middleware that every request passes
([ADR-0040](../adr/0040-http-api-composition-and-token-validation.md), plan sections 8.1, 8.7, 8.9, and 9). This page describes what exists today. The endpoints of the contract ([the API contract](api-contract.md)) arrive in the second pull request of WP1.10 (planned), and the conformance check, the snapshots, and the viewer
in the third (planned).

## The composition

[`Program.cs`](../../src/Catalog.Api/Program.cs) builds the host with `AddCatalogApi` and `UseCatalogApi`, and nothing else. [`CatalogComposition`](../../src/Catalog.Api/Composition/CatalogComposition.cs) has one extension method for each concern, in the order that the host needs them:

| Method | Registers |
| --- | --- |
| `AddCatalogOptions` | Every section of settings with its validator, which runs when the host starts, so a wrong setting stops the host before it serves a request |
| `AddCatalogPersistence` | The credential source, the interceptors, the write and read contexts, the repositories, the unit of work, the stores, the outbox, and the hosted services ([the persistence page](persistence.md)) |
| `AddCatalogCache` | The shared cache, the backplane, the adapter, and `CacheInvalidationSubscriber` as the one singleton handler of the outbox ([the caching page](caching.md)) |
| `AddCatalogApplication` | The identifier generator, the caller, the interim telemetry, the ten validators, and the handlers of the two features through `ServiceCollectionFeatureRegistry`, which composes each chain with `CommandPipeline` and `QueryPipeline` |
| `AddCatalogSecurity` | The bearer handler, the endpoint policies, the rate limiter, and the forwarded headers |
| `AddCatalogHealth` | The four health checks |

A rule of `Catalog.Architecture.Tests` fails when a type of the Api outside the namespace `Catalog.Api.Composition` depends on a type of `Catalog.Infrastructure` or `Catalog.CrossCutting`, so the adapters are named in one place. The handlers and their chains are scoped to the request, the dispatcher is scoped, the cache and the
subscriber are singletons, and the credential source, the metrics, and the processor are singletons that the container disposes.

## The pipeline

```mermaid
flowchart LR
  accTitle: The order of the middleware of the API
  accDescr: A request passes the trace, the forwarded headers, the security headers, the exception handler, the status code pages, routing, the port gate, authentication, the rate limiter, and authorization before it reaches an endpoint.
  A[Trace] --> B[Forwarded headers] --> C[Security headers] --> D[Exception handler] --> E[Status code pages] --> F[Routing] --> G[Port gate] --> H[Authentication] --> I[Rate limiter] --> J[Authorization] --> K[Endpoint]
```

| Step | What it does |
| --- | --- |
| Trace | Starts an activity when none exists, so every error carries a trace identifier that is the one of the request |
| Forwarded headers | Reads `X-Forwarded-For`, `-Proto`, and `-Host` from the proxies that the settings name, and from no one else |
| Security headers | Registers the headers that every answer carries, including an error from another middleware |
| Exception handler | Turns an exception into the 500 or 503 of the error catalog and logs the exception with the trace identifier |
| Status code pages | Writes the body of an empty 4xx or 5xx, such as the 405 of routing or the 404 of an unknown path |
| Port gate | Keeps the management endpoints on the management port and the API on the public port, and checks the host of a request to the public port |
| Authentication | Validates the bearer token, and answers 401 with the challenge and the catalog entry |
| Rate limiter | Counts the caller and answers 429 with `Retry-After` |
| Authorization | Checks the endpoint policy, which is the scope and one of the roles |

## Ports and hosts

Kestrel listens on two sockets of one address. The public port serves the API, and the management port serves the health endpoints and nothing else. The gate reads the port that the connection arrived on, so a `Host` header that names the other port changes nothing, and a request that arrives on the wrong port gets a 404. A request to the public port must also name an allowed host, because the `Location` and `Link` headers are built from it. The management port does not check the host, because a probe names the address of the pod.

## The settings

Every section is bound from configuration and validated at start. A message names the setting and never repeats its value. No file in the repository holds a secret, so the two passwords come from the environment.

| Setting | Default | Meaning |
| --- | --- | --- |
| `Catalog:Hosting:ListenAddress` | `127.0.0.1` | The address of both sockets. A container sets `0.0.0.0` |
| `Catalog:Hosting:PublicPort`, `ManagementPort` | 8080, 8081 | The two ports, which must differ |
| `Catalog:Hosting:AllowedHosts` | `localhost;127.0.0.1;[::1]` | The hosts of the public port, as one string with semicolons. A name is exact, or `*.name` matches a subdomain. `*` is allowed in Development only |
| `Catalog:Auth:Issuer` | none | The issuer that a token must name, compared exactly. There is no default outside Development |
| `Catalog:Auth:MetadataAddress` | the issuer and `/.well-known/openid-configuration` | Where the API reads the keys, when it reaches the identity provider at an address other than the issuer |
| `Catalog:Auth:Audience` | `catalog-api` | The audience that a token must name |
| `Catalog:Auth:RequireHttpsMetadata` | `true` | Only the Development environment may turn it off |
| `Catalog:Auth:ClockSkew` | 30 seconds | At most 30 seconds |
| `Catalog:Auth:MetadataRefreshInterval` | 5 minutes | The shortest time between two reads of the metadata, from 1 second to 24 hours |
| `Catalog:Limits:MaxRequestBodyBytes` | 32 KiB | The largest body, from 1 KiB to 1 MiB |
| `Catalog:Limits:MaxJsonDepth` | 8 | The deepest nesting of a body, from 2 to 64 |
| `Catalog:Limits:ReadPermits`, `WritePermits` | 600, 120 | The requests of one caller in a window |
| `Catalog:Limits:Window` | 1 minute | The length of the window |
| `Catalog:ForwardedHeaders:KnownNetworks`, `KnownProxies` | none | The trusted proxies, as networks in CIDR notation and as addresses, each one string with semicolons. A network with a prefix of 0 is refused |
| `Catalog:ForwardedHeaders:ForwardLimit` | 1 | The number of proxies that the API reads from the chain |
| `Catalog:Database`, `Catalog:Outbox`, `Catalog:Idempotency` | see [the persistence page](persistence.md) | The database connection and its credential, the outbox, and the idempotency store |
| `Catalog:Valkey`, `Catalog:Cache` | see [the caching page](caching.md) | The connection to Valkey and the cache |

The setting `KnownNetworks` keeps the name of the plan. ASP.NET Core 10 renamed the property of the same name to `KnownIPNetworks` and marks the old one obsolete (checked 2026-11-03), and the API fills the new property. A list is one string with semicolons, as the `AllowedHosts` setting of ASP.NET Core is, because a .NET configuration array merges
the file, the environment, and the default, and a setting from the environment could not remove an entry of the file.

`appsettings.json` holds the defaults that every environment shares, and `appsettings.Development.json` holds the addresses of the Compose stack: the issuer `http://localhost:8180/realms/catalog`, the database on `localhost,1433` with the login `sa`, and Valkey on `localhost:6379` with the user `catalog-dev`.
The two passwords arrive as `Catalog__Database__Password` and `Catalog__Valkey__Password`. `.env` holds them for the stack (`MSSQL_SA_PASSWORD` and `VALKEY_APP_PASSWORD`).

## Token validation

| Check | Rule |
| --- | --- |
| Issuer | The exact string of `Catalog:Auth:Issuer`. The issuer that the metadata names is ignored |
| Audience | The exact string of `Catalog:Auth:Audience` |
| Signature | A key of the metadata verifies it. The handler reads the metadata again when a token names an unknown key, at most once for each refresh interval, and keeps no previous key set |
| Algorithm | RS256 or ES256, written in code |
| Lifetime | The token needs an expiry, and the skew is at most 30 seconds |
| Claims | The names are the ones of the token. Scopes come from the space-delimited `scope`, roles from `realm_access.roles`, and the caller from `sub`, or `azp` when there is no subject |

A refused token gets 401 with `WWW-Authenticate: Bearer realm="catalog", error="invalid_token"`, and a request without a token gets the same challenge without the error. The body is the `unauthorized` entry of [the error catalog](error-catalog.md), and the challenge names no cause. A token in the query string is not read.

## Endpoint policies

Each access level has one policy, named after its scope, and a policy needs the scope and one of the roles of `PermissionPolicy`, the same table that the authorization decorator reads ([the application layer](application-layer.md#permissions)). A token with the scope and without the role gets 403 `forbidden` with a detail that names the roles, and a token without the scope gets a detail that names the scope.
An endpoint binds the level of its use case with `RequireAccessOf<TRequest>()`, so the endpoint and the pipeline read the level from the same request type.

## Limits

A caller with a token is counted by its identifier, and any other caller by its address. A read and a write have separate counts. The answer to a refused request is 429 `rate-limited` with `Retry-After` in whole seconds. The health endpoints are not limited. Kestrel limits the body to `MaxRequestBodyBytes` and the headers to 16 KiB, and the endpoints read a body with the size and the depth of the settings (second pull request of WP1.10, planned).

## Security headers

| Header | Value |
| --- | --- |
| `X-Content-Type-Options` | `nosniff` |
| `X-Frame-Options` | `DENY` |
| `Content-Security-Policy` | `default-src 'none'; frame-ancestors 'none'` |
| `Referrer-Policy` | `no-referrer` |
| `Permissions-Policy` | `geolocation=(), camera=(), microphone=()` |
| `Cross-Origin-Resource-Policy` | `same-origin` |
| `Cache-Control` | `no-store` |
| `Strict-Transport-Security` | `max-age=31536000; includeSubDomains`, only when the request came over HTTPS, which a trusted proxy can tell |

The server header is off. A header that an endpoint already set is kept.

## Errors

One writer makes every error. Its `type` is the address of an entry of [the error catalog](error-catalog.md), and a status that the catalog does not list, such as 405, has the type `about:blank` (RFC 9457). The `detail` is fixed text, the `instance` is the path, and the `traceId` is the trace of the request. The API raises eight entries by itself (`malformed-request`, `idempotency-key-required`, `unauthorized`, `payload-too-large`, `unsupported-media-type`, `rate-limited`, `internal-error`, and `service-unavailable`) and maps the other nine from the typed errors of the application ([ADR-0032](../adr/0032-decorator-pipeline-and-application-results.md)).
An unhandled exception is a 500 with a generic text, and the log holds it with the trace identifier. A database exception anywhere in the chain is a 503 with `Retry-After: 5`.

## Health endpoints

The endpoints are on the management port and are not routed by the gateway. The body is JSON with the status, the duration, and each check with its status and its text, and the status of a probe is 200 for `Healthy` and `Degraded`, and 503 for `Unhealthy`.

| Endpoint | Checks | Meaning |
| --- | --- | --- |
| `/health/live` | None | The process answers |
| `/health/startup` | `credentials` | The configuration is valid, and the source of the database credential holds a credential |
| `/health/ready` | `credentials`, `database`, `outbox`, `cache` | See below |

- **database.** The connection opens with the credential of the source, and the history of the database has every migration that this build expects. A schema that is ahead of the code is healthy ([the migration policy](../plans/implementation-plan.md)). An unreachable server, a revoked login, and a schema that is behind are unhealthy.
- **outbox.** A message in the dead-letter state, or a message that was due before a whole lease and two polls ago, is `Degraded`, and a database that cannot be read is unhealthy. The alerts that watch the counters are WP1.11.
- **cache.** The state of the circuit breakers of the shared cache and the backplane. A node whose Valkey is down is `Degraded` and never unhealthy, so readiness stays green ([the consistency model](../explanation/cache-consistency-model.md)).

No text of an exception, no connection string, no user name, and no credential is in a report or in a log line, and the writer replaces the description of a check that threw.

## Telemetry until WP1.11

`LoggingUseCaseTelemetry` implements `IUseCaseTelemetry` with one structured log line for each outcome of a use case, with the name of the use case and the kind of refusal. WP1.11 replaces it with an adapter over an activity source and a meter, and the registration is the one line that changes.

## Run it against the Compose stack

Start the stack, apply the migrations with the bundle of [the runbook](../runbooks/database-migration.md), and set the two passwords from `.env`:

```bash illustrative
./dev compose up
export Catalog__Database__Password="<MSSQL_SA_PASSWORD from .env>"
export Catalog__Valkey__Password="<VALKEY_APP_PASSWORD from .env>"
dotnet run --project src/Catalog.Api
```

The API listens on `127.0.0.1:8080`, and the probes answer on `127.0.0.1:8081`. A token comes from the `catalog` realm of the stack ([Keycloak](tools/keycloak.md)).

## How it is tested

Each test names the requirement that it proves, and `governance trace` reads the names ([the traceability report](../testing/traceability.md)). `tests/Catalog.Api.Tests` starts the real composition in process, with an in-process identity provider that serves the metadata and the keys over HTTP and signs tokens with keys that it makes at run time.

- `ConfigurationValidationTests`, `AppSettingsFilesTests`, and `CompositionTests` prove the settings, the settings files, the lifetimes, and that each wrong setting stops the host with a message that names it (REQ-ARC-002, REQ-APP-006).
- `TokenClaimsTests`, `JwtValidationTests`, `JwksRotationTests`, and `AccessPolicyTests` prove the claims, every check of the token, the rotation and the withdrawal of a key, and all 192 combinations of scope and role (REQ-IDN-002, REQ-IDN-003).
- `ProblemDetailsTests`, `SecurityHeadersTests`, `RateLimitTests`, and `ForwardedHeadersTests` prove the error shape on every path, the headers, the limiter, and the proxies (REQ-API-005, REQ-API-007, REQ-ARC-002).
- `HealthTests` starts real sockets and a real SQL Server 2022 container from the digest of `compose.yaml`, and proves the probes, the two ports, and that no secret reaches an answer or a log (REQ-QUA-001, REQ-CACHE-003).
- The token handler reads the system clock and has no `TimeProvider`, so the tests build tokens from the clock of the machine, and every boundary keeps a margin of one second on each side.
- `tests/Catalog.Architecture.Tests` holds the rule that keeps the adapters in the composition namespace, and `tests/Catalog.Contract.Tests/ApiAgreementTests.cs` compares the error table of the API with the error catalog.
