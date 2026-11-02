---
title: "Runbook: Apply a database migration"
description: "Review the idempotent script, apply the migrations with the bundle under the migrator identity, verify the schema, and know the way back: the schema never rolls back, and data recovery comes from a backup."
audience: [maintainers, operators]
tools: [efcore, sql-server]
introduced: "phase-1"
last-verified: 2026-11-02
owner: "@RostomOhannessian"
---

# Runbook: Apply a database migration

## When to use this

Use it when a pull request adds a migration under `src/Catalog.Infrastructure/Persistence/Migrations`, and when you bring a local database to the current schema. Only the migrator identity changes the schema, and an application replica never migrates ([ADR-0033](../adr/0033-ef-core-mapping-read-model-and-migrations.md)). The one-shot Kubernetes job that runs the bundle arrives in Phase 3 and Phase 4 (WP3.8, WP4.4), and this page is the procedure that the job automates.

## Impact and risk

- **Blast radius:** the catalog database. A migration changes the schema that every replica of the API uses, and the previous release of the API must still work against it (expand and contract, plan section 8.10).
- **Reversible:** no. The schema never rolls back, because `Down` is never run in a deployment. The way back is described under "Roll back".
- **Phase of the current migrations:** `InitialCreate` is an **expand** step: it creates the schema `catalog` on an empty database and drops nothing. `CaseSensitiveIdempotencyKeys` changes the collation of the two key columns of `IdempotencyRecords`. It drops the primary key of that table and creates it again, and it keeps every row, so it is an expand step for a table that holds only records of the last 24 hours. A stricter comparison cannot make two existing keys collide.
- **Needs confirmation from:** the owner reviews each migration (CODEOWNERS covers `src/**/Migrations`). A destructive change needs the `migration:contract-phase` label, an ADR or a runbook entry, and the owner's review.

## Before you start

- The pinned tools: run `dotnet tool restore` from the repository root.
- A database that exists and is empty, or at the previous migration, and the connection string with the credential of the migrator identity. Never use the `sa` login outside a disposable local stack.
- A scratch copy of the repository for the bundle step, because the bundle restores for a runtime identifier and rewrites the lock files (see [the persistence page](../reference/persistence.md)).

## Procedure

1. Generate the idempotent script and read it. A reviewer reads this file, and it can run twice without an error.

   ```bash illustrative
   dotnet tool run dotnet-ef migrations script --idempotent --project src/Catalog.Infrastructure --startup-project src/Catalog.Infrastructure --context CatalogDbContext --output artifacts/catalog.sql
   ```

   Expected output: the file `artifacts/catalog.sql` exists. Look for `DROP`, `ALTER ... DROP`, a rename, a narrowing `ALTER COLUMN`, and a new `NOT NULL` column without a default, which are the destructive changes. If you see one, stop and follow the contract-phase rule above. The drop of the primary key of `IdempotencyRecords` in the second migration is a known step that is followed by `ADD CONSTRAINT`. If you apply the script by hand with `sqlcmd`, pass `-I`, because the script creates filtered indexes, which need `QUOTED_IDENTIFIER` on, and `sqlcmd` turns it off by default (error 1934).

2. Generate the migration bundle in the scratch copy.

   ```bash illustrative
   dotnet tool run dotnet-ef migrations bundle --project src/Catalog.Infrastructure --startup-project src/Catalog.Infrastructure --context CatalogDbContext --self-contained --output artifacts/efbundle.exe
   ```

   Expected output ends with `Done. Migrations Bundle:` and the path.

3. Run the bundle with the connection string of the migrator identity.

   ```bash illustrative
   ./artifacts/efbundle.exe --connection "Server=<host>,<port>;Database=catalog;User Id=<migrator>;Password=<password>;Encrypt=true"
   ```

   Expected output for an empty database (a database at the first migration prints only the second line):

   ```text
   Applying migration '20261102045650_InitialCreate'.
   Applying migration '20261102054823_CaseSensitiveIdempotencyKeys'.
   Done.
   ```

   Run it again to prove that it is safe to repeat. Expected output: `No migrations were applied. The database is already up to date.`

## Verify

- List the tables of the schema. Expect `__EFMigrationsHistory`, `Categories`, `IdempotencyRecords`, `OutboxMessages`, `OutboxReceipts`, and `Products`.

  ```bash illustrative
  sqlcmd -C -S <host>,<port> -U <migrator> -d catalog -Q "SELECT name FROM sys.tables WHERE schema_id = SCHEMA_ID('catalog') ORDER BY name"
  ```

- The tests prove the same facts against a real SQL Server: `MigrationTests` applies the script twice, compares its schema with the migrations, and shows that the application identity can neither create the schema nor alter a table.

## Roll back

The schema never rolls back, and an application rollback never touches it. The previous release of the API keeps working against the new schema because each migration is an expand step.

- **Local database (Phase 1):** it is disposable. Run `./dev compose reset`, which deletes the data, and apply the migrations again.
- **A database with data:** restore it from the last backup. The backup and restore runbook arrives in WP3.4 and WP3.10, and it is the last resort.
- **A bad migration that has not merged:** remove it with `dotnet tool run dotnet-ef migrations remove --project src/Catalog.Infrastructure --startup-project src/Catalog.Infrastructure --context CatalogDbContext`, and generate it again. The phase plan lets `InitialCreate` change until Phase 1 exits.

## After the procedure

- Record the migration name and the time in the pull request or the change record.
- Delete the scratch copy and the `artifacts` folder, which hold a generated bundle and no secret.
- If the change was a contract step, record the ADR or the runbook entry that the rule asks for.

## Rehearsal log

| Date | Who | Environment | Result | Changes made to this runbook |
| --- | --- | --- | --- | --- |
| 2026-11-02 | AI-paired session (WP1.8) | Windows host, a throwaway SQL Server 2022 container of the digest that compose.yaml pins, the `sa` login of that container | Pass: the bundle applied `InitialCreate`, a second run applied nothing, and the six tables existed | First version |
| 2026-11-02 | AI-paired session (WP1.8, outbox and idempotency) | The same throwaway container, the idempotent script applied with `sqlcmd -I` from a second throwaway database | Pass: the script applied both migrations, a second run changed nothing, `CallerId` and `Key` had the collation `Latin1_General_100_BIN2`, and `PK_IdempotencyRecords` existed. Without `-I` the run failed with error 1934. A plain `ALTER COLUMN` of a key column failed with error 5074, which is why the second migration drops the key. The bundle was not rebuilt for the second migration, which `MigrationTests` applies through the same `Migrate` call | Added the `-I` note, the second migration, and the expected output of an empty database |
