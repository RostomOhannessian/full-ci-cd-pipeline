---
title: "API style guide"
description: "The rules that every operation of the product catalog API follows: names, formats, status codes, versioning and deprecation, errors, and how to change the contract."
audience: [learners, contributors, maintainers]
type: reference
tools: [openapi, spectral]
last-verified: 2026-10-11
owner: "@RostomOhannessian"
---

# API style guide

This guide is for a person who designs or changes an operation of the API, and for a person who writes a client. It says what the rules are and why they exist, so that a new operation looks like the old ones. The decisions behind the rules are in
[ADR-0028](../adr/0028-http-api-conventions-of-v1.md), the operations are in [the API contract](api-contract.md), and the errors are in [the error catalog](error-catalog.md).

The contract is checked by [a linter](tools/spectral.md) that holds the structural rules below, so a rule that says "every" is a rule that a pull request cannot break by accident. A rule that the linter cannot check is marked as a convention, and a reviewer holds it.

## Names and addresses

- **Resources are plural nouns.** A collection is `/api/v1/products`, and one item is `/api/v1/products/{productId}`. The identifier is always the last segment, and it is always a UUID version 7.
- **Nothing is deleted.** A resource moves from `active` to `discontinued`, and the move is an action: `POST /api/v1/products/{productId}/discontinue`. An action is a `POST` to a sub-resource that names the transition with a verb, and it is the only place where a path holds a verb.
- **Every path starts with `/api/v1/`.** The version is a path segment (rule `catalog-paths-are-versioned`).
- **Operation IDs are lower camel case**, and they start with a verb that tells the method: `list`, `get`, `create`, `update`, or `discontinue` (rule `catalog-operation-ids-are-camel-case`). A generated client takes its method names from them.

## JSON

- **Property names are lower camel case**, such as `categoryId` and `createdAt`.
- **Timestamps are RFC 3339 in UTC**, with a trailing `Z`, such as `2026-10-11T12:00:00Z`.
- **Enumerations are lower case words**, such as `active` and `discontinued`. A currency is the exception, because it is an ISO 4217 code in capitals.
- **Money is an object with a decimal string.** `{ "amount": "89.99", "currency": "USD" }`. The amount is a string because a JSON number loses digits in many clients, and the currency decides how many decimals are allowed (none for JPY, two for the others).
- **A SKU is normalized.** The API accepts it in any case and returns it in upper case. It is unique across the catalog and cannot change after creation, so an update body has no `sku`.
- **Three shapes for each resource.** A create body, an update body, and a representation are separate schemas, because they have different required properties. A client never sends back a whole representation.
- **A request rejects what it does not know.** A body schema sets `additionalProperties` to `false` (rule `catalog-requests-reject-unknown-properties`), and the API answers an unknown property with a 400. A response can gain properties in a later release, so a client must ignore the ones it does not know.
- **Null is explicit.** A value that can be absent has the type `["string", "null"]`, and a property that is always present is listed in `required`.

## Methods and status codes

| Status | Meaning in this API | Used for |
| --- | --- | --- |
| 200 | Done, and the body is the item or the page | `GET`, `PUT`, and the `discontinue` action |
| 201 | Created, with `Location` and `ETag` | `POST` that creates |
| 304 | Not changed since the `ETag` in `If-None-Match`, and no body | `GET` of one item |
| 400 | The request is not valid: a field, a header, or a body that cannot be parsed | Every operation |
| 401 | No valid token | Every operation |
| 403 | A valid token without the scope of the operation | Every operation |
| 404 | No item has the identifier | One item |
| 409 | The request conflicts with the state of the data | A SKU that exists, a discontinued product, a create that is still running |
| 412 | `If-Match` does not match the current `ETag` | `PUT` and `discontinue` |
| 413 | The body is larger than the limit | A request with a body |
| 415 | The body is not `application/json` | A request with a body |
| 422 | The `Idempotency-Key` was used with a different body | `POST` that creates |
| 428 | `If-Match` is missing | `PUT` and `discontinue` |
| 429 | Too many requests, with `Retry-After` | Every operation |
| 500 | An unexpected error | Every operation |
| 503 | A dependency is unavailable, with `Retry-After` | Every operation |

Three conventions keep the table small:

- **One failure, one status.** A rule about a field is a 400 `validation-failed`, whether it is a length, a decimal count, or a category that does not exist. A 422 is only for a reused idempotency key, because the idempotency draft gives it that meaning.
- **A `PUT` replaces.** It carries the full set of changeable properties, and a missing optional property clears the value.
- **An action is idempotent.** Discontinuing an item that is already discontinued answers 200 with the same representation and the same `ETag`.

## Conditional requests

- **Every item has a strong `ETag`**, and every answer that carries one item sends it (rule `catalog-single-resource-answers-carry-an-etag`).
- **Every `PUT` and every action requires `If-Match`** and documents the 412 and 428 answers (rule `catalog-change-requires-if-match`). `If-Match` takes one strong tag, so `*`, lists, and weak tags are refused.
- **Every `GET` of one item accepts `If-None-Match`** and documents the 304 answer (rule `catalog-single-resource-get-is-conditional`).

