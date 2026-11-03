---
title: "ADR-0033: Map the Domain with EF Core configuration classes, split the read model from the write model, and ship the schema as forward-only migrations"
description: "Decide how the catalog aggregates reach SQL Server without persistence attributes in the Domain, how the query side reads without loading aggregates, how the unit of work turns concurrency and SKU failures into values, and how migrations, the bundle, and the idempotent script are built and applied."
status: accepted
date: 2026-11-02
decision-makers: ["@RostomOhannessian"]
supersedes: []
superseded-by: []
tools: [efcore, sqlclient, sql-server]
related-adrs: ["0004", "0012", "0020", "0028", "0029", "0032"]
evidence:
  - src/Catalog.Infrastructure/Persistence
  - tests/Catalog.Infrastructure.Tests
  - tests/Catalog.Architecture.Tests/MigrationPolicyTests.cs
  - docs/reference/persistence.md
---

# ADR-0033: Map the Domain with EF Core configuration classes, split the read model from the write model, and ship the schema as forward-only migrations

## Context and problem statement

Plan section 8.4 names the persistence of the catalog: the schema `catalog` with five tables, `rowversion` concurrency tokens, a filtered unique index on the SKU, keyset indexes, explicit decimal precision, a unit of work around one `DbContext` transaction, and migrations that CI turns into a bundle and an idempotent script. [ADR-0029](0029-domain-model-and-mutation-baseline.md) built the Domain with private constructors and value objects that make themselves through `Create`, and it said that WP1.8 maps the model with configuration classes and may need constructors that EF Core can call. [ADR-0032](0032-decorator-pipeline-and-application-results.md) defined the ports that this layer implements, including the promise that no database exception reaches a handler. Four questions are open:

- How do the aggregates reach SQL Server, when the Domain may carry no attribute and no framework type?
- How does the query side read a keyset page without loading aggregates?
- What does the unit of work return when two writers race, and when two products claim one SKU?
- How is the schema changed, reviewed, and applied, and who may apply it?

This ADR serves REQ-ARC-001, REQ-ARC-003, REQ-DATA-001, REQ-DATA-002, REQ-DATA-006, REQ-DOM-004, and REQ-DOM-005.

## Decision drivers

- The Domain stays on the base class library, and the architecture tests keep EF Core out of it (plan section 8.1).
- A stale write and a taken SKU are values that a caller can cause, so they are results and never exceptions (ADR-0032).
- The `ETag` is the row version, so the version of a row that a handler loaded and the version that the API writes are the same bytes (ADR-0028).
- The keyset queries compare and sort real columns, so the plan's indexes serve them.
- A change to the schema is reviewed as SQL, applied by one identity, and never applied by an application replica (plan sections 8.4 and 8.10, REQ-DATA-006).

## Considered options

1. Configuration classes over the Domain types, a second read context with plain rows, forward-only migrations, and a migrator identity (chosen).
2. A separate persistence model with a mapper, and a `Restore` factory on each aggregate.
3. Persistence attributes on the Domain types.
4. One context for reads and writes, with the aggregates as the read model.
5. Hand-written SQL for every query, and a schema script that is not generated from the model.

## Decision outcome

Chosen option: **configuration classes over the Domain types, a second read context, and forward-only migrations**, because it keeps the Domain free of the framework with the smallest change to the model, gives the query side real columns, and makes the migration a reviewed file that one identity applies.

### The mapping

- `CatalogDbContext` maps the two aggregates and the three tables of the outbox, its receipts, and the idempotency records. It sets the default schema `catalog`, so the migrations history table lives there too. One configuration class holds the mapping of each type (`Persistence/Mapping`).
- Every value object has a value converter that rebuilds the value through its `Create`, so a stored value goes through the same rules as a new one (`ValueConverters.cs`).
- The row version is a shadow property of type `rowversion` on both aggregate tables. The aggregates never see it, and the repositories read it to build the `EntityTag`. EF Core puts it in the `WHERE` clause of each update, so a write against a stale row changes nothing and EF Core reports a concurrency failure.
- A price is two columns, `PriceAmount decimal(14,2)` and `PriceCurrency char(3)`. The precision is the integer digits of the contract plus the decimal places of the widest accepted currency, computed from the Domain, so a change to a bound of the model is a change to the model that the snapshot test notices.
- A status is stored as its name, in a `varchar(16)` column, so a person who reads the table sees the state.
- The SKU is `varchar(32)`. The unique index `UX_Products_Sku` holds every product, whatever its status, because a discontinued product still owns its SKU (`SkuExistsAsync` says so). Plan section 8.4 and REQ-DATA-001 name a filtered index, and the filter is `[Sku] IS NOT NULL`. It excludes no row today, because the SKU is required. The filter is kept so that the index matches the plan, and a later rule that needs a real filter changes it in a migration.
- The keyset indexes are `IX_Products_CreatedAt_Id` (`CreatedAt` descending, then `Id`), `IX_Products_Name_Id`, and `IX_Categories_Name_Id`. A foreign key from a product to its category never cascades, because a category is discontinued and not removed.
- There is no execution strategy with retries. A retrying strategy and a user transaction do not mix without wrapping the whole unit in the strategy, and the idempotency decorator runs outside the transaction. The first retry policy is a decision for WP1.10 and WP1.11, with the failure data in hand.

