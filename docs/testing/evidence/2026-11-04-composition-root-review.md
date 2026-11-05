---
title: "Evidence record: the review of the composition root (2026-11-04)"
description: "A dated record of the manual review that REQ-ARC-002 asks for: where the API registers dependencies and uses the service provider, what its endpoints, mapping, and HTTP helpers do and do not decide, the rules that hold it, and the observations that the review made."
type: evidence-record
date: 2026-11-04
status: verified
audience: [maintainers, contributors]
last-verified: 2026-11-04
owner: "@RostomOhannessian"
---

# Evidence record: the review of the composition root (2026-11-04)

REQ-ARC-002 says that only `Catalog.Api` may inject `IServiceProvider` or register dependency wiring, and that no business logic may live in the composition root. Its verification modes are `architecture` and `manual`, and its evidence is a composition-root review. The
architecture mode is proven by rules that run on every build. This record is the manual mode: a reading of every file of `src/Catalog.Api` at the merge of the third pull request of WP1.10 (commit `fecb31e`, checked 2026-11-04), with the method and the observations, so a later reader can repeat it.

## Method

1. Search `src` for `IServiceProvider`, `GetRequiredService`, `GetService`, `BuildServiceProvider`, `IServiceCollection`, and `IServiceScopeFactory`, and list each file that has a match.
2. Read every file of `src/Catalog.Api` (31 files) and sort each into one of four roles: wiring (it registers or resolves services), pipeline (it orders the middleware or maps routes), translation (it turns HTTP into a request of a use case or a result into HTTP), or other.
3. For each file that is not wiring, ask whether it decides a rule of the catalog: what a name, a SKU, a price, or a status may be, who may do what, when a state changes, or what is stored.
4. Check that a rule of `Catalog.Architecture.Tests` fails on a violating fixture for each claim that a machine can hold.

## What the search found

| Search | Where | Result |
| --- | --- | --- |
| `IServiceProvider`, `GetRequiredService`, `IServiceCollection` | Only `src/Catalog.Api`: `Composition/CatalogComposition.cs`, `Composition/ServiceCollectionFeatureRegistry.cs`, and `CatalogPipeline.cs` | No other project of `src` has a match, so Domain, Application, Contracts, CrossCutting, and Infrastructure register nothing and resolve nothing |
| `BuildServiceProvider`, `IServiceScopeFactory` | Nowhere in `src` | None |

## What each part does

| Part | Files | Role | Decides a rule of the catalog? |
| --- | --- | --- | --- |
| Entry point | `Program.cs` | Builds the host with `AddCatalogApi` and `UseCatalogApi`, and nothing else | No |
| Wiring | `Composition/CatalogComposition.cs`, `ServiceCollectionFeatureRegistry.cs`, `LoggingUseCaseTelemetry.cs` | Registers the options with their validators, the persistence, the cache, the application layer, the security, the versioning, and the health checks. The registry composes each chain with `CommandPipeline` and `QueryPipeline`, so the order of the decorators is not decided here | No. It names adapters and the order of services, and no condition of the catalog |
| Pipeline | `CatalogPipeline.cs`, `Endpoints/CatalogEndpoints.cs` | Fixes the order of the middleware and maps the routes | No |
| Endpoints | `Endpoints/CategoryEndpoints.cs`, `ProductEndpoints.cs` | Each of the ten handlers reads the request through a mapping, runs the handler through its interface, and writes the result. Each one has the same shape (read, run, write) and no branch on the catalog's data | No |
| Translation | `Mapping/*`, `Http/JsonBody.cs`, `ConditionalHeaders.cs`, `QueryParameters.cs`, `Responses.cs`, `Problems.cs`, `Outcome.cs`, `UtcDateTimeOffsetConverter.cs` | Turns the shape of HTTP (one quoted tag, a bounded body, a page size, an amount as text) into the plain values of a use case and the views of a use case into the records of the contract | No. A value that has the shape of the contract goes on to the use case, which judges what it means (the validators and the domain model hold the length of a name and the pattern of a SKU) |
| Security | `Security/*`, `Configuration/ApiOptions.cs` | Reads the token claims, maps each endpoint to the access level that `PermissionPolicy` defines, and holds the settings | No. The one table of access levels is in the Application layer, and the endpoint policy reads it |
| Documentation | `Documentation/*` | Generates the served document from the endpoint table and the shared components of the contract, and registers the viewer in Development only | No |

## Observations

- **The pipeline resolves one service.** `CatalogPipeline.cs` resolves `ApiErrorHandler` from `RequestServices` for the exception handler. It is a service lookup, but it is in the composition root of the one project that may do it. A later change could register the handler as the exception handler of the options and remove the lookup, and nothing requires it.
- **The mapping names one value type of the Domain.** `Mapping/ViewMapping.cs` reads the number of decimal places of a currency from `Catalog.Domain.Pricing.Currency` to write an amount as text (`1990` for the yen, `89.99` for the dollar). It is the spelling of an output and decides no rule, and the Domain stays the only owner of the number. The rule `api-endpoints-depend-on-contracts-and-handler-interfaces-only` covers the endpoints namespace and not the mapping, so this dependency is allowed and is recorded here.
- **The transport checks stay at the shape.** `TransportInput.TryReadPrice` accepts an amount of the shape of the contract (at most 12 integer digits, no sign, no exponent) and leaves its meaning, and the currency, to the use case. This is the border that the review looked for: a repeated rule would be a duplicate of the domain.
- **No endpoint holds a condition on catalog data.** The only branches are on the outcome of a reader (`IsSuccess`) and of a handler.
- **Static state.** The rules `no-static-mutable-fields-outside-the-composition-root` and `no-static-mutable-properties-outside-the-composition-root` pass on the real assemblies, and the Api holds no mutable static state outside its composition namespace.

## The rules that hold the claims

| Claim | Rule or test | Fails on a violating fixture |
| --- | --- | --- |
| Nothing outside the Api uses the service provider or a scope factory | `no-service-locator-outside-the-composition-root` | Yes, `ArchitectureRulesTests` |
| The Api names Infrastructure and CrossCutting only in its composition namespace | `api-names-infrastructure-and-cross-cutting-only-in-its-composition-namespace` | Yes |
| The endpoints depend on the contracts and the handler interfaces only | `api-endpoints-depend-on-contracts-and-handler-interfaces-only` | Yes |
| Infrastructure and CrossCutting do not depend on the composition root | `infrastructure-does-not-depend-on-cross-cutting-or-the-composition-root`, `cross-cutting-does-not-depend-on-infrastructure-or-the-composition-root` | Yes |
| Every request of the Application resolves through its chain | `CompositionTests` (REQ-ARC-002, REQ-APP-006) | n/a |
| A wrong setting stops the host at start | `ConfigurationValidationTests` | n/a |

## Conclusion

The composition root is `src/Catalog.Api`, it is the only project that registers services or resolves them, and the review found no business rule in its endpoints, mapping, HTTP helpers, security, or documentation parts. The observations above are allowed by the rules and need no change. REQ-ARC-001 asks for the layer rules and has its architecture evidence in the same rule set, so this record also serves it.

## Limits

- This is a reading of one commit. A later change to `src/Catalog.Api` needs the rules above to pass and a reader to look for a condition on catalog data again.
- `Catalog.Client` does not exist yet (WP4.1), so the rule that keeps it from referencing a server project ran only against fixtures.
