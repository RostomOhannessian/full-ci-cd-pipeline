---
title: "Scalar"
description: "The API reference viewer that the catalog API serves in the Development environment only, from the OpenAPI document that the running host generates."
audience: [learners, contributors]
tier: B
tools: [scalar]
introduced: "phase-1"
prerequisites: ["The .NET SDK that global.json pins", "Docker, for the stack that the API needs when you run it by hand"]
estimated-time: "15 minutes"
last-verified: 2026-11-04
verified-against: { scalar: "2.18.7" }
owner: "@RostomOhannessian"
---

# Scalar

## Overview

Scalar renders an OpenAPI document as a page that a person can read and try. In this repository it is the dev-only viewer of the HTTP API: it shows the document that the running host generates from its endpoints ([ADR-0042](../../adr/0042-served-contract-conformance-and-snapshots.md)), so what a developer reads is what the API serves, and
what `oasdiff` has found equal to [the authored contract](../api-contract.md). Four terms cover it:

| Term | What it is | Where it appears here |
| --- | --- | --- |
| Document | The OpenAPI 3.1 JSON that the host generates | `GET /openapi/v1.json`, from `Documentation/ServedContract.cs` |
| Viewer | The page that renders the document | `/scalar/v1`, from the `Scalar.AspNetCore` package |
| Development only | Nothing is registered in any other environment | `DocumentationEndpoints.MapDevelopmentDocumentation` |
| Viewer policy | A content security policy for the viewer, because its scripts and styles need more than the strict policy of the API | Set for `/scalar` only; every other response keeps `default-src 'none'` |

## Decision rationale

The plan row asks for a dev-only Scalar, and the inventory has listed it since Phase 0. The package Scalar.AspNetCore 2.18.7 is MIT licensed, it has no dependency on net10.0 besides the shared framework of ASP.NET Core, and 2.18.7 is its newest release (checked 2026-11-04 in the nuspec).
It does not need the generator of Microsoft.AspNetCore.OpenApi, because it reads any document at a path, so the host serves the document that [ADR-0042](../../adr/0042-served-contract-conformance-and-snapshots.md) generates. A viewer in production would be an attack surface and would give a caller the examples of the contract, so nothing is registered outside Development, and a test proves it.
The alternative, Swagger UI, was not weighed against it in an ADR, and the plan names Scalar.

## Setup tutorial

**Prerequisites:** the repository, the .NET SDK, and Docker.

1. Run the tests that prove the viewer and the document, which start the real host in process (no stack is needed):

   ```text illustrative
   dotnet test --project tests/Catalog.Api.Tests/Catalog.Api.Tests.csproj --configuration Release --filter-class "*ServedContractTests"
   ```

   Expected output:

   ```text
   total: 3
   failed: 0
   succeeded: 3
   ```

2. To read the viewer by hand, start the stack and the API in the Development environment, as [the HTTP API page](../http-api.md#run-it-against-the-compose-stack) shows (it sets `ASPNETCORE_ENVIRONMENT=Development`), and open `http://127.0.0.1:8080/scalar/v1`. The document is at `http://127.0.0.1:8080/openapi/v1.json`.

**Cleanup:** stop the API, and run `./dev compose down` if you started the stack.

## Further research

- [Scalar for ASP.NET Core](https://guides.scalar.com/scalar/scalar-api-references/integrations/net-aspnet-core): the options of the package (checked 2026-11-04)
- [The Scalar repository](https://github.com/scalar/scalar): releases and the license (checked 2026-11-04)
