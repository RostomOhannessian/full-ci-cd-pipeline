---
title: "Persistence"
description: "The tables, columns, and indexes of the catalog schema, how the aggregates map to them, how the repositories, the unit of work, and the read stores use them, and how the migrations, the bundle, and the idempotent script are built and applied."
audience: [learners, contributors, maintainers]
type: reference
tools: [efcore, sqlclient, sql-server, testcontainers-dotnet]
last-verified: 2026-11-02
owner: "@RostomOhannessian"
---

# Persistence

The persistence layer is the project [`src/Catalog.Infrastructure`](../../src/Catalog.Infrastructure/Catalog.Infrastructure.csproj). It implements the ports that the application layer defines, on SQL Server through EF Core ([ADR-0033](../adr/0033-ef-core-mapping-read-model-and-migrations.md)). It references the application layer only, and the Domain stays free of every attribute and framework type. This page describes what exists today. The credential sources are planned in a later pull request of WP1.8, and the section that would describe them is not here yet.

## The schema

Every table is in the schema `catalog`, and so is the history of the migrations.

| Table | Holds | Key | Concurrency |
| --- | --- | --- | --- |
| `Categories` | The category aggregate | `Id` (a UUID version 7) | `RowVersion` (`rowversion`) |
| `Products` | The product aggregate, with the price as two columns | `Id` (a UUID version 7) | `RowVersion` (`rowversion`) |
| `OutboxMessages` | One domain event that waits for delivery, with its lease, attempts, and state | `Id` | None |
| `OutboxReceipts` | The proof that a handler took a message | `MessageId` and `Handler` | None |
| `IdempotencyRecords` | One request with an idempotency key, for 24 hours | `CallerId` and `Key`, both compared exactly (binary collation) | None |

### Columns of the aggregates

| Column | Type | Rule of the model |
| --- | --- | --- |
| `Products.Sku` | `varchar(32)` | Three to 32 ASCII letters and digits with single hyphens, in capital letters |
| `Products.Name` | `nvarchar(200)` | One to 200 characters, trimmed |
| `Products.Description` | `nvarchar(2000)`, null | Up to 2000 characters, or none |
| `Products.PriceAmount` | `decimal(14,2)` | Twelve integer digits and the decimal places of the widest currency, computed from the Domain |
| `Products.PriceCurrency` | `char(3)` | The ISO 4217 code, from the accepted set |
| `Products.CategoryId` | `uniqueidentifier` | A foreign key to `Categories.Id` that never cascades |
| `Products.Status`, `Categories.Status` | `varchar(16)` | The name of the status, `Active` or `Discontinued` |
| `CreatedAt`, `UpdatedAt` | `datetimeoffset` | UTC, from the `TimeProvider` of the operation |
| `Categories.Name` | `nvarchar(100)` | One to 100 characters, trimmed |

### Indexes

| Index | Columns | Serves |
| --- | --- | --- |
| `UX_Products_Sku` | `Sku`, unique, filter `[Sku] IS NOT NULL` | The rule that no two products share a SKU. It holds every product, whatever its status. The filter excludes no row today, because the SKU is required |
| `IX_Products_CreatedAt_Id` | `CreatedAt` descending, `Id` | The newest-first list of products |
| `IX_Products_Name_Id` | `Name`, `Id` | The list of products by name |
| `IX_Categories_Name_Id` | `Name`, `Id` | The list of categories |
| `IX_Products_CategoryId` | `CategoryId` | The foreign key |
| `IX_OutboxMessages_Pending` | `NextAttemptAt`, `Id`, filter `[Status] = 'Pending'` | The lease query of the processor |
| `IX_OutboxMessages_CompletedAt` | `CompletedAt`, filter `[Status] = 'Done'` | The cleanup of finished messages |
| `IX_IdempotencyRecords_ExpiresAt` | `ExpiresAt` | The cleanup of expired records |

SQL Server orders a `uniqueidentifier` by its own rule, the last six bytes first. A page breaks a tie of the sort key by identifier in that order, and the keyset predicate runs in SQL Server too, so no row is skipped or repeated.

## The mapping

`CatalogDbContext` is the write model, and the classes in `Persistence/Mapping` hold the mapping of each type.

- A value converter rebuilds each value object through its `Create`, so a value that a row holds passes the rules of the model when it loads.
- The row version is a shadow property, which the aggregates never see. The repositories read it to build the `EntityTag`, which is the base64url text of the eight bytes.
- EF Core loads an aggregate through its private constructor, whose parameters have the names of properties. The price is the one value that it sets after the constructor ran (ADR-0033).
- `CatalogReadContext` is the read model. It maps `Products` and `Categories` as plain rows (`ProductRow`, `CategoryRow`), never tracks, and has no migrations. A test compares its columns with those of the write model.

