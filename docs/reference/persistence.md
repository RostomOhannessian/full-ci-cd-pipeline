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

The persistence layer is the project [`src/Catalog.Infrastructure`](../../src/Catalog.Infrastructure/Catalog.Infrastructure.csproj). It implements the ports that the application layer defines, on SQL Server through EF Core ([ADR-0033](../adr/0033-ef-core-mapping-read-model-and-migrations.md)). It references the application layer only, and the Domain stays free of every attribute and framework type. This page describes what exists today. The outbox interceptor and processor, the idempotency store, and the credential sources are planned in the next pull requests of WP1.8, and the sections that describe them say so.

## The schema

Every table is in the schema `catalog`, and so is the history of the migrations.

| Table | Holds | Key | Concurrency |
| --- | --- | --- | --- |
| `Categories` | The category aggregate | `Id` (a UUID version 7) | `RowVersion` (`rowversion`) |
| `Products` | The product aggregate, with the price as two columns | `Id` (a UUID version 7) | `RowVersion` (`rowversion`) |
| `OutboxMessages` | One domain event that waits for delivery, with its lease, attempts, and state (planned logic, the table exists) | `Id` | None |
| `OutboxReceipts` | The proof that a handler took a message | `MessageId` and `Handler` | None |
| `IdempotencyRecords` | One request with an idempotency key, for 24 hours (planned logic, the table exists) | `CallerId` and `Key` | None |

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
| `IIdempotencyStore` | Planned in the second pull request of WP1.8 | Reserves a key, records the first result in the transaction of the command, and releases a failed reservation |
| `IOutboxDispatcher` | Planned in the second pull request of WP1.8 | Wakes the processor of the node after a commit |

## Migrations, the bundle, and the idempotent script

The migration `InitialCreate` and the snapshot of the model are in `src/Catalog.Infrastructure/Persistence/Migrations`. The `dotnet-ef` tool writes them, and CODEOWNERS asks for the review of the owner on each change, so a migration is committed apart from the code that needs it. Migrations are forward-only: EF Core writes a `Down` method, and no deployment calls it, because an application rollback never rolls the schema back (plan section 8.10).

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
- `MigrationTests` applies the idempotent script twice, compares its schema with the migrations, generates it twice, and proves that only the migrator identity changes the schema (REQ-DATA-006).
- `tests/Catalog.Architecture.Tests` holds the layer rules and the rule that no production assembly migrates.