### The changes to the Domain

The model needs three small changes, and no rule of it changes. ADR-0029 said that this is the moment to make them here first.

| Change | Why |
| --- | --- |
| The private constructors of `Category` and `Product` take every stored value by the name of its property (`status`, `createdAt`, `updatedAt`, and the others), and `Create` passes the initial values | EF Core binds a constructor parameter to a property of the same name, so it can load a stored aggregate without a parameterless constructor and without reflection in the Domain |
| `Product` leaves the price out of its constructor and `Create` sets it | EF Core cannot bind a constructor parameter to a complex property (found 2026-11-02, EF Core 10.0.14). It sets the price after the call, and the constructor holds a placeholder that never survives a load |
| The private constructor of `Money` gives the amount the decimal places of its currency | EF Core loads `Money` through that constructor, and the column keeps two places for every currency, so a yen price would otherwise load as `1500.00` and print differently from a new one |
| `IHasDomainEvents` is the non-generic face of `AggregateRoot<TId>` | The outbox interceptor (WP1.8) reads and clears the events of every tracked aggregate without knowing its identifier type |

### The read model

`CatalogReadContext` maps the same two tables as plain rows with primitive identifiers (`ProductRow`, `CategoryRow`), never tracks, and has no migrations. The two read stores turn the rows into the views of the Application and build the keyset predicates: the sort key, then the identifier, after a position. A test compares the columns of the read model with the write model, so they cannot drift. A cursor whose sort key is not one that the store would have made returns no rows, because only a caller that changed a cursor by hand can send one.

SQL Server orders a `uniqueidentifier` by its own rule, the last six bytes first, so the tie order of a page is not the order of `Guid.CompareTo`. The sort and the keyset predicate both run in SQL Server, so they agree and no row is skipped or repeated. The contract promises a stable tie-break by identifier and never a particular order, and the identifiers are opaque to a client.

### The unit of work

`EfUnitOfWork` opens one transaction on the context of the scope. A successful run saves the changes (which the outbox interceptor of the next pull request turns into outbox rows in the same transaction), runs the hooks, and commits. A refusal rolls back and returns the value. A failed save becomes a value in two cases only: a `DbUpdateConcurrencyException` is `PreconditionFailed`, and a violation of the unique index `UX_Products_Sku` (error 2601 or 2627, by the index name in the message) is `Conflict(SkuAlreadyExists)`. A duplicate primary key, a missing category, and every other failure roll back and surface as exceptions, because a caller cannot cause them with an ordinary request. After a failure the tracker is cleared, so the scope can read the current version again. A run does not nest.

### Migrations

- The migration `InitialCreate` and the snapshot are generated by the `dotnet-ef` tool, which is a pinned local tool (`.config/dotnet-tools.json`). They live in `src/Catalog.Infrastructure/Persistence/Migrations`, which CODEOWNERS covers (`/src/**/Migrations/`), so each change to the schema is reviewed by the owner and is committed apart from the code that needs it. The phase plan lets the initial migration change until the phase exits.
- Migrations are forward-only. EF Core writes a `Down` method, and no deployment calls it, because an application rollback never rolls the schema back (plan section 8.10).
- The bundle and the idempotent script are generated and never checked in. `docs/reference/persistence.md` gives the two commands. A test applies the script twice to an empty database, compares its schema with the one that the migrations build, and generates it twice to show that the text is the same.
- Only the migrator identity changes the schema. No production assembly calls `Migrate`, `EnsureCreated`, or `EnsureDeleted`, and an architecture test reads the member references of each built assembly to hold the rule. An integration test shows that a login with data rights cannot create the schema or alter a table, and that a login with the rights of the migrator can migrate.
- The generated files are marked `generated_code` in `.editorconfig`, so the style rules do not apply to code that the tool rewrites, and the coverage thresholds already exclude them.

### Dependency intake (ADR-0020)

| Package | Version | License | Note |
| --- | --- | --- | --- |
| `Microsoft.EntityFrameworkCore.SqlServer` | 10.0.14 | MIT | The provider. It depends on SqlClient 6.1.7 |
| `Microsoft.EntityFrameworkCore.Design` | 10.0.14 | MIT | For the `dotnet-ef` tool only (`PrivateAssets="all"`) |
| `Microsoft.Data.SqlClient` | 7.1.1 | MIT | Pinned above the provider's 6.1.7, because plan section 17 and spikes 1.d and 1.e use 7.1.1 |
| `System.Security.Cryptography.Pkcs` | 10.0.14 | MIT | A transitive pin. SqlClient 7.1.1 depends on 9.0.18, which has advisories GHSA-2fmv-rv2c-59x7 and GHSA-846f-f764-76mx (patched in 9.0.21 and 10.0.13) |
| `dotnet-ef` | 10.0.14 | MIT | A local tool |

