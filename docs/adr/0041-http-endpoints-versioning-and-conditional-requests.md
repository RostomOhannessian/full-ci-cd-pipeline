---
title: "ADR-0041: Serve the version 1 operations with thin endpoints, a strict body reader, conditional requests, and idempotent creates"
description: "Decide how the ten operations of the version 1 contract are served: one thin endpoint for each operation, a path version that Asp.Versioning routes and reports on every answer, a bounded and strict JSON reader, strong ETags in If-Match and If-None-Match, quoted idempotency keys that the pipeline replays, absolute Link headers that keep every filter, and which gaps between the contract and the use cases close on the side of the use cases."
status: accepted
date: 2026-11-04
decision-makers: ["@RostomOhannessian"]
supersedes: []
superseded-by: []
tools: [aspnet-core, asp-versioning, openapi, sql-server, valkey]
related-adrs: ["0017", "0027", "0028", "0032", "0036", "0039", "0040"]
evidence:
  - src/Catalog.Api/Endpoints/CatalogEndpoints.cs
  - src/Catalog.Api/Http/JsonBody.cs
  - src/Catalog.Api/Http/ConditionalHeaders.cs
  - src/Catalog.Api/Mapping/ViewMapping.cs
  - tests/Catalog.Api.Tests/CategoryEndpointTests.cs
  - tests/Catalog.Api.Tests/ProductEndpointTests.cs
  - tests/Catalog.Api.Tests/RequestBodyTests.cs
  - docs/testing/evidence/2026-11-04-http-api-endpoints-run.md
---

# ADR-0041: Serve the version 1 operations with thin endpoints, a strict body reader, conditional requests, and idempotent creates

## Context and problem statement

The first pull request of WP1.10 built the host ([ADR-0040](0040-http-api-composition-and-token-validation.md)). This decision serves the ten operations of the authored contract, `contracts/openapi/catalog/v1/catalog.openapi.yaml`, on top of it, through the handlers of WP1.7 and the stores of WP1.8 and WP1.9.
The contract fixes the paths, the parameters, the headers, and the errors ([ADR-0028](0028-http-api-conventions-of-v1.md)), and the use cases fix the rules, so what is left is the translation between HTTP and the use cases. These questions were open:

- **Where each part of the translation lives.** Plan sections 8.1 and 8.6 and REQ-ARC-004 say that an endpoint depends on the contract types and the handler interfaces and never on the domain model or an adapter.
- **How the body is read.** The contract says that the API rejects an unknown member with a 400 and enforces size and depth limits (REQ-API-006). The default model binding of minimal APIs ignores an unknown member, reports a malformed body in its own shape, and leaves the size to the server.
- **What a precondition header may hold.** The contract allows one strong `ETag` in `If-Match` and `If-None-Match`, and a library default would accept a weak tag, a list, and `*`.
- **How the idempotency key is read, and what a replay returns.** The contract gives the key as a quoted string, and the pipeline of [ADR-0032](0032-decorator-pipeline-and-application-results.md) replays the first result.
- **How a version is routed and reported.** The version is a path segment, and every response must list the supported versions, including a `401` that a middleware writes before any endpoint runs.
- **What to do when the contract and a use case disagree.** The contract is the source of truth ([ADR-0017](0017-contract-first-api-design.md)), and implementing the endpoints found two places where the use cases fell short of it.

This ADR serves REQ-API-001 to REQ-API-006, REQ-ARC-004, REQ-APP-004, REQ-APP-005, REQ-DOM-003, and REQ-DATA-004.

Facts that shape the decision (checked 2026-11-04):

- `JsonSerializer` writes a `DateTimeOffset` with a zero offset as `+00:00`, and the contract shows every instant as `2026-10-11T12:00:00Z`.
- The persistence layer reads an amount with the decimal places of its column, and the contract writes an amount with the decimal places of its currency (`1990` for the yen and `89.99` for the dollar).
- Asp.Versioning.Http reports the versions from the endpoint (a filter), so a response that a middleware writes before the endpoint carries no `api-supported-versions`.
- The source generator of regular expressions adds a type in the namespace `System.Text.RegularExpressions.Generated` to the assembly. The architecture test `The_rules_read_assemblies_that_hold_only_the_types_of_their_own_layer` fails on a type outside the layer, because it exists to catch a tool that changed an assembly.
- `TestServer` does not enforce the body limit of Kestrel, so a limit that only the server enforces cannot be tested there.
- The use cases read an identifier through the factory of the model, which accepts only a UUID of version 7, so an identifier of another version is a validation error and not a 404.
- The use case `ListCategories` had no status filter, and the product search matched the name only. The contract and the API contract page give both: a category list takes `status`, and `q` matches the name or the SKU.

