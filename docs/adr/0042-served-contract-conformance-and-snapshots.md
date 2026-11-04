---
title: "ADR-0042: Generate the served contract from the endpoints, check it with oasdiff, and keep the snapshots in the repository"
description: "Decide how the running API proves that it serves its authored contract: a document that the host generates from its endpoints and the shared components of the contract, an oasdiff check in CI, a viewer that exists in Development only, an authorization matrix over every operation, tests against a real Keycloak, and snapshots that a small helper compares because Verify 33 asks the owner for a sponsorship declaration."
status: accepted
date: 2026-11-04
decision-makers: ["@RostomOhannessian"]
supersedes: []
superseded-by: []
tools: [aspnet-core, openapi, oasdiff, scalar, microsoft-openapi, keycloak, testcontainers-dotnet]
related-adrs: ["0004", "0017", "0020", "0026", "0027", "0028", "0040", "0041"]
evidence:
  - src/Catalog.Api/Documentation/ServedContract.cs
  - src/Catalog.Api/Documentation/DocumentationEndpoints.cs
  - tests/Catalog.Api.Tests/ServedContractTests.cs
  - tests/Catalog.Api.Tests/AuthorizationMatrixTests.cs
  - tests/Catalog.Api.Tests/KeycloakRealmContractTests.cs
  - tests/Catalog.Api.Tests/Support/Snapshot.cs
  - tools/ci/Test-ContractConformance.ps1
  - docs/testing/evidence/2026-11-04-http-api-conformance-run.md
---

# ADR-0042: Generate the served contract from the endpoints, check it with oasdiff, and keep the snapshots in the repository

## Context and problem statement

The contract is authored first ([ADR-0017](0017-contract-first-api-design.md)), and the plan says that the build generates the served OpenAPI document and blocks on oasdiff unless it is structurally equal to the authored one after ignoring descriptions, examples, and summaries
(REQ-CON-002, plan section 8.6, [ADR-0027](0027-contract-linter-and-conformance-tooling.md)). WP1.5 built the comparison script and its tests, and the first two pull requests of WP1.10 built the host and the ten endpoints. This decision closes the loop and adds the tests that the plan row asks for.
These questions were open:

- **What generates the served document.** The plan names Microsoft.AspNetCore.OpenApi. The comparison counts the components by name (checked 2026-11-03 with oasdiff v1.33.0: a missing or an unused schema, parameter, header, or response is a difference), so a document that the framework generates from the types of the endpoints would need a transformer for every shared part of the contract.
- **What the code can say and what only the contract says.** The code knows the paths, the methods, the operation identifiers, the scopes, and which parameters, bodies, and responses each operation has. It does not know that a name has at most 100 characters or what pattern a SKU follows, because the models enforce those rules and do not describe them.
- **Where the viewer lives.** The row asks for a dev-only Scalar, and a viewer in production is an attack surface and a leak of the contract's examples.
- **How to prove the authorization matrix and the realm.** The matrix over the real operations, and the contract between the realm file and the API with real tokens, are the last proofs that REQ-IDN-001, REQ-IDN-002, and REQ-IDN-005 name.
- **How to keep snapshots.** The row asks for Verify snapshots, and the pinned version of the plan, 33.2.0, fails the build unless the owner declares a sponsorship or an exemption.

This ADR serves REQ-CON-002, REQ-API-001, REQ-API-005, REQ-API-006, REQ-IDN-001, REQ-IDN-002, REQ-IDN-003, REQ-IDN-005, and REQ-QUA-001.

Facts that shape the decision (checked 2026-11-04):

