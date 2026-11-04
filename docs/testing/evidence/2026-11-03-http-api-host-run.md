---
title: "Evidence record: the host of the HTTP API (2026-11-03)"
description: "A dated record of the first pull request of WP1.10 on Windows with Docker Desktop and in the pinned Linux SDK image: what ran where, the counts, what the tests showed about the composition root, the token checks, the policies, the errors, the limits, the proxies, the two ports, and the probes, and what the runs did not show."
type: evidence-record
date: 2026-11-03
status: verified
audience: [maintainers, contributors]
last-verified: 2026-11-03
owner: "@RostomOhannessian"
---

# Evidence record: the host of the HTTP API (2026-11-03)

This record is the evidence for the first pull request of WP1.10 (HTTP API): `src/Catalog.Api` as the composition root and the security of the host, which are the JWT bearer validation, the policies, the one writer of errors, the limits, the security headers, the forwarded headers, the two ports, and the health endpoints ([ADR-0040](../../adr/0040-http-api-composition-and-token-validation.md), [the HTTP API host](../../reference/http-api.md)). The endpoints of the catalog arrive in the second pull request, so this record says nothing about them. It says what ran where, so a reader can tell which host proved which claim.

## What ran where

| Where | Command | What it ran | Result |
| --- | --- | --- | --- |
| Windows x86-64, Docker Desktop, .NET SDK 10.0.403 | `dotnet test --solution ProductCatalog.slnx --configuration Release --no-build --report-trx` | Every test project natively. The Api tests start the real composition in process, with an in-process identity provider on a loopback socket, and the probes use a SQL Server 2022 container that an assembly fixture starts from the digest of `compose.yaml` | 1,996 tests ran, 1,996 passed, 0 failed, 0 skipped (before the record was written, 3 tests of the repository self-check failed because it did not exist yet). By project: Api 148, Application 268, Architecture 144, Contract 103, Domain 173, Infrastructure 271, Documentation auditor 265, Governance auditor 624. The Api suite took about 65 seconds and the Infrastructure suite about 48 seconds |
| Linux, the pinned SDK 10.0.401 image, Docker socket mounted | `docker compose -f tools/lint/compose.yaml run --rm linux-tests` | A scratch copy of the repository: locked restore, Release build with warnings as errors, every test project with the code coverage extension, and the coverage thresholds | 1,996 tests ran, 1,996 passed, 0 failed, 0 skipped. The first Linux run failed in the locked restore (NU1004) because `tests/Catalog.Contract.Tests/packages.lock.json` lacked the new project reference to `Catalog.Api`, so the lock file was regenerated and committed. The coverage gate passed: Catalog.Application 99.54% of lines and 99.58% of branches, Catalog.Domain 99.63% of lines and 98.41% of branches, and the solution 96.87% of lines (2,665 of 2,751) |

Smart App Control blocked the freshly built `Catalog.Api.dll` on this Windows host during the work (error 0x800711C7), so some runs of the Api tests happened only in the Linux image, through a helper that ran one project in the `linux-tests` image. The last Windows run above was not blocked.

## What the tests showed