## The adapters of the ports

| Port | Class | Behavior |
| --- | --- | --- |
| `IProductRepository` | `ProductRepository` | Loads a product with tracking and its version, checks whether a SKU exists whatever the status, adds a product, and gives the version as of the last save |
| `ICategoryRepository` | `CategoryRepository` | The same for categories |
| `IUnitOfWork` | `EfUnitOfWork` | Runs the work in one transaction, saves, runs the hooks, and commits. A stale write is `PreconditionFailed`, a taken SKU is `Conflict(SkuAlreadyExists)`, and every other failure rolls back and surfaces as an exception |
| `IProductReadStore`, `ICategoryReadStore` | `ProductReadStore`, `CategoryReadStore` | Read a view, or a keyset page, from the read model. A cursor that a caller changed by hand returns no rows |
| `IIdempotencyStore` | `IdempotencyStore` | Reserves a key, records the first result in the transaction of the command, and releases a failed reservation ([ADR-0036](../adr/0036-idempotency-store.md)) |
| `IOutboxDispatcher` | `OutboxDispatcher` | Wakes the processor of the node after a commit ([ADR-0035](../adr/0035-outbox-processor-design.md)) |
| `IOutboxMessageHandler` | Implemented by the handlers of the follow-on work, which arrive with WP1.9 | Takes one message of the outbox, and is idempotent |

## The outbox

A `SaveChangesInterceptor` (`OutboxInterceptor`) turns each recorded domain event of the tracked aggregates into a row of `OutboxMessages` in the same transaction as the change, and clears the events after the save. The payload is hand-written JSON that starts with `eventType`, `schemaVersion`, and `occurredAt`, and a snapshot test holds each of the eight shapes. A rollback leaves no row, and a failed save keeps the events on the aggregate.

`OutboxProcessor` delivers the rows at least once, and `OutboxBackgroundService` runs it in a loop ([ADR-0035](../adr/0035-outbox-processor-design.md)):

| Column | Meaning |
| --- | --- |
| `Status` | `Pending`, `Done`, or `DeadLetter` |
| `Attempts` | The number of deliveries that were started, counted when the message is leased |
| `NextAttemptAt` | When the message is due. A failure moves it by an exponential backoff with jitter |
| `LeasedUntil`, `LeaseId` | The lease that one processor holds. Only the holder of the `LeaseId` can finish the message |
| `CompletedAt` | When every handler took the message |
| `LastError` | The name of the type of the last fault, never its message |

- **Lease.** One statement takes the due rows with `UPDLOCK, READPAST, ROWLOCK`, so two processors take different rows and neither waits. A lease that ends makes the row due again.
- **Receipts.** After a handler returns, a row `(MessageId, Handler)` records it, and a retry skips the handlers that have a receipt.
- **Dead letter.** After `MaxAttempts` (8) failures the message becomes `DeadLetter`, the counter `catalog.outbox.dead_lettered` on the meter `Catalog.Outbox` goes up, and an error is logged. The alert that watches the counter is planned in WP1.11.
- **Wake-up.** The dispatcher wakes the loop after a commit, and the loop polls every 5 seconds in any case.
- **Cleanup.** Finished messages older than 7 days and expired idempotency records are deleted in batches. A dead-letter message stays.

The settings are in `OutboxOptions` (batch size, lease, attempts, backoff, jitter, poll, retention), and the registration validates them at startup.

## The idempotency store

`IdempotencyStore` implements the three operations of the port ([ADR-0036](../adr/0036-idempotency-store.md)):

| Operation | Runs | What it does |
| --- | --- | --- |
| `BeginAsync` | Outside the transaction | Inserts a reservation, or reads the record that exists, and answers `Reserved`, `Replay` (with the first result), `InProgress`, or `Reused` (another payload). It takes over a reservation older than 2 minutes |
| `CompleteAsync` | In the transaction of the command, through `IUnitOfWork.BeforeCommit` | Sets the record to `Completed` with the response, and throws, rolling the effect back, when the reservation is gone |
| `ReleaseAsync` | Outside the transaction | Deletes the reservation of a request that failed |

A record lives 24 hours. The key and the caller are compared exactly, so `Abc` and `abc` are two keys.

## Migrations, the bundle, and the idempotent script

