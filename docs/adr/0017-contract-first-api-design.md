---
title: "ADR-0017: Design the API contract first and enforce conformance"
description: "Author the OpenAPI contract first and keep code conformant to it so the repository can teach deliberate API design and enforce compatibility."
status: accepted
date: 2026-10-04
decision-makers: ["@RostomOhannessian"]
accepted-in: "WP1.5"
supersedes: []
superseded-by: []
tools: [openapi, oasdiff, kiota, nswag, microsoft-openapi, spectral, aspnet-core, asp-versioning, pactnet, scalar]
related-adrs: ["0018", "0027", "0028"]
evidence:
  - docs/research/2026-10-04-dotnet-ecosystem-verification.md
  - docs/research/spikes/1.a-pactnet-kestrel.md
---

# ADR-0017: Design the API contract first and enforce conformance

## Context and problem statement

Plan gap G3 changes the repository from a generated-contract story to a contract-first story. Phase 1 must author the v1 API contract up front in `contracts/openapi/catalog/v1/catalog.openapi.yaml`, then make the running API prove conformance to that contract before merge. Phase 4 later adds breaking-change governance, consumer tests, and the generated client.

This repository has teaching goals as well as delivery goals. Plan sections 8.6 and 10.1, together with the [Phase 1](../plans/phases/phase-1-api-core.md) and [Phase 4](../plans/phases/phase-4-contracts-gitops.md) plans, require explicit handling for ETags, `If-Match`, `Idempotency-Key`, pagination headers, OAuth scopes, examples, and Problem Details. Those details are easier to design and review in a contract file than after endpoints already exist.

The .NET ecosystem verification record supports this direction. It says oasdiff v1.33.0 has full OpenAPI 3.1 support, Kiota v1.35.0 has stable OpenAPI 3.1 input, and NSwag v14.7.1 still describes itself as a Swagger/OpenAPI 2.0 and 3.0 toolchain (checked 2026-10-04). The same record also says ASP.NET Core 10 uses Microsoft.OpenApi 2.x internally, so the repository should pin the 2.x line for compatibility.

The repository still needs hand-written DTOs and server code. This ADR is about which artifact is authoritative, not about generating every server type from the contract.

## Decision drivers

- The contract must be reviewable before implementation details hide HTTP semantics.
- The repository needs an automated conformance proof between the authored contract and the served document.
- Phase 4 needs a stable baseline for breaking-change checks, Kiota client generation, and Pact tests.
- The chosen approach must fit ASP.NET Core 10's OpenAPI 3.1 toolchain and avoid tools that lag on 3.1 support.

## Considered options

1. Generate the contract from code and treat that generated document as the source of truth.
2. Author the contract first and generate server stubs from it.
3. Author the contract first, hand-write versioned DTOs, and enforce conformance with automated checks.
4. Use a different contract language such as TypeSpec as the primary source.

## Decision outcome

Chosen option: **Author the contract first, hand-write versioned DTOs, and enforce conformance with automated checks**, because it preserves deliberate API design while keeping server code idiomatic for a teaching repository.

### Consequences

- Good: HTTP semantics, examples, scopes, and error shapes become visible and reviewable before implementation details accumulate.
- Bad: the repository now maintains both the authored YAML contract and the implementation-facing DTOs. Mitigation: make oasdiff conformance blocking in Phase 1 and add the breaking-change gate in Phase 4.
- Neutral: this ADR does not choose the contract linter. [ADR-0027](0027-contract-linter-and-conformance-tooling.md) chose Spectral in WP1.5, and [ADR-0028](0028-http-api-conventions-of-v1.md) fixes the HTTP conventions that the contract holds.

### Confirmation

- **WP1.5** authors the OpenAPI 3.1 contract, chooses the linter, and publishes the API style guide.
- **WP1.10** generates the served document at build time, runs oasdiff structural equality, and proves the HTTP semantics in integration tests.
- **WP4.1** adds the breaking-change gate against the last released contract and generates `Catalog.Client` with Kiota.
- **WP4.1** (Schemathesis) and **WP4.2** (Pact) use the contract, which confirms that the authored file stays useful beyond documentation.

