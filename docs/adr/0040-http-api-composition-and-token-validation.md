---
title: "ADR-0040: Compose the API in one root with a fixed middleware order, two ports, and token validation that fixes the algorithms"
description: "Decide how the catalog API starts and protects itself: one composition root that registers every adapter, a middleware order that is the design, a public port and a management port that a connection cannot swap, bearer validation with exact issuer, audience, and algorithms, scope-and-role policies, rate limits, forwarded headers that trust only named proxies, and probes that report degradation honestly."
status: accepted
date: 2026-11-03
decision-makers: ["@RostomOhannessian"]
supersedes: []
superseded-by: []
tools: [aspnet-core, microsoft-aspnetcore-authentication-jwtbearer, microsoft-extensions-diagnostics-healthchecks-abstractions, microsoft-aspnetcore-mvc-testing, keycloak, sql-server]
related-adrs: ["0004", "0017", "0026", "0028", "0032", "0033", "0035", "0036", "0037", "0038", "0039"]
evidence:
  - src/Catalog.Api/Composition/CatalogComposition.cs
  - src/Catalog.Api/CatalogPipeline.cs
  - src/Catalog.Api/Security/ConfigureJwtBearer.cs
  - src/Catalog.Api/Http/Middleware.cs
  - tests/Catalog.Api.Tests/JwtValidationTests.cs
  - tests/Catalog.Api.Tests/HealthTests.cs
  - docs/testing/evidence/2026-11-03-http-api-host-run.md
---

# ADR-0040: Compose the API in one root with a fixed middleware order, two ports, and token validation that fixes the algorithms

## Context and problem statement

WP1.10 builds the HTTP API on top of the layers of WP1.6 to WP1.9. The first part of the work package does not serve a resource yet. It builds the host that every endpoint will run in: where the registrations live, in which order a request passes
the middleware, how a caller proves who it is, what a refused or failed request looks like, and how the platform asks whether the process is healthy. Plan sections 8.1, 8.6, 8.7, 8.9, and 9 describe each part, and they leave open how the parts fit together. These questions
were open:

- **Where the registrations live and how they are kept honest.** Plan section 8.1 makes the Api the only composition root, and the persistence and cache pages name the registrations that it must make (the dispatcher is scoped, the cache subscriber is one singleton handler of the outbox, the options validators run at start).
- **The order of the middleware.** A different order gives a different system: an error that has no trace identifier, a limiter that cannot tell the caller, a 401 without the security headers.
- **How a request reaches the management port and the public port.** Plan section 8.9 says the gateway never routes to port 8081. A check that reads a header would be a check that a caller can write.
- **What the token validation fixes.** Plan section 8.7 lists the checks, and the library has defaults that widen two of them.
- **How a refusal and a fault look.** Plan section 8.6 and [ADR-0028](0028-http-api-conventions-of-v1.md) fix the error format, and every path that writes an error must write the same one.
- **What the probes report.** Plan section 8.9 keeps Valkey out of readiness, and the requirement REQ-CACHE-003 says that readiness stays green while the cache degrades.

This ADR serves REQ-ARC-002, REQ-IDN-003, REQ-IDN-002, REQ-API-005, REQ-API-007, REQ-APP-006, and REQ-CACHE-003.

Facts that shape the decision (checked 2026-11-03):

- `ForwardedHeadersOptions.KnownNetworks` is obsolete in ASP.NET Core 10 (warning ASPDEPR005), and `KnownIPNetworks` replaces it. Both lists default to the loopback address, so the default trusts a proxy on the machine.
- Microsoft.IdentityModel 8.19 keeps the previous key set for one hour after a refresh and validates a token that fails with the new set against the old one (the last known good configuration). The handler of the metadata also adds the issuer that the metadata names to the valid issuers.
- The token handler of Microsoft.IdentityModel reads the system clock and has no `TimeProvider`, so a test cannot give it a fake clock.
- `JsonWebTokenHandler` adds an expiry of one hour to a token that has none, unless `SetDefaultTimesOnTokenCreation` is off.
- A .NET configuration array that is bound to a property with a default merges the file, the environment, and the default, so a setting from the environment cannot remove an entry of the file.
- Keycloak 26.8.0 writes the realm roles in the object `realm_access` and the scopes in one space-delimited claim, and it grants a requested scope to a user who lacks the mapped role ([ADR-0026](0026-api-only-compose-stack-and-catalog-realm.md), risk R21).

## Decision drivers

- A person who reads the composition root sees every adapter that the process uses and every scope that it has, and an architecture rule keeps the adapters out of the rest of the Api.
- A refusal or a fault always has the same shape, the trace identifier, and the security headers, whichever middleware wrote it.
- A caller cannot choose the port that it arrives on, the address that it is counted by, or the host that the links name.
- The token validation of plan section 8.7 holds even when a setting, the identity provider, or the library default would widen it.
- No answer, log line, or exception shows a token, a credential, or the text of an exception that a caller could use.
- Every behavior has a test that runs the real composition, and the tests need no secret and no clock of the machine where the library allows.

