---
title: "ADR-0027: Lint the contract with Spectral, test it with Microsoft.OpenApi, and compare documents with oasdiff"
description: "Run Spectral 6.17.0 from a digest-pinned container with a ruleset that turns plan sections 8.6 and 8.7 into checks, read the contract in C# with Microsoft.OpenApi 2.12.2 for the checks that need the other documents, and compare the served document with oasdiff 1.33.0."
status: accepted
date: 2026-10-11
decision-makers: ["@RostomOhannessian"]
supersedes: []
superseded-by: []
tools: [spectral, oasdiff, microsoft-openapi, microsoft-openapi-yamlreader, openapi, docker-compose]
related-adrs: ["0004", "0017", "0018", "0020", "0023", "0028"]
evidence:
  - contracts/openapi/spectral.yaml
  - tools/ci/ContractRules.Tests.ps1
  - tools/ci/ContractConformance.Tests.ps1
---

# ADR-0027: Lint the contract with Spectral, test it with Microsoft.OpenApi, and compare documents with oasdiff

## Context and problem statement

[ADR-0017](0017-contract-first-api-design.md) makes the authored OpenAPI contract the source of truth, and leaves the contract linter open: "Redocly CLI, Spectral, or vacuum" (plan section 8.6, step 2). WP1.5 authors the contract in
`contracts/openapi/catalog/v1/catalog.openapi.yaml` and must choose, because the contract is only a source of truth if a pull request that weakens it fails.

A linter has two jobs here. The first is the common mistakes of any OpenAPI document: a missing description, an example that does not match its schema, an unused component. Every candidate does that. The second is the rules of this API, which
plan sections 8.4, 8.6, and 8.7 state in prose: every create needs an `Idempotency-Key`, every change needs `If-Match` and the 412 and 428 answers, every error is `application/problem+json`, and every answer names the API versions. A rule that
a tool cannot express is a rule that nobody checks.

Two more checks do not fit a linter. A linter reads one document, and the contract must also agree with the error catalog page and the permission matrix page. And a served document, which the API build generates, must be compared with the
authored one (REQ-CON-002).

Facts that shape the decision (checked 2026-10-11):

| Tool | Version | License | Official image | Last image update |
| --- | --- | --- | --- | --- |
| Redocly CLI | 2.62.1 | MIT | `redocly/cli` | 2026-10-09 |
| Spectral | 6.17.0 | Apache-2.0 | `stoplight/spectral` | 2026-10-02 |
| vacuum | v0.33.0 | MIT | `dshanley/vacuum` | 2026-10-10 |
| oasdiff | v1.33.0 | Apache-2.0 | `tufin/oasdiff` | 2026-10-08 |

All four licenses are on the allow-list of [ADR-0004](0004-licensing-and-dependency-license-policy.md), and the images are used as tools and never linked or redistributed. Microsoft.OpenApi 2.12.2 is MIT, and
Microsoft.AspNetCore.OpenApi 10.0.12 depends on Microsoft.OpenApi from 2.12.0 and below 3.0.0, although the latest release of the library is 3.10.2.

## Decision drivers

- Every rule of the API that the plan states in prose is a check that fails a pull request.
- Every rule is shown to fail, as the repository requires of every gate.
- A tool that runs in a digest-pinned container, with nothing to install, on every host and in CI.
- Licenses on the allow-list, and a maintained project.
- A ruleset that a reader of the repository can read, and that the next maintainer can change.

## Considered options

1. Redocly CLI.
2. Spectral.
3. vacuum.
4. No linter, and C# tests only.

## Decision outcome

Chosen option: **Spectral 6.17.0, run from the digest-pinned `stoplight/spectral` image**, because it was the only candidate that expressed all five trial rules of the comparison below without a workaround that changed what the rule checks.

The rest of the design follows from that choice.

- **The ruleset** is `contracts/openapi/spectral.yaml`. It extends `spectral:oas` (recommended), whose rules already report an example that does not match its schema as an error, and it adds 15 rules named `catalog-...`, one for each sentence of plan sections 8.4, 8.6, and 8.7 that a linter can check.
- **The command** is the `contract-lint` service of `tools/lint/compose.yaml`. A warning fails it. The `contract` job of the `ci` workflow and a contributor run the same command.
- **Every rule is shown to fail.** `tools/ci/ContractRules.Tests.ps1` breaks the contract in one way for each rule, lints all the broken copies in one container run, and expects each copy to trigger its rule. The unbroken contract must trigger none, and a rule with no break fails the test, so a new rule cannot arrive untested.
- **The checks that need other documents** are C# tests in `tests/Catalog.Contract.Tests`, which read the contract with Microsoft.OpenApi 2.12.2 and Microsoft.OpenApi.YamlReader 2.12.2: the error types in the contract against the error catalog page, the scopes against the permission matrix, and the patterns of the headers against sample values.
  The packages stay on the 2.x line, and no production project references them.
- **The conformance check** is `tools/ci/Test-ContractConformance.ps1`, which runs oasdiff 1.33.0 from the digest-pinned `tufin/oasdiff` image and ignores descriptions, examples, and summaries (plan section 8.6, step 6). Its Pester tests show that a changed constraint, a dropped required
  property, a missing response, a renamed header, a changed scope, a renamed endpoint, and a changed version all fail, and that wording changes pass. WP1.10 generates the served document and runs it through this script.

### The comparison

The same contract was linted with each tool, first with the stock rules and then with five trial rules, written in each tool's own language, and each trial rule was tested against a contract with one planted defect for each rule (a time-boxed trial).

