---
title: "ADR-0028: Fix the HTTP conventions of the version 1 contract"
description: "Decide the status codes, headers, error types, formats, and deprecation window of the v1 API before any endpoint exists, so the contract and not the code settles each one."
status: accepted
date: 2026-10-11
decision-makers: ["@RostomOhannessian"]
supersedes: []
superseded-by: []
tools: [openapi, spectral, asp-versioning]
related-adrs: ["0017", "0027"]
evidence:
  - contracts/openapi/catalog/v1/catalog.openapi.yaml
  - docs/reference/error-catalog.md
  - docs/reference/api-style-guide.md
---

# ADR-0028: Fix the HTTP conventions of the version 1 contract

## Context and problem statement

Plan section 8.6 lists the resources, the headers, and the error format of the API, and section 8.4 lists the concurrency and idempotency rules. It leaves a set of small choices open, and each one changes what a client has to write: which status code a
failed business rule gets, what an `Idempotency-Key` looks like, how money is written, where an error type points, and how long a deprecated version stays. [ADR-0017](0017-contract-first-api-design.md) says that these choices belong in the
authored contract, before the code exists. WP1.5 writes the contract, so it makes them, and this ADR records them so that WP1.6 to WP1.10 build to the contract and do not argue with it.

Facts that shape the choices (checked 2026-10-11):

- The IETF Idempotency-Key draft is `draft-ietf-httpapi-idempotency-key-header-07`, dated 2025-10-15, and it expired on 2026-04-18 without becoming an RFC. It says the value is a Structured Field string, so a UUID is written in quotes, that a missing key gets 400, that a key reused with a different payload gets 422, and that a concurrent
  retry gets a conflict error. Plan section 8.4 says to follow the draft.
- RFC 9457 obsoletes RFC 7807 and defines `application/problem+json` with `type`, `title`, `status`, `detail`, and `instance`, and allows extension members.
- RFC 9745 defines the `Deprecation` response header as a Structured Field date, and RFC 8594 defines `Sunset` as an HTTP date.
- ASP.NET Core 10 writes a `decimal` as a JSON number, and many JSON clients read a number as a double and lose digits.

## Decision drivers

- A client can branch on the status code and on one field of the error, and never needs to parse a message.
- A decision that the plan already made is not changed here.
- Every decision is visible in the contract, so the linter, the tests, and the conformance check can hold the code to it.
- A choice that is cheap to make now and costly to change after a release is made now, because the contract is not released until `v0.1.0`.

## Considered options

1. Decide these conventions in the contract and this ADR, before the code (chosen).
2. Let the first implementation decide, and write the contract from the running API.

## Decision outcome

Chosen option: **decide them now**, because option 2 is the generated-contract story that ADR-0017 rejected. The decisions are these.

| Area | Decision | Why |
| --- | --- | --- |
| Error type | Every `type` is `https://rostomohannessian.github.io/full-ci-cd-pipeline/reference/error-catalog.html#<slug>`, and the slug is a heading of `docs/reference/error-catalog.md`. The address works once the documentation site is published at the Phase 1 exit (WP1.13) | The plan says that `type` URIs resolve to the catalog, and the site is where it is published |
| Which status for what | 400 for a request that is not valid (a field, a header, or a body that cannot be parsed), 401 and 403 for the token, 404 for an unknown identifier, 409 for a state conflict, 412 and 428 for the version precondition, 413 and 415 for the body, 422 only for a reused idempotency key, 429 and 503 with `Retry-After`, and 500 for the rest | 422 is the status that the draft gives to a reused key, so using it for anything else would make the code ambiguous. A rule on a field is a validation error, which is the same answer that a client already handles |
| A category that does not exist or is discontinued | A 400 `validation-failed` with a message on `categoryId`, and not a 404 or a 409 | The category is a field of the body, and the URL identifies the product |
| Discontinued product | A change of price or category is a 409 `product-discontinued`, and a change of name or description is allowed | Plan section 8.3 |
| Idempotency | Every `POST` that creates a resource requires `Idempotency-Key`, which is the draft's quoted string of 1 to 128 printable ASCII characters. A missing key is a 400, a reused key with another body is a 422, and a key that is still running is a 409. A replay returns the first response, including the status, the body, `Location`, and `ETag`. Records live 24 hours | Plan section 8.4 says "required on POST creates". Category creation is a `POST` create too |
| Actions are idempotent | `POST .../discontinue` needs `If-Match` and answers 200 with the same representation and `ETag` when the resource is already discontinued | Plan section 8.3 |
| Entity tags | A strong tag derived from the row version, written as a quoted URL-safe token. `If-Match` takes one strong tag, and `*`, lists, and weak tags are not accepted | The plan says "strong ETag", and one tag keeps the comparison trivial |
| Versions | The version is the path segment `v1`. Every response carries `api-supported-versions`, and every response also documents `api-deprecated-versions`, `Deprecation`, and `Sunset`, which a server sends only for a version that is deprecated | A client learns about a deprecation on any call |
| Deprecation window | A version is supported for at least 180 days after the `Deprecation` header first appears, and `Sunset` names a date at least 180 days after that first day. The window is a documented number and an ADR can change it | The plan asks for "a documented support window" and gives no number |
| Money | `{ "amount": "89.99", "currency": "USD" }`, with the amount as a decimal string and the currency from `USD`, `EUR`, `GBP`, and `JPY` | A string keeps every digit in every client. The currency decides how many decimals are allowed (JPY none, the others two), and the server rejects more |
| SKU | A SKU is normalized to upper case on write, is unique across the catalog, and cannot change after creation, so an update body has no `sku` | Plan section 8.3 lists no event for a changed SKU, and an identifier that never changes is easier to cache |
| Request and response shapes | Separate schemas for a create body, an update body, and a representation. A request body rejects an unknown property, and a response may gain properties | Plan section 8.6 says unknown properties return 400, and a client that tolerates new response properties survives additive changes |
| Pagination | Every collection takes `pageSize` (1 to 100, default 20) and an opaque `cursor`, and answers with `items`, `nextCursor` (null on the last page), and a `Link` header with `rel="next"` when there is a next page | Plan section 8.6 |
| Order | `sort=createdAt` puts the newest first and `sort=name` sorts A to Z, and both break ties by identifier | They match the keyset indexes of plan section 8.4 |
| Filters | Without `status`, every status is returned | A filter narrows a result, and the default does not hide data |
| Error body | Problem Details with `type`, `title`, `status`, `detail`, `instance`, and `traceId`, and for a validation error an `errors` object that maps a field path to its messages. `detail` is safe for a person and never holds a stack trace, a query, or a secret | Plan sections 8.6 and 8.11 |
| Security | Every operation names its own scope. Reads need `catalog.read`, product create and update need `catalog.write`, product discontinue and every category write need `catalog.admin` | Plan section 8.7 |
| Conformance | The served document must equal the authored one after ignoring descriptions, examples, and summaries. `info.title`, `info.version`, and `servers` are not ignored, so WP1.10 must make the generated document match them | Plan section 8.6, step 6 |

