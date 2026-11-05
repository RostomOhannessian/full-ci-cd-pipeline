---
title: "Evidence record: the conformance and the security tests of the HTTP API (2026-11-04)"
description: "A dated record of the third pull request of WP1.10 on Windows with Docker Desktop and in the pinned Linux SDK image: what ran where, the counts, what the tests showed about the served document, the authorization matrix, the snapshots, the viewer, and a real Keycloak container, the finding about Verify, and what the runs did not show."
type: evidence-record
date: 2026-11-04
status: verified
audience: [maintainers, contributors]
last-verified: 2026-11-04
owner: "@RostomOhannessian"
---

# Evidence record: the conformance and the security tests of the HTTP API (2026-11-04)

This record is the evidence for the third pull request of WP1.10 (HTTP API): the served OpenAPI document and its comparison with the authored contract, the dev-only viewer, the authorization matrix over the ten operations, the snapshots, and the realm contract tests against a real Keycloak container
([ADR-0042](../../adr/0042-served-contract-conformance-and-snapshots.md), [the HTTP API](../../reference/http-api.md)). It continues [the record of the endpoints](2026-11-04-http-api-endpoints-run.md). It says what ran where, so a reader can tell which host proved which claim.

## What ran where

| Where | Command | What it ran | Result |
| --- | --- | --- | --- |
| Windows x86-64, Docker Desktop, .NET SDK 10.0.403 | `dotnet test --solution ProductCatalog.slnx --configuration Release --no-build --coverage --coverage-output-format cobertura --report-trx --results-directory TestResults` | Every test project natively. The Api tests start the real composition in process, a SQL Server 2022 container, a Valkey 8.1 container, and the Keycloak 26.8.0 container, which assembly fixtures start from the digests of `compose.yaml` | 2,154 tests ran, 2,154 passed, 0 failed, 0 skipped. By project: Api 291, Application 269, Architecture 147, Contract 113, Domain 173, Infrastructure 272, Documentation auditor 265, Governance auditor 624. The Api suite took about 75 seconds and the Infrastructure suite about 53 seconds. Seven tests of the first Windows run of the day failed, and each was a real finding that is fixed: four snapshot tests found no folder, the documentation auditor wanted a command block of the Scalar page marked `illustrative` (two tests), and the governance auditor found the traceability report stale (one test) |
| Windows | `pwsh -NoProfile -File tools/ci/Test-ContractConformance.ps1 -Served TestResults/served/catalog.openapi.json` | The oasdiff container compared the document that `ServedContractTests` wrote with the authored contract, ignoring descriptions, examples, and summaries | Exit 0, "The served document is structurally equal to the authored contract (ignoring description, examples, summary)" |
| Windows | `pwsh -NoProfile -File tools/ci/Test-CoverageThresholds.ps1 -CoverageFile 'TestResults/*.cobertura.xml'` | The coverage gate over the cobertura files of the run | Passed: Catalog.Application 99.54% of lines and 99.58% of branches, Catalog.Domain 99.63% of lines and 98.41% of branches, and the solution 97.45% of lines (3,210 of 3,294) |

The Linux run is recorded below, in the section "The Linux run".

## What the tests showed

| Question | Answer | Test |
| --- | --- | --- |
| Does the host serve what the contract says? | The served document, generated from the ten endpoints and the shared components of the authored file, is structurally equal to the contract in oasdiff v1.33.0, with only descriptions, examples, and summaries ignored. The paths carry no trailing slash and the document declares the version of the file | `ServedContractTests`, the oasdiff script |
| Does a transport record agree with its schema? | Every record of `Catalog.Contracts.V1` has the members of its schema, every required member is present, and a member is `null` only where the schema allows it | `TransportAgreementTests` |
| Is the viewer a Development feature? | In Development `/openapi/v1.json` answers 200 and `/scalar/v1` answers 200 with a looser content policy that the API keeps to itself. A Production host answers 404 for the document, `/scalar`, `/scalar/v1`, and `/scalar/scalar.js` | `ServedContractTests` |
| Does every operation need its scope and its role? | For each of the ten operations, 64 combinations of the three scopes and the three roles: a caller that holds the scope of the contract and a role of the ladder gets past authorization and ends at a 400 that the API makes before a database, and every other caller gets a 403 that names the scope. A request with no token or a garbage token is a 401 | `AuthorizationMatrixTests` |
| Do the error answers keep their shape? | The 12 errors that the API makes by itself, the 7 that the use cases raise, and the 429 with `Retry-After` match the committed snapshots. The trace identifier and the identifiers in the instance path are replaced | `SnapshotTests` |
| Does the realm give the API what it needs? | The pinned Keycloak 26.8.0 image imports `identity/keycloak/realms/catalog-realm.json` with random secrets for the placeholders. The discovery document names the issuer, the keys are RS256, the tokens of `smoke-tests` (all three scopes, the admin role) and `synthetic-shopper` (the read scope, the reader role) name `catalog-api` in `aud`, hold the scopes, and carry `realm_access.roles`. `synthetic-shopper` gets `invalid_scope` when it asks for a scope that it was not given, and a wrong secret is a 401 | `KeycloakRealmContractTests` |
| Does the real API accept the real tokens? | The API accepts a Keycloak token, applies the policy of its scope and role, and refuses a tampered token and a token of another issuer with a 401 | `KeycloakRealmContractTests` |
| Does the realm file hold a secret? | It holds placeholders only, and no literal secret | `KeycloakRealmContractTests` |