## Decision drivers

- An endpoint holds no rule of the catalog, so a rule has one home and a test of it does not need HTTP.
- Every refusal of the input is made in one place, and it has the shape of the error catalog and names the field that failed.
- A caller cannot send a body, a header, or a parameter that the contract does not allow and get an answer that suggests that it was accepted.
- A limit holds on every server and in every test host, so it is code and not only a setting of Kestrel.
- A response is the same bytes as the contract shows, so a client reads one spelling of an instant and one spelling of an amount.

## Considered options

1. One thin endpoint for each operation with a small reader for the transport, a body reader of our own, header parsers of our own, and a version middleware (chosen).
2. MVC controllers with `[ApiController]`, model binding, and `ProblemDetails` from the framework.
3. Minimal API endpoints with the default model binding and `JsonUnmappedMemberHandling.Disallow`.

## Decision outcome

Chosen option: **thin endpoints over a transport layer that the API owns**, because the contract has stricter rules than the defaults of either framework, and each of those rules is a few lines that a test can hold.

| Area | Decision | Why |
| --- | --- | --- |
| Where things live | `Endpoints` holds one method for each operation: it reads the request through `Mapping`, runs the handler through its interface, and writes the response. `Mapping` turns views into contract records and requests into commands. `Http` holds the readers and writers of the transport. An architecture rule fails when a type of `Endpoints` depends on the domain model, Infrastructure, or CrossCutting | REQ-ARC-004. A rule of the catalog stays in the use case |
| Operations | The names of the operations are the `operationId` values of the contract, and each endpoint requires the level that its request declares (`RequireAccessOf<TRequest>`), so the endpoint and the pipeline read the level from one place | One source for the access level |
| Version | The group is `/api/v{version:apiVersion}` with a version set of `1.0`, no default version, and reporting on. `ApiVersionReportMiddleware` asks the reporter of Asp.Versioning for the headers of the chosen endpoint when the response starts and keeps a header that is already there | REQ-API-002. A `401`, a `403`, and a `429` carry the headers too, as the contract says |
| Identifiers and parameters | A path identifier and a query parameter are read as text and checked by the API. A value of the wrong type is a `validation-failed` error that names the parameter, and a parameter that appears twice is one too | The contract's `400` says which field failed, and the binder would say only that the request could not be read |
| Body | `JsonBody` requires `application/json` with no charset or `utf-8` (415), reads at most the limit plus one byte whether or not the length is announced (413), parses with the depth limit (400 `malformed-request`), refuses a root that is not an object, lists every member that the contract does not name with its path (400 `validation-failed`, from the metadata of the serializer), and reports a value of the wrong type with its path. Case matters | REQ-API-006. The limit is code, and the list of members is the DTO itself |
| Preconditions | `If-Match` and `If-None-Match` hold one strong token in quotes of at most 64 characters, as the persistence layer writes it. Anything else is a `validation-failed` error for the header. A missing `If-Match` goes on to the use case, which answers 428, and a stale one answers 412. A `GET` with the current tag answers 304 with the `ETag` and no body | REQ-API-003. The use case decides what a missing header means |
| ETag | The `ETag` of a response is the version of the view, in quotes, and a create, a read, a change, and a discontinue send it. A replay of an idempotent create returns the first view, so its `ETag` is the one of the first response | The persistence layer owns the version |
| Idempotency | `Idempotency-Key` is one quoted string of 1 to 128 printable ASCII characters without a quote or a backslash, and it is read before the body. A missing or invalid key is 400 `idempotency-key-required`. The pipeline replays the first result for the same key and body (201 again with the same `Location` and `ETag`), answers 422 for another body, and 409 while the first request runs | REQ-API-004 and REQ-DATA-004 |
| Pages | The handler hands out the cursor, and the endpoint writes `nextCursor` in the body and a `Link` header with `rel="next"` and an absolute address that keeps every parameter of the request in its order and puts the new cursor last | The contract says the `Link` keeps every filter |
| Values | `ViewMapping` writes an amount with the decimal places of its currency and a status as `active` or `discontinued`, and `UtcDateTimeOffsetConverter` writes every instant in UTC with a `Z`. The price of a body must have the shape of the contract (`price.amount`) before it becomes a number, and the rest is the model's | One spelling in a response, and no rule repeated |
| No regular expressions | The shapes of the headers and the amount are checked character by character | The generator adds a type to the assembly, and the architecture test keeps the layer assemblies free of foreign types |
| Gaps in the use cases | `ListCategories` gets an optional `Status`, and the product search matches the name or the SKU. The contract stays as it is | The contract is the source of truth, and both gaps are in code that WP1.7 and WP1.8 wrote from a shorter reading of it |