## Idempotent creates

- **Every `POST` that creates requires `Idempotency-Key`** (rule `catalog-create-requires-idempotency-key`) and answers 201 with `Location` and `ETag` (rule `catalog-create-answers-with-location`).
- **The key is a quoted string** of 1 to 128 printable ASCII characters, such as `"c1b7e2a4-5f1d-4d0a-8f2e-3a6d9e1b7c42"`. The format is the Structured Field string of the IETF draft, which expired on 2026-04-18 without becoming an RFC (checked 2026-10-11), so this page and the contract are the specification.
- **A client sends a new random key for each new request**, and the same key when it retries that request.
- **The API remembers a key for 24 hours.** Inside that time, the same key with the same body returns the first answer, and the same key with a different body returns 422.

## Pagination

- **Every collection is paged.** It takes `pageSize` (1 to 100, default 20) and `cursor`, and its 200 answer can carry a `Link` header (rule `catalog-collections-paginate`).
- **A page has `items` and `nextCursor`.** The cursor is null on the last page, and a `Link` header with `rel="next"` repeats it as an address while there is a next page.
- **A cursor is opaque and bound to its request.** It is valid only with the filters and the sort that produced it. A client never builds one.
- **The order is stable.** `sort=createdAt` puts the newest first, `sort=name` sorts A to Z, and both break a tie by identifier, so a page never repeats or skips an item.
- **A filter narrows, and a missing filter hides nothing.** Without `status`, every status is returned.

## Versioning and deprecation

- **The version is a path segment**, and a version is a major version. Version 1 is `v1`.
- **Every answer sends `api-supported-versions`**, and every answer documents `api-deprecated-versions`, `Deprecation`, and `Sunset` (rules `catalog-responses-send-api-version` and `catalog-responses-carry-the-deprecation-headers`). The server sends the last three only for a version that is deprecated.
- **A change inside a version is additive.** A new endpoint, a new optional request property, a new optional parameter, and a new property in a response are allowed. A client must tolerate them.
- **A breaking change needs a new version.** Removing or renaming anything, making an optional input required, narrowing a value, changing a status code or an error type, and changing what a field means are all breaking. From WP4.1, a pull request that makes one against the last released contract is blocked.
- **The support window is at least 180 days.** To deprecate a version, the project records the decision in an ADR, starts sending `Deprecation` (RFC 9745) with the date that the deprecation began, and sends `Sunset` (RFC 8594) with a date at least 180 days later. The version keeps working until that date, and the changelog and the documentation announce both dates.
- **No version is deprecated today.** The window is a documented number, and the first deprecation will test it (checked 2026-10-11).

## Errors

- **Every error is `application/problem+json`** (RFC 9457), and no error uses another content type (rule `catalog-errors-are-problem-json`).
- **The `type` is the contract between the server and a client.** It is the address of an entry in [the error catalog](error-catalog.md), and a client branches on it.
- **The `detail` is safe.** It never holds a stack trace, a query, a path on the server, or a secret, and a 500 has a generic `detail`.
- **Every error has a `traceId`.** The same identifier is in the trace and the logs, so a report with it can be followed.
- **To add an error type**, add an entry to the catalog, with its status, and add an example with that `type` to the response that returns it. A test fails when the contract names a type that the catalog does not have, when a catalog entry has no example, and when the status of an example differs from the status of its entry.

## Security

- **Every operation names its own scope** (rule `catalog-operation-has-own-security`), so no operation inherits a default by accident. Reads need `catalog.read` (rule `catalog-reads-need-the-read-scope`), product create and update need `catalog.write`, and product discontinue and every category write need `catalog.admin`.
- **A token never goes in a URL.** It goes in the `Authorization` header only.
- **An identifier never leaks existence.** A product that the caller may not read is a 403 for the whole operation, and a 404 only means that the identifier was never used.

## Documentation inside the contract

- **Every body has an example** (rule `catalog-media-types-have-examples`), and every example is valid against its schema, which the built-in rules `oas3-valid-media-example` and `oas3-valid-schema-example` report as errors.
- **Every operation has a summary and a description** that says which scope it needs, which headers it uses, and what each non-obvious answer means.

## Change the contract

1. **Change the contract first.** Edit `contracts/openapi/catalog/v1/catalog.openapi.yaml`. The code follows the contract, and never the other way.
2. **Lint it.** Run `docker compose -f tools/lint/compose.yaml run --rm contract-lint`. A warning fails it.
3. **Run the tests.** `dotnet test` runs `tests/Catalog.Contract.Tests`, which checks the contract against the permission matrix and the error catalog, and `./dev test scripts` runs the tests of the lint rules and the conformance check.
4. **Update the pages.** Change [the API contract page](api-contract.md) when the operations, the scopes, or the headers change, and [the error catalog](error-catalog.md) when an error type changes.
5. **Write an ADR** when the change makes a decision, and read [the compatibility rules](#versioning-and-deprecation) before you change an operation of a released version.