- Microsoft.OpenApi 2.12.2 writes `openapi: 3.1.1` for a document that declares `3.1.0`, and oasdiff counts the version as a difference.
- The route of an endpoint that a group maps with an empty path ends with a slash (`/api/v1/categories/`), and the contract has none.
- Verify.XunitV3 from 33.0.0 fails the build (SponsorCheck SC021, [time-bounded exemptions of at most 12 months](https://github.com/VerifyTests/Verify)) unless the owner sets a sponsorship or an exemption. The releases up to 32.0.1 have no such check, and they ask for xunit.v3.extensibility.core 3.2.2 where the repository uses xunit.v3 4.0.1. The package license stays MIT.
- The realm has two clients with service accounts, `smoke-tests` (all three scopes and the admin role) and `synthetic-shopper` (the read scope and the reader role), and Keycloak refuses a scope that a client does not have among its default and optional scopes.
- Scalar.AspNetCore 2.18.7 is MIT licensed and has no dependency on net10.0 besides the shared framework (the nuspec).

## Decision drivers

- A change to an endpoint that the contract does not describe fails the build, and so does a change to the contract that the endpoints do not serve.
- A rule that only the contract states is stated once.
- Nothing of the viewer or the document exists in production.
- A test uses a real identity provider where the plan says so, and it needs no secret that a person chose.
- No dependency asks the owner for a declaration, a fee, or an exemption (ADR-0004).

## Considered options

1. A document that the host generates from its endpoints for the part that the code owns, and from the embedded copy of the authored file for the part that the contract owns (chosen).
2. Microsoft.AspNetCore.OpenApi with document and operation transformers that rebuild every shared component.
3. Serve the authored file as it is.

## Decision outcome

Chosen option: **generate the operations, keep the vocabulary of the contract**, because it makes the check about what the endpoints do, without a second copy of the style guide in C#.

| Area | Decision | Why |
| --- | --- | --- |
| Operations | Each endpoint declares a `ContractOperation` (the tag, the schema of the success response, the status, the shared parameters, the body schema, and its 409 response). The generator takes the path, the method, the operation identifier, and the scope from the endpoint table (the policy name is the scope), and derives the error responses by the rules of the style guide: a path identifier is a 404, a precondition header is a 412 and a 428, a body is a 413 and a 415, and an idempotency key is a 422 and a 409 | Nothing that the table knows is written twice, and an operation lists its refusals through what it accepts |
| Components | The information, the servers, the tags, the security of the document, and every shared schema, parameter, header, and response come from the embedded copy of the authored file, read with Microsoft.OpenApi. `TransportAgreementTests` compare the records of `Catalog.Contracts.V1` with those schemas: the same members, every required member present, and null only where the schema allows it | The code has no other statement of a length or a pattern, and the records are what the API reads and writes |
| The check | The tests write the document to `TestResults/served/catalog.openapi.json`, and the `ci` workflow runs `tools/ci/Test-ContractConformance.ps1` on it after the tests, blocking, with the elements that the plan lists ignored and nothing else. The document says the version that the file declares, and the paths have no trailing slash | REQ-CON-002 |
| The viewer | `GET /openapi/v1.json` and `/scalar` are registered only in the Development environment, with a content policy of their own for the viewer, and the strict policy stays on every other response. A test starts a Production host and expects 404 for the document and the viewer | Dev-only, proven |
| Matrix | `AuthorizationMatrixTests` read the scope of each operation from the authored contract and send all 64 combinations of the three scopes and the three roles to each of the ten operations, with a ladder of roles that the test writes apart from the permission table. An allowed caller must end at a 400 that the API makes before a database, and any other caller must get a 403 | REQ-IDN-002 |
| Realm | `KeycloakFixture` starts the pinned image of `compose.yaml` with the generic Testcontainers builder (no new package), imports the realm file with its `${NAME}` placeholders filled from random values, and fixes the port so that the issuer is `http://localhost:{port}/realms/catalog`. The server runs with `--hostname-backchannel-dynamic=true`, so the keys and the token endpoint resolve from the address at which a caller reached it, and the fixture and the API (through `Catalog:Auth:MetadataAddress`) read it at the address that the test process can reach, which is `host.docker.internal` in the Linux test image, while every token keeps the fixed issuer. The tests read the discovery document, the keys, the audience, the scopes, and the roles of real tokens, show that a client cannot ask for a scope that it was not given, and send the tokens to the real API. They live in `Catalog.Api.Tests` and not in the project `Catalog.Identity.ContractTests` that plan sections 7 and 8.7 name, because they reuse the host fixtures of the API tests and a project of its own would copy them | REQ-IDN-001, REQ-IDN-005, REQ-QUA-001 |
| Snapshots | `Snapshot.MatchAsync` compares a JSON value with `Snapshots/{file}.{method}.verified.json` with two-space indentation and LF, replaces the trace identifier, writes `.received.json` and fails on a difference or a missing file, and accepts a change only with `SNAPSHOT_ACCEPT=1` outside CI. The snapshots are the 20 error answers and the paths that the endpoints generate. Verify is not used | A fee-shaped build gate that the owner must declare against is a commercial term, which ADR-0004 does not allow, and the owner declined to declare one |

### Consequences

- Good: an endpoint that gains a header, a response, or a scope without the contract fails CI, and so does a contract that the endpoints do not serve, because oasdiff compares every part that the code generates.
- Good: the matrix, the realm, and the viewer have tests that a person can read, and the real Keycloak shows that a client cannot get a scope that it was not given.
- Good: no new package is added besides Scalar.AspNetCore, which is MIT licensed and used in Development only.
- Bad: the document keeps the shared components of the contract, so a schema that the code does not follow is found by `TransportAgreementTests` and not by oasdiff. Mitigation: that test fails on a member that the record and the schema do not share.
- Bad: the helper is code that a library would give. Mitigation: it is about 100 lines, and a mismatch names the first different line.
- Bad: Scalar.AspNetCore and Microsoft.OpenApi ship in the production image, although Development uses them. Mitigation: nothing is registered in other environments, a test proves that, and the image of WP3 can trim them.
- Neutral: the host embeds the authored file, so a container image of the API must be built with the `contracts` folder in its context.

### Confirmation

- `ServedContractTests` generate the document and write it for the check, show the ten operations and their scopes, serve the viewer with its own policy in Development, and answer 404 for the document and the viewer in Production. The `ci` job `Build and test` runs oasdiff on the document.
- `TransportAgreementTests` compare every record of the contract namespace with its schema.
- `AuthorizationMatrixTests` run the 640 combinations and the 401 cases against the real endpoints.
- `KeycloakRealmContractTests` run against the pinned Keycloak image.
- `ErrorSnapshotTests`, `RateLimitSnapshotTests`, `UseCaseErrorSnapshotTests`, and `ServedPathsSnapshotTests` compare the error bodies and the generated paths with the committed snapshots.

## Pros and cons of the options

### Generate the operations and keep the vocabulary of the contract

- Good: the check is about behavior, and the style guide has one statement.
- Bad: part of the document is the contract's own words, so it proves less about the schemas than about the operations.

### Microsoft.AspNetCore.OpenApi with transformers for every component

- Good: the standard generator.
- Bad: every shared parameter, header, response, and constraint needs a transformer, which is a second copy of the contract in C# that a person must keep equal by hand.

### Serve the authored file as it is

- Good: no code.
- Bad: the comparison would always pass and prove nothing about the endpoints.

## Revisit when

- Microsoft.AspNetCore.OpenApi or a successor can describe the shared components of the contract without a copy.
- Verify drops its sponsorship check, or the owner decides to declare an exemption.
- A second version of the contract exists, and the document needs one version set for each version (WP4.1).
- The image of the API is built (WP3), and the contract file and the viewer must be in or out of it.

## Further reading

- [oasdiff: diff](https://github.com/oasdiff/oasdiff/blob/v1.33.0/docs/DIFF.md): the comparison of two documents (checked 2026-11-04)
- [OpenAPI Specification 3.1.0](https://spec.openapis.org/oas/v3.1.0.html): the document that the contract is (checked 2026-11-04)
- [Scalar for ASP.NET Core](https://guides.scalar.com/scalar/scalar-api-references/integrations/net-aspnet-core): the viewer (checked 2026-11-04)
- [Verify](https://github.com/VerifyTests/Verify): the snapshot library and its sponsorship check from 33.0.0 (checked 2026-11-04)