Execution record (WP1.0, 2026-10-06): [spike 1.a](../research/spikes/1.a-pactnet-kestrel.md) showed that PactNet 5.0.1 on .NET 10 writes
consumer pacts and verifies a provider hosted with `WebApplicationFactory<T>.UseKestrel(0)` over a real socket. A deliberately broken
contract failed verification for the right reason, and the provider-state endpoint existed only in the test assembly, so it was absent
from the production composition. This supports the Pact leg of the contract workflow that WP4.2 delivers. The spike also found that
`UseKestrel()` without an argument binds the fixed port 5000, so tests should pass `0`, and that `ServerAddress` does not exist in
`Microsoft.AspNetCore.Mvc.Testing` 10.0.12, so tests read the bound address from `IServerAddressesFeature`. PactNet is still dormant
and has no `win-arm64` or musl native library, and only `win-x64` was exercised. The decision stays proposed, and WP1.5 still accepts it.

Execution record (WP1.5, 2026-10-11): WP1.5 accepts this decision and delivers the first two steps of the contract workflow. The authored contract is
`contracts/openapi/catalog/v1/catalog.openapi.yaml`, an OpenAPI 3.1.0 document with the 10 operations of plan section 8.6, Problem Details for every error, the headers
of plan sections 8.4 and 8.6, the three OAuth scopes, and a valid example for every body. Spectral lints it with the ruleset in `contracts/openapi/spectral.yaml`, and
a test breaks the contract once for each rule and expects the rule to fire. 86 contract tests read it with Microsoft.OpenApi 2.12.2 and check it against the permission
matrix and the error catalog. `tools/ci/Test-ContractConformance.ps1` compares a served document with the authored one through oasdiff 1.33.0 and ignores descriptions, examples,
and summaries, and its tests show wording changes passing and seven kinds of structural change failing. The served document, the DTOs, and the blocking comparison
arrive with WP1.10, and the breaking-change gate with WP4.1. Authoring first paid off at once: writing the contract settled 17 error types, the status code for each failure,
the format of the idempotency key, and the shape of money, and each was a one-line decision in a YAML file and not a refactor of code ([ADR-0028](0028-http-api-conventions-of-v1.md)).

## Pros and cons of the options

### Generate the contract from code and treat that generated document as the source of truth

- Good: one artifact drives the whole stack and minimizes duplication.
- Bad: design intent for headers, examples, deprecation, and compatibility policy shows up late, after endpoint code already exists.

### Author the contract first and generate server stubs from it

- Good: keeps the contract authoritative and can speed initial scaffolding.
- Bad: generated stubs add ceremony that this repository does not need, and they can hide Clean Architecture boundaries behind generated server glue.

### Author the contract first, hand-write versioned DTOs, and enforce conformance with automated checks

- Good: preserves explicit API design, keeps DTOs readable and teachable, and gives a clear place for oasdiff and Kiota to plug in later.
- Bad: requires disciplined conformance checks so the YAML and the code do not drift.

### Use a different contract language such as TypeSpec as the primary source

- Good: TypeSpec is a real design-first alternative with strong modeling features.
- Bad: it adds another language to teach and is not the direction supported by the evidence gathered for this repository's .NET-first toolchain.

## Revisit when

- ASP.NET Core's OpenAPI toolchain changes in a way that makes authored YAML plus served JSON conformance awkward or unsupported.
- Kiota or oasdiff loses the OpenAPI 3.1 support this workflow depends on.
- WP1.5 or WP1.10 shows that the authored contract and DTO workflow creates drift that the blocking conformance tests cannot manage cleanly.
- A future ADR adopts a different multi-language contract source for a documented reason.

## Further reading

- [.NET ecosystem verification record](../research/2026-10-04-dotnet-ecosystem-verification.md) (checked 2026-10-04)
- [Implementation plan](../plans/implementation-plan.md) (checked 2026-10-04)
- [Spike 1.a: PactNet 5.0.1 on .NET 10 with a Kestrel-hosted provider](../research/spikes/1.a-pactnet-kestrel.md) (checked 2026-10-06)