## Considered options

1. One composition root of extension methods, a fixed middleware order, a gate on the port of the connection, hard-coded algorithms, and probes that report degradation (chosen).
2. A framework for modules that register themselves, with the middleware in the order that each module prefers.
3. The defaults of ASP.NET Core and of the bearer handler, configured by settings.

## Decision outcome

Chosen option: **one composition root and a fixed pipeline**, because the order and the registrations are the design and a reader must see them in two files, and because each default of option 3 that matters here is wider than the plan allows.

| Area | Decision | Why |
| --- | --- | --- |
| Composition | `CatalogComposition` holds one extension method for each concern (options, persistence, cache, application, security, health), `ServiceCollectionFeatureRegistry` implements `IFeatureRegistry` and composes each chain with `CommandPipeline` and `QueryPipeline`, and `Program` only calls them. The Api project exposes its internals to the tests | The reference compositions of the persistence and cache tests become one real one, and the order of the decorators stays in `CrossCutting` |
| Adapters stay in the root | An architecture rule fails when a type of the Api outside the namespace `Catalog.Api.Composition` depends on an `Infrastructure` or `CrossCutting` type | The root registers the adapters, and the rest of the Api works through ports and contracts |
| Middleware order | Trace, forwarded headers, security headers, exception handler, status code pages, routing, port gate, authentication, rate limiter, authorization, endpoints | Every answer has a trace and the headers, the limiter counts the caller that authentication found, and a refused request never costs a policy check |
| Two ports | Kestrel listens on the public port (8080) and the management port (8081) on one address. The gate reads `Connection.LocalPort` and an endpoint marker: a management endpoint answers only on the management port, and the API never answers on it | The port of a connection is not a header. A `Host` header that names 8081 on the public port still gets 404 |
| Hosts | A request to the public port must name an allowed host, and the management port does not check it | The `Location` and `Link` headers are built from the host, and a probe names the address of a pod |
| Issuer, audience | The issuer is compared exactly and in order, with an `IssuerValidator` that ignores the issuer of the metadata. The audience is exact | The handler adds the issuer of the metadata to the valid issuers, which would accept a second issuer |
| Algorithms | RS256 and ES256 are written in code, and no setting widens them | A setting that widened them would be the way back to HS256 key confusion |
| Lifetime | The skew is at most 30 seconds, which the validator of the settings enforces, and a token needs an expiry | Plan section 8.7 |
| Keys | The handler reads the metadata again when a token names an unknown key, at most once for each refresh interval (default 5 minutes). The last known good configuration is turned off | A key that the issuer withdrew must stop working when the API has read the metadata again, and Keycloak keeps the old key published while it rotates |
| Metadata over HTTPS | The setting defaults to on, and only the Development environment may turn it off, for the plain-HTTP issuer of the Compose stack | A production issuer that is not HTTPS stops the host at start |
| Claims | The handler keeps the claim names of the token. Scopes come from the space-delimited `scope`, roles from `realm_access.roles`, and the caller from `sub`, or `azp` when there is no subject | Those are the names that Keycloak writes. A claim in another shape grants nothing |
| Policies | One endpoint policy for each access level, built from `PermissionPolicy`, so a token needs the scope and one of the roles. The answer of a refusal names what the operation requires | R21, and the pipeline reads the same table |
| Rate limit | One fixed-window limiter for every request. A caller with a token is counted by its identifier, and any other by its address. A read and a write have separate counts and a write has the lower default (600 and 120 each minute). The health endpoints are not limited. A refusal is a 429 `rate-limited` with `Retry-After` | A flood of bad tokens is limited by address, and a probe must always get an answer |
| Request limits | Kestrel limits the body to 32 KiB and the headers, and the settings hold the depth of a JSON body (at most 8 levels). The endpoints read a body with both limits (second pull request of WP1.10, planned), because TestServer has no Kestrel limit | Plan section 8.6 |
| Forwarded headers | `X-Forwarded-For`, `-Proto`, and `-Host` are read only from the networks and the addresses that `Catalog:ForwardedHeaders` names, and both default lists are cleared. The setting keeps the name `KnownNetworks` of the plan and fills `KnownIPNetworks`. A network with a prefix of 0 is refused | The default trusts the loopback address |
| Lists in settings | The allowed hosts, the networks, and the proxies are each one string with semicolons, as the `AllowedHosts` setting of ASP.NET Core is | A setting from the environment replaces the whole list and never merges with the file |
| Security headers | Every answer carries `X-Content-Type-Options`, `X-Frame-Options`, `Content-Security-Policy`, `Referrer-Policy`, `Permissions-Policy`, `Cross-Origin-Resource-Policy`, and `Cache-Control: no-store`, and `Strict-Transport-Security` when the request came over HTTPS. The server header is off | The API serves JSON to programs, so the strictest policy costs nothing |
| Errors | One writer makes every `application/problem+json`, with the address of the entry, the path, and a trace identifier that is the activity of the request, or one that a middleware starts when no listener made one. A status that the catalog does not list is `about:blank` (RFC 9457). The text of an exception never reaches the answer, and a database exception anywhere in the chain is a 503 with `Retry-After` | One shape for every path |
| Probes | `/health/live` checks nothing, `/health/startup` checks that the credential is loaded, and `/health/ready` checks the database (connection, login, and migrations that this build expects), the outbox, and the cache. A degraded outbox or cache is a 200 with `Degraded` in the body, and only an unhealthy check is a 503. The writer never prints an exception | REQ-CACHE-003, and plan section 8.9 |
| Telemetry until WP1.11 | `LoggingUseCaseTelemetry` writes one structured line for each outcome of a use case, with the name of the use case and the kind of refusal | WP1.11 replaces the one registration |