The versions were checked on 2026-11-02 against nuget.org. NuGet audit failed the restore on the Pkcs advisories, and the pin is the fix that ADR-0020 prescribes. The tool pages, the inventory, and the dependency catalog carry the same rows.

**Resolution of the open finding (2026-11-03).** The maintainer chose option 1, and the amendment of 2026-11-03 to [ADR-0004](0004-licensing-and-dependency-license-policy.md#amendment-of-2026-11-03-a-native-windows-runtime-that-no-shipped-artifact-contains) records it: a second kind of license exception, `windows-native-runtime`, covers `Microsoft.Data.SqlClient.SNI.runtime` 7.1.0 and nothing else, because no shipped artifact contains the package (the Phase 2 images are Linux-only, and a publish for a Linux runtime identifier does not copy it). The license gate, `DependencyPolicyTests`, and the `license-policy` rule of `governance security` pass on this branch with that exception, and a new version of the package fails all three until someone reads its terms. Options 2 and 3 were not taken. The conditions that end the exception, and which checks are enforced today and which are planned in WP2.1, WP2.3, and WP2.4, are in ADR-0004.

The finding that led to it: the license gate (`dotnet nuget-license ... -err`) failed on `Microsoft.Data.SqlClient.SNI.runtime` 7.1.0, a transitive dependency of SqlClient 7.1.1, and of the SqlClient that the EF Core provider pulls in by itself. The package ships "Microsoft Software License Terms" for a native Windows library (`runtimes/win-*`) in a license file that the tool cannot map to an SPDX identifier, and ADR-0004 treats it as a non-OSI linked dependency for a Windows run.

### Consequences

- Good: the Domain has no attribute, no framework type, and no reflection, and the model changed by three constructors and one interface.
- Good: a stored value passes the rules of the model when it loads, so a row that was changed by hand fails loudly and does not produce an invalid aggregate that looks valid.
- Good: the query side reads real columns with the keyset predicates that the plan's indexes serve, and it never loads an aggregate.
- Good: the ETag, the version of a loaded row, and the version after a save are one value, which a test proves.
- Bad: the read model maps the two tables a second time. Mitigation: the drift test fails when the columns, the types, or the nullability differ.
- Bad: a constructor of an aggregate now has the shape that the mapping needs, and a rename of a property needs a rename of its parameter. Mitigation: the round-trip tests fail at once.
- Bad: the filter of the SKU index is a no-op. Mitigation: the decision above names it, and the test pins the definition.
- Neutral: the price placeholder in the constructor of `Product` is a rule of construction and never of the model, because `Create` sets the price and EF Core sets it on a load.

### Confirmation

- `tests/Catalog.Infrastructure.Tests` runs against a real SQL Server 2022 container (ADR-0034): `SchemaTests` (REQ-DATA-001), `AggregateRoundTripTests` (REQ-DOM-004, REQ-DOM-005), `RepositoryTests`, `UnitOfWorkTests` (REQ-DATA-002), `ReadStoreTests`, and `MigrationTests` (REQ-DATA-006).
- `tests/Catalog.Architecture.Tests/MigrationPolicyTests.cs` holds the rule that no production assembly migrates.
- The Domain tests, the contract agreement tests, and the Application tests still pass, which shows that the rules of the model did not change.

## Pros and cons of the options

### Configuration classes over the Domain types, a second read context, and forward-only migrations

- Good: the smallest change to the model, one mapping per type, and real columns for the keyset queries.
- Bad: three constructors take the shape of the mapping, and the read model is a second mapping.

### A separate persistence model with a mapper, and a `Restore` factory on each aggregate

- Good: the Domain constructors keep their shape, and the persistence model can change without the aggregate.
- Bad: every aggregate gets a public way to build itself from stored values, which any code can call and which skips the rules, and the mapper doubles the code that has to agree with the tables.

### Persistence attributes on the Domain types

- Good: the least code.
- Bad: the Domain references EF Core, which plan section 8.1 and the architecture tests forbid.

### One context for reads and writes, with the aggregates as the read model

- Good: one mapping.
- Bad: a page of products would load aggregates with their tracking, and a keyset predicate on a converted identifier cannot be written in LINQ.

### Hand-written SQL for every query and the schema

- Good: full control of every statement.
- Bad: the schema and the code have no common source, and every change to a column is a change in two places that nothing compares.

## Revisit when

- EF Core binds a constructor parameter to a complex property, so the placeholder price can leave the constructor of `Product`.
- A second currency with more than two decimal places is accepted, which changes the precision of the price column through a migration.
- The first measured failure of a connection under load shows that an execution strategy with retries is needed.
- A rule needs a real filter on the SKU index.

## Further reading

- EF Core, creating and configuring a model: [https://learn.microsoft.com/en-us/ef/core/modeling/](https://learn.microsoft.com/en-us/ef/core/modeling/) (checked 2026-11-02)
- EF Core, migrations overview and bundles: [https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying) (checked 2026-11-02)
- Plan section 8.4: [../plans/implementation-plan.md](../plans/implementation-plan.md) (checked 2026-11-02)
- Persistence reference: [../reference/persistence.md](../reference/persistence.md)