| | Redocly CLI 2.62.1 | Spectral 6.17.0 | vacuum v0.33.0 |
| --- | --- | --- | --- |
| Stock rules on the contract | Valid, 1 warning (`localhost` in `servers`) | 1 warning (`info-contact`) | 0 errors, 11 warnings, 3 hints |
| Container run, start to end | 1.6 s | 1.8 s | 0.5 s |
| Trial rules that caught their planted defect | 2 of 5 | 5 of 5 | 4 of 5 |
| What stopped the rest | A declarative assertion cannot say "this operation has a parameter named X", and the rule for the error content type did not fire on a response that comes from `components`. A JavaScript plugin could, at the cost of code to maintain | Nothing, after the filter syntax was adjusted | A hyphenated header name in a field path gave a false finding on every response until the path was changed, and one planted defect (an operation without its own security) was not reported |
| Ruleset language | Redocly configuration with assertions | Spectral ruleset (JSONPath and functions) | Spectral-compatible ruleset |

Spectral needed one adjustment: its filter syntax has no `=~` operator, so a rule that selects operations by the start of their ID uses `@.operationId.match(/^create/)` with a guard on the property name. The tests of the rules exist partly because such details are easy to get wrong without noticing.

### Consequences

- Good: the rules of the API are data in one file, and every rule has a test that breaks the contract and expects it.
- Good: the contract tests read the contract with the model that ASP.NET Core uses, so WP1.10 can compare like with like.
- Good: vacuum reads Spectral rulesets, so moving to vacuum later is a small change if Spectral stalls.
- Bad: Spectral's JSONPath dialect has quirks, and the ruleset is a second language for a contributor to read. Mitigation: each rule has a description that says what it enforces, and each has a test.
- Bad: three tools touch the contract (Spectral, the C# tests, and oasdiff). Mitigation: each does what the others cannot, and each runs from a pinned version.
- Bad: the Spectral image prints a Node deprecation notice that is not about this repository, so the service sets `NODE_NO_WARNINGS`.
- Bad: the Spectral image is built on Node 16, which reached end of life on 2023-09-11, and it sends one anonymous usage request on every start unless it is told not to (checked 2026-10-11). Mitigation: the image is pinned by digest, the mount is read-only, no secret is in reach, and the `contract-lint` service has no network (`network_mode: none`) and sets the opt-out variables. The oasdiff service has no network either.
- Bad: Spectral published no release between v6.15.0 (2025-04-22) and v6.16.1 (2026-06-30), a gap of more than a year (checked 2026-10-11), and v6.17.0 followed on 2026-10-01. Mitigation: the ruleset is in the format that vacuum reads, and the revisit trigger below applies to a gap like that one.
- Neutral: Redocly CLI and vacuum stay in the inventory notes as evaluated options, and `openapi-linter`, the placeholder entry, is replaced by the Spectral entry.

### Confirmation

- `docker compose -f tools/lint/compose.yaml run --rm contract-lint` passes on the contract, and fails on each of the 16 breaks of `tools/ci/ContractRules.Tests.ps1`.
- `tools/ci/ContractConformance.Tests.ps1` passes, with wording changes passing and seven kinds of structural change failing.
- `tests/Catalog.Contract.Tests` passes, and the documentation auditor passes with the new tool pages.
- **WP1.10** runs the served document through the conformance script, and adds the breaking-change comparison with the last release in **WP4.1**.

## Pros and cons of the options

### Redocly CLI

- Good: the most complete product. It lints, bundles, and renders documentation, and its recommended rules found the fewest false alarms.
- Bad: its declarative custom rules could not express two of the five trial rules, and a plugin means JavaScript code to maintain in a repository that has none.

### Spectral

- Good: expressive rules, with JSONPath and a schema function that can state "this array contains an object with this name", and a ruleset format that other tools read.
- Good: the container is the project's own and is updated with each release.
- Bad: the JSONPath dialect has quirks, and a rule that fails to select anything passes silently, which the per-rule tests exist to catch.

### vacuum

- Good: the fastest, a single Go binary, and it reads Spectral rulesets.
- Bad: it is a 0.x project that released five versions in four weeks (2026-09-13 to 2026-10-10), which is a lot of change to follow for a gate. The trial found a false finding and a missed defect in a ruleset that Spectral handled.

### No linter, and C# tests only

- Good: nothing more to learn.
- Bad: the tests would re-implement what a linter does, in C#, for every rule, and the contract would have no check that a contributor can run in a second without building the solution.

## Revisit when

- Spectral has no release for 12 months, or its image stops being published.
- A rule of the API needs a check that Spectral cannot express, and vacuum or a Redocly plugin can.
- A later OpenAPI minor version becomes the project's choice, and a tool in this ADR does not read it.
- The conformance check ignores something that the plan did not list, and WP1.10 must justify it in an ADR.

## Further reading

- [Spectral rulesets](https://docs.stoplight.io/docs/spectral/e5b9616d6d50c-rulesets) (checked 2026-10-11)
- [Spectral CLI](https://docs.stoplight.io/docs/spectral/9ffa04e052cc1-spectral-cli) (checked 2026-10-11)
- [oasdiff](https://www.oasdiff.com/) (checked 2026-10-11)
- [Microsoft.OpenApi](https://github.com/Microsoft/OpenAPI.NET) (checked 2026-10-11)
- [ADR-0017: Design the API contract first and enforce conformance](0017-contract-first-api-design.md)