The migrations `InitialCreate` and `CaseSensitiveIdempotencyKeys` and the snapshot of the model are in `src/Catalog.Infrastructure/Persistence/Migrations`. The `dotnet-ef` tool writes them, and CODEOWNERS asks for the review of the owner on each change, so a migration is committed apart from the code that needs it. The second migration is edited by hand after the generator, because SQL Server refuses to change the collation of a column that the primary key uses, so it drops the key and creates it again. Migrations are forward-only: EF Core writes a `Down` method, and no deployment calls it, because an application rollback never rolls the schema back (plan section 8.10).

Three commands build what a reviewer or a migrator needs. None of the outputs is checked in, because each is generated again from the model. Run them from the repository root after `dotnet tool restore`.

Add a migration after a change to a mapping:

```bash illustrative
dotnet tool run dotnet-ef migrations add <Name> --project src/Catalog.Infrastructure --startup-project src/Catalog.Infrastructure --context CatalogDbContext --output-dir Persistence/Migrations
```

Generate the idempotent script, which a reviewer reads and which can run again:

```bash illustrative
dotnet tool run dotnet-ef migrations script --idempotent --project src/Catalog.Infrastructure --startup-project src/Catalog.Infrastructure --context CatalogDbContext --output artifacts/catalog.sql
```

Generate the migration bundle, which the migrator runs with the connection string of the database to migrate:

```bash illustrative
dotnet tool run dotnet-ef migrations bundle --project src/Catalog.Infrastructure --startup-project src/Catalog.Infrastructure --context CatalogDbContext --self-contained --output artifacts/efbundle
```

The design-time factory holds a connection string with no credential, because none of the commands opens a connection. Run the bundle with `--connection` and the credential of the migrator identity.

The bundle is published for a runtime identifier, so its restore rewrites the lock files of the projects that it builds, and a locked restore (`CI=true`) refuses it with NU1004 (found 2026-11-02). Run the bundle command in a scratch copy of the repository, or restore the lock files afterward with `git checkout -- '*packages.lock.json'`. The CI job that builds the bundle (planned, Phase 2 and Phase 3) works in a scratch copy with the locked mode off. The script needs no restore beyond the one that the tool does, and it leaves the lock files alone.

Only the migrator identity changes the schema. An application replica never calls `Migrate`, and `tests/Catalog.Architecture.Tests/MigrationPolicyTests.cs` fails when a production assembly does. The one-shot job that runs the bundle in Kubernetes arrives in Phase 3 (WP3.8) and Phase 4 (WP4.4). Until then, a local database is disposable (`dev compose reset`), and the initial migration may change until the phase exits.

## How it is tested

Each test names the requirement that it proves, and `governance trace` reads the names ([the traceability report](../testing/traceability.md)). The suites run against a real SQL Server 2022 container that one assembly fixture starts ([ADR-0034](../adr/0034-integration-test-database-strategy-and-linux-run.md)).

- `SchemaTests` reads the schema from the database: the tables, the `rowversion` columns, the filtered unique index, the keyset indexes, the precision, the foreign key, the snapshot, and the match between the read model and the write model (REQ-DATA-001).
- `AggregateRoundTripTests` saves and loads both aggregates, with every value, a missing description, a yen price, and the discontinued status (REQ-DATA-001, REQ-DOM-004, REQ-DOM-005).
- `RepositoryTests` and `UnitOfWorkTests` cover loading, the SKU check, the version, the commit, the refusal, the fault, the stale writer, the taken SKU, the hooks, and the nesting rule (REQ-DATA-001, REQ-DATA-002, REQ-ARC-003).
- `ReadStoreTests` pages through 24 products by both sort orders with ties, adds a row between two pages, and checks the filters (REQ-DATA-001).
- `MigrationTests` applies the idempotent script twice, compares its schema (columns, collations, and indexes) with the migrations, generates it twice, applies the collation migration to a database that has a row, and proves that only the migrator identity changes the schema (REQ-DATA-006).
- `OutboxInterceptorTests`, `OutboxPayloadTests`, `OutboxProcessorTests`, `OutboxBackgroundServiceTests`, `OutboxSignalTests`, `OutboxBackoffTests`, and `PersistenceOptionsTests` cover the outbox: one row for each event in the same transaction, the eight payload snapshots, the lease, the fence, the receipts, the backoff, the dead-letter state, the stop after delivery, the death of a processor, concurrent processors, a locked row, the loop, and the cleanup (REQ-DATA-002, REQ-DATA-003, REQ-DOM-004). No test sleeps: a fake clock ends each lease and backoff.
- `IdempotencyStoreTests` covers the reservation, the replay, the reused key, the request in progress, the exact comparison, the release, the expiry, the takeover, eight concurrent requests, the commit and the rollback with the effect, and the cleanup (REQ-DATA-004).
- `tests/Catalog.Architecture.Tests` holds the layer rules and the rule that no production assembly migrates.
