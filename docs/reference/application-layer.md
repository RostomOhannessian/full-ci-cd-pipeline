---
title: "Application layer"
description: "The handler interfaces, the results and typed errors with their mapping to the error catalog, the permission table, the decorator order, and the ports of the catalog application layer."
audience: [learners, contributors, maintainers]
type: reference
tools: [fluentvalidation]
last-verified: 2026-11-01
owner: "@RostomOhannessian"
---

# Application layer

The application layer is the project [`src/Catalog.Application`](../../src/Catalog.Application/Catalog.Application.csproj). It holds the use cases of the catalog and the ports that the outer layers implement. It references the domain model and the base class library only
([ADR-0032](../adr/0032-decorator-pipeline-and-application-results.md)). The decorator code is in `Catalog.CrossCutting`, the registration lives in the composition root, and the adapters of the ports arrive with the persistence (WP1.8), the cache (WP1.9), and the endpoints (WP1.10). The first pull request of WP1.7 delivered the results, the errors, the permission table, and the ports, and the second delivered the pipeline. The use cases (third pull request) are planned.

## Handler interfaces

A use case has one handler behind one interface, and there is no mediator and no dispatch by type lookup.

| Interface | Purpose |
| --- | --- |
| `ICommandHandler<TCommand, TResult>` | Handles a command that changes the catalog and returns `Task<Result<TResult>>` |
| `IQueryHandler<TQuery, TResult>` | Handles a query that reads the catalog and returns `Task<Result<TResult>>` |
| `IProtectedRequest` | Declares the access level of a request with a static abstract member, so a handler cannot change it |
| `IIdempotentCommand` | Marks a command that carries an optional idempotency key |
| `ICachedQuery` | Marks a query whose successful result may be cached, and names its cache key and tags |

## Results and errors

A refusal that a caller can cause is a value. `Result<T>` holds a value or an `ApplicationError`, and `ProblemMapping` is the one table that maps each error to an entry of [the error catalog](error-catalog.md). A test compares every row with the catalog page, so a change to a status or a title fails until
both agree ([ADR-0028](../adr/0028-http-api-conventions-of-v1.md)).

| Error | When | Entry of the error catalog | Status |
| --- | --- | --- | --- |
| `ValidationFailed` | A field, a parameter, or a header is not valid, or the domain refused a rule on a field | `validation-failed` | 400 |
| `Forbidden` | The caller lacks the scope or the role of the access level | `forbidden` | 403 |
| `NotFound` | No product or category has the identifier | `not-found` | 404 |
| `Conflict` with `SkuAlreadyExists` | Another product has the same SKU | `sku-already-exists` | 409 |
| `Conflict` with `ProductDiscontinued` | A discontinued product would change its price or its category | `product-discontinued` | 409 |
| `Conflict` with `IdempotencyRequestInProgress` | The first request with the same key has not finished | `idempotency-request-in-progress` | 409 |
| `PreconditionFailed` | The `If-Match` value is not the current version | `precondition-failed` | 412 |
| `IdempotencyKeyReused` | The key was used with a different request | `idempotency-key-reused` | 422 |
| `PreconditionRequired` | The operation needs an `If-Match` value and the caller sent none | `precondition-required` | 428 |

The eight entries that the application never raises belong to the API: `malformed-request`, `idempotency-key-required`, `unauthorized`, `payload-too-large`, `unsupported-media-type`, `rate-limited`, `internal-error`, and `service-unavailable`. A rule that the domain model refuses
becomes an application error with `ApplicationError.From`, as [the domain model page](domain-model.md) lists.

## Permissions

Each request declares one access level. A caller needs the scope of the level and one of its roles, because Keycloak 26.8.0 grants a requested scope to a user who lacks the role that the scope maps to ([ADR-0026](../adr/0026-api-only-compose-stack-and-catalog-realm.md), risk R21 in
[the risk register](../project/risk-register.md)). `PermissionPolicy` holds the table, and the API binds the same table to its endpoint policies (WP1.10).

| Access level | Scope | Roles that grant it |
| --- | --- | --- |
| `Read` | `catalog.read` | `catalog-reader`, `catalog-editor`, `catalog-admin` |
| `Write` | `catalog.write` | `catalog-editor`, `catalog-admin` |
| `Admin` | `catalog.admin` | `catalog-admin` |

