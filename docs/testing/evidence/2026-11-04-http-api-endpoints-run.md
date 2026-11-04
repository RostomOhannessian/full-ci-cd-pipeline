---
title: "Evidence record: the endpoints of the HTTP API (2026-11-04)"
description: "A dated record of the second pull request of WP1.10 on Windows with Docker Desktop and in the pinned Linux SDK image: what ran where, the counts, what the tests showed about the ten operations, the headers, the conditional requests, the idempotent creates, the pages, the strict reading of bodies, and the version headers, and what the runs did not show."
type: evidence-record
date: 2026-11-04
status: verified
audience: [maintainers, contributors]
last-verified: 2026-11-04
owner: "@RostomOhannessian"
---

# Evidence record: the endpoints of the HTTP API (2026-11-04)

This record is the evidence for the second pull request of WP1.10 (HTTP API): the ten operations of the version 1 contract under `/api/v1`, the version segment, the conditional requests, the idempotent creates, the pages, and the strict reading of bodies ([ADR-0041](../../adr/0041-http-endpoints-versioning-and-conditional-requests.md), [the HTTP API](../../reference/http-api.md)).
It continues [the record of the host](2026-11-03-http-api-host-run.md). It says what ran where, so a reader can tell which host proved which claim. The check that the served document equals the contract, the snapshots, the authorization matrix over the endpoints, and the realm tests are the third pull request, so this record says nothing about them.

## What ran where

| Where | Command | What it ran | Result |
| --- | --- | --- | --- |
| Windows x86-64, Docker Desktop, .NET SDK 10.0.403 | `dotnet test --solution ProductCatalog.slnx --configuration Release --no-build --report-trx` | Every test project natively. The endpoint tests start the real composition in process, with an in-process identity provider, a SQL Server 2022 container, and a Valkey 8.1 container that assembly fixtures start from the digests of `compose.yaml` | 2,099 tests ran, 2,099 passed, 0 failed, 0 skipped. By project: Api 246, Application 269, Architecture 147, Contract 103, Domain 173, Infrastructure 272, Documentation auditor 265, Governance auditor 624. Smart App Control did not block a test assembly in this run |
| Linux, the pinned SDK 10.0.401 image, Docker socket mounted | `docker compose -f tools/lint/compose.yaml run --rm linux-tests` | A scratch copy of the repository: locked restore, Release build with warnings as errors, every test project with the code coverage extension, and the coverage thresholds | 2,099 tests ran, 2,099 passed, 0 failed, 0 skipped. The first Linux runs of the day failed, and each failure was a real finding: the documentation auditor wanted the section `## Asp.Versioning` for the inventory entry that is now in use, and a test of the error catalog page read the words that the page lost. The coverage gate passed: Catalog.Application 99.54% of lines and 99.58% of branches, Catalog.Domain 99.63% of lines and 98.41% of branches, and the solution 97.08% of lines (3,062 of 3,154) |

## What the tests showed

