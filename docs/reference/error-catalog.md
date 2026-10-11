---
title: "Error catalog"
description: "Every error type that the v1 API returns, with its status code, when it happens, what to do about it, and an example body."
audience: [learners, contributors, maintainers]
type: reference
tools: [openapi]
last-verified: 2026-10-11
owner: "@RostomOhannessian"
---

# Error catalog

Every error that the API returns is an RFC 9457 Problem Details document with the content type `application/problem+json`. Its `type` member is the address of an entry on this page, so a client can branch on the type
and a person can follow the address to read what to do. The conventions behind the table are in [ADR-0028](../adr/0028-http-api-conventions-of-v1.md), and the shapes are in [the contract](api-contract.md).

This page describes the API as the contract defines it. The API itself arrives in WP1.10, so no endpoint returns these errors yet. The address of each entry, such as
`https://rostomohannessian.github.io/full-ci-cd-pipeline/reference/error-catalog.html#validation-failed`, resolves once the documentation site is published at the Phase 1 exit (WP1.13). Until then, read this page in the repository.

## How an error looks

Every error has these members:

| Member | Meaning |
| --- | --- |
| `type` | The address of the entry on this page. Branch on this and on nothing else |
| `title` | A short summary that is the same for every error of the type |
| `status` | The HTTP status code of the response |
| `detail` | An explanation of this occurrence, written for a person. It never holds a stack trace, a query, or a secret |
| `instance` | The path of the request |
| `traceId` | The trace identifier of the request. Quote it when you report a problem, because it finds the request in the traces and the logs |
| `errors` | Only for `validation-failed`: the messages for each invalid field |

```json
{
  "type": "https://rostomohannessian.github.io/full-ci-cd-pipeline/reference/error-catalog.html#validation-failed",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "detail": "The request has 2 invalid fields. See errors for the messages.",
  "instance": "/api/v1/products",
  "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
  "errors": {
    "name": ["The name must have between 1 and 200 characters."],
    "price.amount": ["The amount must have at most 2 decimal places for USD."]
  }
}
```

## How to handle an error

1. Read the `status` first. A 4xx means that the request must change, and a 5xx means that the server could not answer.
2. Branch on the `type`. The `title` and the `detail` can change wording, and the `type` does not.
3. Retry only when the entry says so. A 429 and a 503 carry `Retry-After`, a 409 `idempotency-request-in-progress` is retried with the same request, and a 412 is retried after reading the resource again. Never retry a 400, a 401, a 403, a 404, or a 422 without changing something.
4. Send the `traceId` with every report.

## validation-failed

- **Status:** 400
- **Title:** One or more validation errors occurred.

A field, a parameter, or a header is not valid. The `errors` member maps the JSON path of each invalid field to its messages, such as `price.amount`, and a parameter or a header uses its own name, such as `pageSize` or `If-Match`.
The same type covers an unknown property in a body, a number or a length outside its range, an amount with too many decimals for its currency, a cursor that is not valid, and a `categoryId` that names a category that does not exist or is discontinued.

**What to do.** Read `errors`, fix every field that it lists, and send the request again.

## malformed-request

- **Status:** 400
- **Title:** The request could not be parsed.

The body is not valid JSON, it is nested too deeply, or its encoding is wrong. No field was checked, because the body could not be read.

**What to do.** Send well-formed UTF-8 JSON that matches the schema of the operation.

## idempotency-key-required

- **Status:** 400
- **Title:** An Idempotency-Key header is required.

A request that creates a resource has no `Idempotency-Key` header, or its value is not a quoted string of 1 to 128 printable ASCII characters.

**What to do.** Generate a random value for each new create request, write it in quotes, such as `"c1b7e2a4-5f1d-4d0a-8f2e-3a6d9e1b7c42"`, and send the same value when you retry that request.

## unauthorized

- **Status:** 401
- **Title:** Authentication is required.

The request has no bearer token, or the token is expired, has the wrong issuer or audience, or has a signature that the API does not accept. The response carries a `WWW-Authenticate` header.

**What to do.** Get a new token from the catalog realm and send it as `Authorization: Bearer <token>`.

## forbidden

- **Status:** 403
- **Title:** The token does not allow this operation.

The token is valid, and it does not carry the scope that the operation requires. The `detail` names the scope.

**What to do.** Ask for a token with the scope that [the permission matrix](api-contract.md#permission-matrix) lists for the operation. Repeating the request with the same token never helps.

## not-found

- **Status:** 404
- **Title:** The resource was not found.

No category or product has the identifier in the path.

**What to do.** Check the identifier. A product that was discontinued still exists and is still found, so a 404 means that the identifier was never used.

## sku-already-exists

- **Status:** 409
- **Title:** The SKU is already in use.

Another product has the same SKU after the API normalizes it to upper case. A SKU is unique across the catalog, whatever the status of the other product.

**What to do.** Choose another SKU, or read the existing product and change it.

## product-discontinued

- **Status:** 409
- **Title:** A discontinued product cannot change its price or category.

The product is discontinued, and the update would change its price or its category. The name and the description can still change.

**What to do.** Leave the price and the category as they are, or create a new product.

## idempotency-request-in-progress

- **Status:** 409
- **Title:** A request with this Idempotency-Key is still running.

A first request with the same `Idempotency-Key` has not finished yet.

**What to do.** Wait a moment and send the same request again with the same key. The API then returns the result of the first request.

## precondition-failed

- **Status:** 412
- **Title:** The resource has changed.

The `If-Match` value does not match the current `ETag`, because someone else changed the resource after you read it.

**What to do.** Read the resource again, apply your change to the new version, and send it with the new `ETag`.

## payload-too-large

- **Status:** 413
- **Title:** The request body is too large.

The body is larger than the limit that the API sets for a request body.

**What to do.** Send a smaller body. No operation of this API needs a large one.

## unsupported-media-type

- **Status:** 415
- **Title:** The request body must be application/json.

The `Content-Type` of the request is missing or is not `application/json`.

**What to do.** Send the header `Content-Type: application/json`.

## idempotency-key-reused

- **Status:** 422
- **Title:** The Idempotency-Key was used with a different request.

The `Idempotency-Key` was already used with a request body that is not the same as this one. A key belongs to exactly one request.

**What to do.** Use a new key for a different request. To repeat the first request, send exactly the first body.

## precondition-required

- **Status:** 428
- **Title:** An If-Match header is required.

An update or a discontinue request has no `If-Match` header.

**What to do.** Read the resource, and send the `ETag` that you got in the `If-Match` header.

## rate-limited

- **Status:** 429
- **Title:** Too many requests.

The caller sent more requests than the limit allows. The response carries `Retry-After` with the number of seconds to wait.

**What to do.** Wait for the number of seconds in `Retry-After`, then continue at a lower rate.

## internal-error

- **Status:** 500
- **Title:** An unexpected error occurred.

The server failed in a way that it did not expect. The `detail` is generic on purpose.

**What to do.** Retry once after a short wait, and if it fails again, report the `traceId`.

## service-unavailable

- **Status:** 503
- **Title:** The service is temporarily unavailable.

A dependency of the API, such as the database, cannot be reached. The response carries `Retry-After`.

**What to do.** Wait for the number of seconds in `Retry-After`, then retry.