A caller without the scope gets `Forbidden` with the reason `MissingScope`, and a caller with the scope and without a role gets `MissingRole`. A test checks all 64 combinations of three scopes and three roles for each level, and another compares the scopes with the contract and the roles with
the realm file.

## Decorator order

Commands pass through telemetry and logging, validation, authorization, idempotency (when the command has a key), and the transaction before the handler. Queries pass through telemetry, validation, authorization, and the query cache (for marked queries) before the handler. The reason for each position is in
[ADR-0032](../adr/0032-decorator-pipeline-and-application-results.md).

The decorators are in [`src/Catalog.CrossCutting/Decorators`](../../src/Catalog.CrossCutting/Decorators), and `CommandPipeline.Compose` and `QueryPipeline.Compose` are the only places that name them, so the order is plain code. The composition root calls `Compose` once for each handler that a feature registers
(WP1.10, planned). A rule of `Catalog.Architecture.Tests` fails when a decorator names another decorator.

| Decorator | Used by | What it does |
| --- | --- | --- |
| `TelemetryCommandDecorator`, `TelemetryQueryDecorator` | Commands, queries | Observes the run through `IUseCaseTelemetry`, and reports a success, a refusal, or a fault |
| `ValidationCommandDecorator`, `ValidationQueryDecorator` | Commands, queries | Runs every FluentValidation validator and refuses with all the messages at once, with the fields written as JSON paths of the contract |
| `AuthorizationCommandDecorator`, `AuthorizationQueryDecorator` | Commands, queries | Checks the access level of the request against the permission table |
| `IdempotencyCommandDecorator` | Commands with a key | Reserves the key, replays the first result, refuses a different request with the same key, and registers the hook that writes the record in the transaction |
| `TransactionCommandDecorator` | Commands | Runs the handler in the unit of work, and wakes the outbox after a successful commit |
| `QueryCacheDecorator` | Queries that implement `ICachedQuery` | Serves the result from the cache and loads it on a miss |

## Ports

The application defines the ports, and a rule of `Catalog.Architecture.Tests` fails when an interface of the application declares more than five operations, so a caller depends only on what it uses.

| Port | Capability | Implemented by |
| --- | --- | --- |
| `IProductRepository`, `ICategoryRepository` | Load aggregates with their version, add new ones, and read the version after a save | Persistence (WP1.8, planned) |
| `IUnitOfWork` | Run the work of a command in one transaction, save inside it, and run hooks before the commit | Persistence (WP1.8, planned) |
| `IIdempotencyStore` | Reserve a key, record the first result in the transaction of the command, and release a failed reservation | Persistence (WP1.8, planned) |
| `IOutboxDispatcher` | Wake the dispatcher of the node after a commit, on a best-effort basis | Persistence (WP1.8, planned) |
| `ICatalogCache` | Return a cached result or load it, and remove the entries of a tag | Caching (WP1.9, planned) |
| `ICurrentPrincipal` | Give the caller, its scopes, and its roles | API (WP1.10, planned) |
| `IUseCaseTelemetry` | Observe a use case: trace, count, and log its outcome | Observability (WP1.11, planned) |
| `IIdGenerator` | Create the identifier of a new product or category | `GuidV7IdGenerator`, in this layer |

`GuidV7IdGenerator` calls `Guid.CreateVersion7` with the time of the injected `TimeProvider`, so identifiers sort by creation time, and a test passes a generator that counts. `EntityTag` is the version of a stored resource as a token that is safe in a URL, and `IdempotencyKey` is a key of 1 to 128 printable ASCII
characters. The read stores that the queries need are defined with the queries in the third pull request of WP1.7 (planned).

## How it is tested

Each test names the requirement that it proves, and `governance trace` reads the names ([the traceability report](../testing/traceability.md)).

- `tests/Catalog.Application.Tests` holds the unit tests of the results, the mapping table, the permission table, the tag, the key, the identifier generator, and every decorator. The permission test is exhaustive, and the tag test is a property test. `PipelineOrderTests` runs requests through the production chains over fakes that share one journal and asserts the order and every short circuit.
- `tests/Catalog.Contract.Tests/ApplicationAgreementTests.cs` holds the mapping table to the error catalog and the permission table to the contract and the realm file.
- `tests/Catalog.Architecture.Tests` holds the rule that keeps the ports small and the rule that only a pipeline names the decorators, each with a violating fixture that shows it failing.