| Question | Answer | Test |
| --- | --- | --- |
| Does a create answer as the contract says? | 201 with the absolute `Location`, a strong `ETag`, the body with the SKU in upper case, a trimmed name, a `null` description, the price in the decimal places of its currency, and every instant in UTC with a `Z`. The `Location` reads the same resource | `CategoryEndpointTests`, `ProductEndpointTests` |
| Does a price keep the decimal places of its currency? | `1990` for the yen, and `5.00`, `0.00`, and `12.50` for the dollar, the euro, and the pound, in the create and in the next read | `ProductEndpointTests` |
| What does the same idempotency key do? | The same key and the same body replay the first answer (the same id, `ETag`, `Location`, and creation time) and create one resource. Another body is 422 and creates nothing. A key whose first request still runs is 409. A missing, unquoted, empty, too long, or doubled key is 400 `idempotency-key-required` | `CategoryEndpointTests`, `ProductEndpointTests` |
| What does a precondition do? | The current `ETag` in `If-None-Match` is 304 with no body and the `ETag`. A change or a discontinue without `If-Match` is 428 and changes nothing, a stale one is 412, and the old tag is stale after a change. A weak tag, a list, `*`, and an unquoted or malformed tag are 400 for the header | `CategoryEndpointTests`, `ProductEndpointTests` |
| Does a change show at once? | Yes. A read after a change returns the new tag and the new price, although a cached read came first, because the commit invalidates the cache in process | `ProductEndpointTests` |
| Is a discontinue idempotent? | Yes. The second action returns 200 with the same representation and the same tag, and a price change of a discontinued product is 409 `product-discontinued` | `CategoryEndpointTests`, `ProductEndpointTests` |
| What do the rules of the model look like over HTTP? | A SKU in use is 409 in any case, a category that does not exist or is discontinued is a validation error for `categoryId`, and an amount or a currency that is not valid is a validation error for `price.amount` or `price.currency` | `ProductEndpointTests` |
| Do the filters and the sorts work? | A product list narrows by category, status, and text in the name or the SKU, and sorts by creation time (newest first) or by name. A category list narrows by status | `ProductEndpointTests`, `CategoryEndpointTests` |
| Do the pages follow the `Link` header to the end? | Yes. Five categories in pages of two, and five products in pages of two with every filter and the sort in the address, arrive once each and in order, and the last page has no `Link` and a `null` cursor. A cursor of one sort is refused for another | `CategoryEndpointTests`, `ProductEndpointTests` |
| What does a parameter that is not valid do? | A page size outside 1 to 100 or not a number, a cursor that is not valid, a status or a sort that the contract does not know, an identifier that is not a UUID, and a parameter that appears twice are each a validation error that names the parameter | `CategoryEndpointTests`, `ProductEndpointTests` |
| What does a body that is not allowed do? | A media type other than `application/json` or a charset other than `utf-8` is 415. A body of one byte more than 32 KiB is 413, announced or streamed, and a body of exactly the limit is read. A body nested 9 levels is 400 `malformed-request`, and one of 8 levels is read. A body that is not JSON, is empty, or holds two documents is 400 `malformed-request`. A root that is not an object is a validation error for `body`. Every unknown member is listed with its path, case matters, and a value of the wrong type is named | `RequestBodyTests` |
| Are the scope and the key checked before the body? | Yes. A caller without the scope gets 403 for any body, and a create without a key gets 400 `idempotency-key-required` before the body is read | `RequestBodyTests` |
| Does every answer list the versions? | A 200, a 201, a 304, a 401, a 403, and a 400 of a versioned route carry `api-supported-versions: 1.0`, and none carries a deprecation header. A path without a version is a 404, and an unknown version is refused, both as `application/problem+json` | `VersioningTests`, `CategoryEndpointTests` |
| Are the endpoints thin? | The architecture rule fails on the violating fixture when a type of `Catalog.Api.Endpoints` depends on the domain model, Infrastructure, or CrossCutting, and it passes on the ten endpoints | `ArchitectureRulesTests` |
| Does the read store keep the two filters? | A category status keeps only the categories that have it, and the product search matches the SKU in any case | `ReadStoreTests`, `CategoryUseCaseTests` |

## Findings

- The use cases lacked the category status filter and the SKU in the search, although the contract and the API contract page give both. The use cases changed, and the contract stayed as it is (ADR-0041).
- The JSON serializer writes a zero offset as `+00:00`, so a small converter writes every instant with a `Z`, as the contract shows it.
- Asp.Versioning reports the versions from the endpoint, so a 401 or a 403 carried none until `ApiVersionReportMiddleware` asked the same reporter.
- The source generator of regular expressions adds a type to the assembly, which the architecture test of foreign types refuses, so the shapes of the headers and the amount are checked character by character.
- The model accepts only a UUID of version 7, so a random identifier is a validation error and not a 404. The tests use `Guid.CreateVersion7()` for a missing resource.

## Limits

- The running request of the 409 case is a reservation that the test writes into the real store with the fingerprint that the pipeline would make, because a test cannot hold a request open without a sleep. The store tests of WP1.8 hold the race itself.
- `TestServer` has no Kestrel limit, which is why the reader counts the bytes itself. The limit of Kestrel was not exercised on a real socket in this pull request.
- The tests use the default user of the Valkey container, because the access control list was proven in WP1.9 ([the record of the adapter](2026-11-03-cache-adapter-run.md)).
- Timings are single samples from one machine. The creation times of the tests come from a fake clock that moves on by a second before each create.
- No hosted run existed for this branch when this record was written. The `ci` workflow runs the same suites on Linux without the Docker socket exception.