| Question | Answer | Test |
| --- | --- | --- |
| Is every use case of the catalog registered as the chain that the pipelines compose? | Yes. The registry resolves each of the ten requests to its handler through `CommandPipeline` and `QueryPipeline`, scoped to the request, and the caller of the use cases is the caller of the request | `CompositionTests` |
| Which lifetimes does the composition root give the outbox? | The dispatcher is scoped, and `CacheInvalidationSubscriber` is one singleton handler that the processor and the dispatcher both take | `CompositionTests` |
| What does a wrong setting do? | It stops the host at start. The message names the setting and never repeats its value, and a missing database or Valkey credential stops the host without printing a secret | `ConfigurationValidationTests`, `CompositionTests` |
| Do the settings files hold a secret? | No. The Development file and the two passwords from the environment make every option valid, and without the passwords the host stops. The production file names no issuer, so a deployment must provide one over HTTPS | `AppSettingsFilesTests` |
| Which tokens are accepted? | A token signed with RS256 or ES256 by a key that the issuer publishes, with the exact issuer, the audience `catalog-api`, and an expiry. A token that expired 29 seconds ago, or is valid in 29 seconds, is accepted, and one at 31 seconds is refused | `JwtValidationTests` |
| Which tokens are refused? | A wrong issuer or audience, a key that the issuer does not publish (also with a published key id), an algorithm outside the allow-list even when the signature is valid, the public key used as an HMAC secret, an unsigned token, a token without an expiry, a value that is not a token, and a token in the query string. The challenge names the realm and the error `invalid_token` and no description | `JwtValidationTests` |
| What does rotation do? | A token signed with a key that the issuer added is accepted after the refresh of the metadata. A key that the issuer stopped publishing is refused after the next refresh, because the last known good configuration is off. The issuer is the one of the settings, even when the metadata advertises another | `JwksRotationTests` |
| Does a token reach a log or an error body? | No. The log and the error body of a refused request hold none of the token | `JwtValidationTests` |
| Does a scope alone, or a role alone, grant access? | No. All 192 combinations of scopes and roles for the access levels get the answer that an independent oracle gives, a token with the scope and without the role is refused with an answer that names the roles, a role does not replace a scope, and a scope with another case or a longer name does not count | `AccessPolicyTests` |
| How are the claims read? | The scopes from the space-delimited claim, the roles from `realm_access.roles` only, the caller from `sub` with `azp` as the fallback, and a principal that is not authenticated has none of them | `TokenClaimsTests` |
| What is the shape of an error? | `application/problem+json` with a trace identifier of 32 hexadecimal characters, a different one for each request. An unhandled exception shows no text and the log keeps it with the trace identifier. A database failure anywhere in the chain is a 503 with `Retry-After` and no driver text | `ProblemDetailsTests` |
| What does the limiter do? | The request after the last permit gets 429, `Retry-After`, and the error of the catalog. One caller spending its permits does not limit another, a write has a lower limit than a read, a request without a token is counted by its address, and a flood of bad tokens from one address is limited | `RateLimitTests` |
| Do the security headers cover every answer? | Yes, success, each kind of error, and a refusal by a policy. Transport security is not sent over plain HTTP | `SecurityHeadersTests` |
| Who may tell the host the address, the scheme, or the host name? | Only a proxy in a known network. A caller outside the networks cannot spoof any of them, a forwarded host that is not allowed is refused even from a trusted proxy, and a request to the public port must name an allowed host | `ForwardedHeadersTests` |
| Are the health endpoints on the management port only? | Yes, on real sockets. The API is not served on the management port, a caller cannot reach a probe by naming that port in the host header, and the management port is neither limited nor host-checked | `HealthTests` |
| What do the probes say? | Liveness checks nothing. Startup checks that the credential is loaded and never names it. Readiness is healthy with a current schema, unhealthy when the database is unreachable or the schema is behind the code (and says nothing about the server or the login), and degraded but green for a dead-letter message or a degraded cache | `HealthTests` |
| Does the Api name an adapter outside its composition namespace? | The architecture rule fails on the violating fixture `Violations.Api.LeakyEndpoint` and passes on the clean fixture | `ArchitectureRulesTests` |
| Does the error table of the API agree with the error catalog? | Yes, for each of the 8 types that the API owns | `ApiAgreementTests` |

## Limits

- `TokenValidationParameters` has no time provider in Microsoft.IdentityModel 8.x, so the tests of the expiry use tokens made with the real clock and a margin of one second around the 30-second skew. The other tests use a fake time provider.
- A request that names an unknown key makes the handler refresh the metadata, bounded by the refresh interval. The tests set the interval to one second and poll a condition of the system with a bound, never a fixed sleep.
- Hosting, headers, forwarded-headers, and management-port behavior has no requirement of its own in `docs/requirements/requirements.yaml`, so those tests carry `REQ-ARC-002`, the nearest real requirement. The gap is reported to the maintainer and no requirement was invented.
- Timings are single samples from one machine. No test ran against a real Keycloak in this pull request, because the realm contract tests belong to the third pull request of WP1.10.
- No hosted run existed for this branch when this record was written. The `ci` workflow runs the same suites on Linux without the Docker socket exception.