## Findings

- **Verify cannot be used at 33.x.** Verify.XunitV3 from 33.0.0 fails the build with SponsorCheck SC021 unless the owner declares a sponsorship or a time-limited exemption. Versions up to 32.0.1 have no such check but need `xunit.v3.extensibility.core` 3.2.2, where the repository uses xunit.v3 4.0.1. The maintainer chose to write a small helper in the test project, and the inventory holds `verify` with a note (ADR-0042).
- **The helper first found its folder through `[CallerFilePath]`.** A Release build rewrites the source paths to `/_/...`, so the full suite could not find the committed snapshots, and four snapshot tests failed with "does not exist". The helper now finds the folder from the repository root.
- **A snapshot that passed once was not deterministic.** The first accepted run held two identifiers that the server and the test make at run time (the instance path of three errors), and a second run showed it. The helper now replaces the UUIDs in the instance path, and the snapshots were regenerated and run twice, the second time with `CI` set and no accept.
- **oasdiff compares components by name and counts small differences.** Microsoft.OpenApi writes `openapi: 3.1.1` for a document that declares 3.1.0, and the collection routes ended with a slash. The generator takes the declared version from the file and trims the slash. The contract was not changed.
- **The first Linux run failed in the Keycloak fixture.** The fixture polled `http://localhost:{port}`, and inside the `linux-tests` container `localhost` is the container and not the Docker host (the image reaches the host through `host.docker.internal`). The server now runs with `--hostname-backchannel-dynamic=true`, the fixture calls it at the address that Testcontainers reports, and the API hosts read its metadata at that address through `Catalog:Auth:MetadataAddress`, so the issuer stays fixed in every token. A Windows run with `TESTCONTAINERS_HOST_OVERRIDE=127.0.0.1` showed the same result as the default run. The test of another issuer now reads real metadata, so its 401 comes from the issuer and not from metadata that could not be read.
- **The documented way to run the API was wrong.** `dotnet run --project src/Catalog.Api` runs in Production without a launch profile, and the Compose addresses are in `appsettings.Development.json`. The page now sets `ASPNETCORE_ENVIRONMENT=Development`.

## Limits

- The document keeps the shared components of the authored contract, so oasdiff proves the operations, the parameters, the bodies, and the responses of the endpoints and not the length or pattern of a field. `TransportAgreementTests` and the validators of the use cases hold those, and a constraint that only a model enforces is not seen by oasdiff.
- The matrix checks that a caller gets past authorization by a 400 that the API makes before a database, and it does not run a successful request. The endpoint suites of the second pull request run the successes.
- The Keycloak tests use the two service-account clients, because the realm has no flow that a test could run for a person (the password grant is off). The demo users are proven by `dev compose check` (WP1.4), and the proof in the cluster belongs to WP3.5.
- The Keycloak container listens on a port that the fixture chooses free before the container starts, so that its issuer is known, and another process could take the port in the moment between the choice and the bind, as in the Valkey fixture.
- The oasdiff step does not run in the `linux-tests` image, which has no Docker Compose. It runs on Windows and in hosted CI.
- Timings are single samples from one machine.
- No hosted run existed for this branch when this record was written.

## The Linux run

| Where | Command | What it ran | Result |
| --- | --- | --- | --- |
| Linux, the pinned SDK 10.0.401 image, Docker socket mounted, `TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal` | `docker compose -f tools/lint/compose.yaml run --rm linux-tests` | A scratch copy of the repository: locked restore, Release build with warnings as errors, every test project with the code coverage extension, and the coverage thresholds. The Api tests start the SQL Server, Valkey, and Keycloak containers from the digests of `compose.yaml` through the Docker socket | 2,154 tests ran, 2,154 passed, 0 failed, 0 skipped. The first run failed only in the Keycloak fixture (see the findings). The coverage gate passed: Catalog.Application 99.54% of lines and 99.58% of branches, Catalog.Domain 99.63% of lines and 98.41% of branches, and the solution 97.45% of lines (3,210 of 3,294) |
| Linux target on Windows | `dotnet publish src/Catalog.Api/Catalog.Api.csproj --configuration Release -r linux-x64 --self-contained false -o <folder>`, then `pwsh -NoProfile -File tools/ci/Test-NoWindowsNativeRuntime.ps1 -Path <folder>` | The publish output with the new references (Scalar.AspNetCore, Microsoft.OpenApi) | No native Windows runtime of Microsoft.Data.SqlClient.SNI.runtime in the output. The publish rewrote the lock files of six source projects, and they were restored with `git checkout` and checked with a `CI=true` locked restore |

The oasdiff step does not run in this image (no Docker Compose), so the conformance check ran on Windows only and runs in hosted CI.