### Consequences

- Good: each operation is one method of about twenty lines that a reader checks against the contract, and an architecture rule keeps rules of the catalog out of it.
- Good: every refusal of the input names its field, and the same writer makes every error.
- Good: a body of any size costs the server at most the limit, and the tests prove it on a host that has no server limit.
- Good: a response has one spelling of an instant, an amount, and a status, which the conformance test of the third pull request can compare with the contract.
- Bad: the body reader and the header parsers are code that the framework would give for free. Mitigation: each has a test for every branch, and the metadata of the serializer replaces the list of members.
- Bad: a request that is wrong in two ways reports only the first. The order is the identifier, the headers, the body, and then the use case, which reports its own errors together.
- Bad: `Cache-Control: no-store` on every response, which the security headers set, keeps a shared cache from storing a `GET` that has an `ETag`. A client still sends `If-None-Match` and gets 304, so the cost is a round trip and not the body. Revisit when a gateway cache is planned (WP3.8).
- Neutral: an identifier that is not a UUID of version 7 is a 400 and not a 404, because the model refuses it first.

### Confirmation

- `CategoryEndpointTests` and `ProductEndpointTests` run every operation against a real SQL Server and a real Valkey: the 201 with `Location` and `ETag`, the 304, the 412 and 428, the replay and the 422 and 409 of an idempotent create, the discontinue that changes nothing the second time, the filters, and the pages that follow the `Link` header to the end.
- `RequestBodyTests` holds the media type, the size with and without an announced length, the depth, the members that the contract does not name, and the types of values, and none of them needs a database.
- `VersioningTests` holds the headers on a `401`, a `403`, and a `400`, and that a path without a version or with an unknown one is not routed.
- `tests/Catalog.Architecture.Tests` holds the rule `api-endpoints-depend-on-contracts-and-handler-interfaces-only`, with a clean and a violating fixture.
- The read store tests hold the status filter and the SKU search, and the use case tests hold the status filter.

## Pros and cons of the options

### One thin endpoint for each operation with a transport layer that the API owns

- Good: every rule of the contract that the frameworks do not give is a few lines with a test.
- Good: the endpoints depend on nothing that the architecture rule forbids.
- Bad: more code than the defaults.

### MVC controllers with model binding

- Good: attributes give some validation and a standard `ProblemDetails`.
- Bad: model binding ignores an unknown member and reports a bad value in its own shape, so the contract's errors need a filter for each of them, and the plan asks for explicit plumbing ([ADR-0032](0032-decorator-pipeline-and-application-results.md)).

### Minimal API endpoints with the default binding and `JsonUnmappedMemberHandling.Disallow`

- Good: the least code.
- Bad: an unknown member throws an exception whose message is the only place that names the member, a size limit is the server's, and a bad route value is a 400 whose body is not the error catalog's.

## Revisit when

- A second version of the contract exists, and the version set, the deprecation headers, and the grouping of the served document change (WP4.1).
- A gateway cache stores responses (WP3.8), and `Cache-Control` and `ETag` need to work together.
- The model accepts an identifier of another version, and a missing resource is a 404 for every well-formed identifier.
- The contract adds an operation, a member, or a parameter, and the metadata of the serializer, the readers, and the mapping change with it.

## Further reading

- [RFC 9110: conditional requests](https://www.rfc-editor.org/rfc/rfc9110.html#name-conditional-requests): `If-Match`, `If-None-Match`, and the strong comparison (checked 2026-11-04)
- [RFC 8288: web linking](https://www.rfc-editor.org/rfc/rfc8288.html): the `Link` header with `rel="next"` (checked 2026-11-04)
- [RFC 9745: the Deprecation header](https://www.rfc-editor.org/rfc/rfc9745.html) and [RFC 8594: the Sunset header](https://www.rfc-editor.org/rfc/rfc8594.html) (checked 2026-11-04)
- [API Versioning for ASP.NET Core](https://dotnet.github.io/aspnet-api-versioning): how the versions are reported (checked 2026-11-04)