### Consequences

- Good: WP1.6 to WP1.10 have a contract to build to, and the linter holds every one of these decisions in the contract.
- Good: a client gets one rule for each case, with a single status code for each failure.
- Bad: the contract is not released, so a mistake here costs a change that WP1.10 can still absorb, and a mistake after `v0.1.0` is a breaking change. Mitigation: WP1.10 may propose a change through the contract first, and the conformance check shows any drift.
- Bad: the Idempotency-Key draft has expired, and a client that sends the key without quotes gets a 400. Mitigation: the error says what to send, and the catalog page and the style guide show the format.
- Bad: the type URLs do not resolve until the site is published. Mitigation: the catalog page is in the repository at the path that the address maps to, and WP1.13 publishes it.
- Neutral: the choice of four currencies is a starting set, and WP1.6 owns the final list and the precision of each. Changing it is a contract change.

### Confirmation

- The contract, `docs/reference/api-contract.md`, `docs/reference/api-style-guide.md`, and `docs/reference/error-catalog.md` say the same thing, and `tests/Catalog.Contract.Tests` fails when they disagree about an error type, a status code, or a scope.
- The Spectral ruleset holds the structural conventions, and `tools/ci/ContractRules.Tests.ps1` shows each rule failing.
- **WP1.7** maps each application error to one row of the table of error types, **WP1.8** implements the idempotency store with the 24-hour expiry, and **WP1.10** implements the headers and runs the conformance check.

## Pros and cons of the options

### Decide these conventions in the contract and this ADR, before the code

- Good: the decisions are reviewed on their own, and the code is built to them.
- Bad: some decisions will meet facts in WP1.10 that nobody could see yet.

### Let the first implementation decide, and write the contract from the running API

- Good: no contract rework.
- Bad: the first implementation's accident becomes the contract, which is what ADR-0017 set out to avoid.

## Revisit when

- WP1.7 or WP1.10 finds that a status code or a shape cannot be built as written, and the contract and this ADR change together.
- The IETF publishes the idempotency draft as an RFC, or replaces it, and the format of the key changes.
- A client of the API needs a currency or a precision that the set does not allow.
- A version needs to be deprecated, which tests the window for the first time.

## Further reading

- [RFC 9457: Problem Details for HTTP APIs](https://www.rfc-editor.org/rfc/rfc9457.html) (checked 2026-10-11)
- [RFC 9745: The Deprecation HTTP Response Header Field](https://www.rfc-editor.org/rfc/rfc9745.html) (checked 2026-10-11)
- [RFC 8594: The Sunset HTTP Header Field](https://www.rfc-editor.org/rfc/rfc8594.html) (checked 2026-10-11)
- [The Idempotency-Key HTTP Header Field, draft 07](https://www.ietf.org/archive/id/draft-ietf-httpapi-idempotency-key-header-07.txt) (checked 2026-10-11)
- [RFC 9110: HTTP Semantics](https://www.rfc-editor.org/rfc/rfc9110.html) (checked 2026-10-11)