### Consequences

- Good: a reader finds every adapter in one namespace, and an architecture rule with a violating fixture keeps it so.
- Good: the identity checks of plan section 8.7 hold against a widened setting, a second issuer in the metadata, and a withdrawn key, and each has a test.
- Good: a request cannot change the port that it arrived on, the address that it is counted by, or the host that the links name.
- Bad: the token times in the tests come from the system clock, because the token handler has no `TimeProvider`. Mitigation: each boundary of the tests keeps a margin of one second on each side, and a test run uses a few milliseconds.
- Bad: turning off the last known good configuration means that a rotation that withdraws the old key at once rejects a token that is still inside its five minutes. Mitigation: Keycloak keeps the old key published while it rotates, and a deployment that does not can raise the refresh interval of the metadata.
- Bad: the interim telemetry adapter logs and does not trace. Mitigation: WP1.11 owns the adapter, and the port is the contract.
- Neutral: the tests start the whole composition in process, so each test class that needs the database starts a host of its own. The SQL Server container starts once for the assembly.

### Confirmation

- `JwtValidationTests` and `JwksRotationTests` hold every check of the token validation against an in-process issuer, including the withdrawn key and the issuer that the metadata advertises.
- `AccessPolicyTests` runs all 192 combinations of scope and role for the three levels against an oracle that is written apart from the permission table.
- `ForwardedHeadersTests`, `SecurityHeadersTests`, `RateLimitTests`, and `ProblemDetailsTests` hold the headers, the proxies, the limiter, and the error shape on every path.
- `HealthTests` runs the host on real sockets with a real SQL Server container, and proves that the health endpoints exist on the management port only, that a degraded cache and a dead-letter message keep readiness green, and that no secret reaches an answer or a log.
- `CompositionTests` resolve every use case through its chain and prove the lifetimes of the dispatcher and the subscriber, and that each wrong setting stops the host at start with a message that names the setting.
- `tests/Catalog.Architecture.Tests` holds the rule that keeps the adapters in the composition namespace, and `ApiAgreementTests` compares the error table of the API with the error catalog.

## Pros and cons of the options

### One composition root of extension methods, a fixed middleware order, a gate on the port, and probes that report degradation

- Good: the order and the registrations are plain code that a test and a reviewer read.
- Good: each default of the library that is wider than the plan is closed in one place.
- Bad: it repeats a few lines that a framework would hide, such as the registration of ten validators.

### A framework for modules that register themselves

- Good: a feature adds itself without a change to the root.
- Bad: the order of the middleware becomes the sum of the preferences of the modules, and plan section 8.2 asks for explicit plumbing.

### The defaults of ASP.NET Core and of the bearer handler

- Good: no code.
- Bad: the default trusts the loopback address, accepts the issuer of the metadata, and keeps a withdrawn key for an hour, and each of the three is wider than plan sections 8.7 and 9 allow.

## Revisit when

- Microsoft.IdentityModel gains a `TimeProvider`, and the token tests can use the fake clock of the other tests.
- A deployment cannot give the API a port of its own for the probes, and the probes must share the public port.
- The platform realm (WP3.5) or the gateway (WP3.8) needs another issuer or another proxy network.
- WP1.11 replaces the interim telemetry adapter, or WP4.4 measures the limits under load and the defaults change.

## Further reading

- [Forwarded headers in ASP.NET Core](https://learn.microsoft.com/aspnet/core/host-and-deploy/proxy-load-balancer): the default trust of the loopback address and the deprecation of `KnownNetworks` (checked 2026-11-03)
- [RFC 6750: Bearer token usage](https://www.rfc-editor.org/rfc/rfc6750.html): the challenge and its error codes (checked 2026-11-03)
- [RFC 9457: Problem Details for HTTP APIs](https://www.rfc-editor.org/rfc/rfc9457.html): the type `about:blank` (checked 2026-11-03)
- [RFC 8725: JSON Web Token best current practices](https://www.rfc-editor.org/rfc/rfc8725.html): the algorithm allow-list and the exact issuer and audience (checked 2026-11-03)
