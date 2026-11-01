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
([ADR-0032](../adr/0032-decorator-pipeline-and-application-results.md)). The decorator code is in `Catalog.CrossCutting`, the registration lives in the composition root, and the adapters of the ports arrive with the persistence (WP1.8), the cache (WP1.9), and the endpoints (WP1.10). WP1.7 delivered the results, the errors, the permission table, and the ports (first pull request), the pipeline (second), and the use cases with their read stores, validators, and registration (third).

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

## Use cases

Each operation of [the API contract](api-contract.md) has one request and one handler. A request is a public record that declares its access level, and a handler is an `internal sealed` class that the feature registers. The views are plain records (identifiers as `Guid`, the price as an amount and a currency code, and the version as the token of an `EntityTag`), so the API maps them to the contract and the idempotency store keeps them as JSON.

| Operation | Request | Level | Refusals that the handler adds to the pipeline's |
| --- | --- | --- | --- |
| `listCategories` | `ListCategories` | Read | None |
| `createCategory` | `CreateCategory` | Admin | None |
| `getCategory` | `GetCategory` | Read | `NotFound` |
| `updateCategory` | `RenameCategory` | Admin | `PreconditionRequired`, `NotFound`, `PreconditionFailed` |
| `discontinueCategory` | `DiscontinueCategory` | Admin | `PreconditionRequired`, `NotFound`, `PreconditionFailed` |
| `listProducts` | `ListProducts` | Read | None |
| `createProduct` | `CreateProduct` | Write | A validation error on `categoryId` when the category does not exist or is discontinued, and `Conflict` for a SKU in use |
| `getProduct` | `GetProduct` | Read | `NotFound` |
| `updateProduct` | `UpdateProduct` | Write | `PreconditionRequired`, `NotFound`, `PreconditionFailed`, a validation error on `categoryId`, and `Conflict` for a discontinued product |
| `discontinueProduct` | `DiscontinueProduct` | Admin | `PreconditionRequired`, `NotFound`, `PreconditionFailed` |

- A command that changes a resource needs `If-Match`. The handler checks that the caller sent a version before it looks anything up, and then compares it with the version that the repository loaded. After the work, the handler calls `IUnitOfWork.SaveChangesAsync`, so the new version of the row is known inside the transaction.
- `GetProduct` and `ListProducts` implement `ICachedQuery`. A product has the key `product:{id}` and the tag `product:{id}`, and a list has the key `products:list:{hash}` and the tags `products:list` and `category:{id}`. The cache decorator uses them, and WP1.9 turns them into FusionCache entries.
- A list has a page size of 1 to 100 (20 by default) and an opaque, versioned cursor over a keyset position (`PageCursor`). A cursor that was made for another sort order is refused. `IProductReadStore` and `ICategoryReadStore` return one row more than the page size, and the handler turns the extra row into `NextCursor`.
- The validators are in `Catalog.CrossCutting.Validation`. Each runs the factories of the value objects of the domain model for the fields of its request and reports every failure, so the `errors` member names all invalid fields at once. A handler still refuses a broken rule when no validator runs.
- `AddCategories` and `AddProducts` register the handlers through `IFeatureRegistry`, in a fixed order. The composition root implements the registry (WP1.10).

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
| `IProductReadStore`, `ICategoryReadStore` | Read one view, or a page of views after a keyset position, without loading an aggregate | Persistence (WP1.8, planned) |

`GuidV7IdGenerator` calls `Guid.CreateVersion7` with the time of the injected `TimeProvider`, so identifiers sort by creation time, and a test passes a generator that counts. `EntityTag` is the version of a stored resource as a token that is safe in a URL, and `IdempotencyKey` is a key of 1 to 128 printable ASCII
characters.

## How it is tested

Each test names the requirement that it proves, and `governance trace` reads the names ([the traceability report](../testing/traceability.md)).

- `tests/Catalog.Application.Tests` holds the unit tests of the results, the mapping table, the permission table, the tag, the key, the identifier generator, the paging, every decorator, every validator, the registration, and the ten use cases. The permission test is exhaustive, and the tag and the cursor tests are property tests. `PipelineOrderTests` runs requests through the production chains over fakes that share one journal and asserts the order and every short circuit, and the use case tests run each handler through the same chain over an in-memory catalog that has versions and an outbox of events.
- `tests/Catalog.Contract.Tests/ApplicationAgreementTests.cs` holds the mapping table to the error catalog, the permission table to the contract and the realm file, and the access level of each request to the scope of its operation.
- `tests/Catalog.Architecture.Tests` holds the rules of the layer: ports stay small, only a pipeline names the decorators, handlers implement a handler interface, no pipeline code uses reflection, a query handler uses no write port, and a command handler uses no read store. Each has a violating fixture that shows it failing.
- The mutation score of the application tests is 96.92 percent (checked 2026-11-01), and [the evidence record](../testing/evidence/2026-11-01-application-mutation-baseline.md) explains each survivor.
