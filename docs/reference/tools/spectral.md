---
title: "Spectral"
description: "The OpenAPI linter that checks the authored contract against the stock OpenAPI rules and 15 rules of this API, from a digest-pinned container, on every pull request."
audience: [learners, contributors]
tier: B
tools: [spectral]
introduced: "phase-1"
prerequisites: ["Docker", "PowerShell 7 (pwsh), for the rule tests in step 5"]
estimated-time: "20 minutes"
last-verified: 2026-10-11
verified-against: { spectral: "6.17.0" }
owner: "@RostomOhannessian"
---

# Spectral

## Overview

An OpenAPI document is data. Nothing in the format says that every create needs an `Idempotency-Key` header, or that every error is `application/problem+json`. The authored
contract is the source of truth for the API ([ADR-0017](../../adr/0017-contract-first-api-design.md)), so a change that weakens one of those rules must fail a pull request, and
a reviewer must not have to notice it. Spectral is a linter for JSON and YAML documents. It reads a ruleset, applies each rule to the parts of the document that the rule selects,
and prints every violation. It needs a ruleset to lint anything, and it ships rulesets for OpenAPI and AsyncAPI documents (checked 2026-10-11 in the [README](https://github.com/stoplightio/spectral)).

Five concepts cover most of what you need:

| Concept | What it is | Where it appears here |
| --- | --- | --- |
| Ruleset | A YAML, JSON, or JavaScript file that lists rules and extends other rulesets | [contracts/openapi/spectral.yaml](../../../contracts/openapi/spectral.yaml) |
| Rule | A `given` (a JSONPath that selects parts of the document) and a `then` (a function that checks each selected part, or one `field` of it), with a description and a severity | 15 rules named `catalog-...`, and 2 built-in rules whose severity the ruleset states |
| Function | A check that a rule applies. The core functions include `pattern`, `casing`, `truthy`, and `schema`, which applies a JSON Schema | Most of the rules use `schema` |
| Severity | `error`, `warn`, `info`, `hint`, or `off`. The option `--fail-severity` sets the lowest severity that makes the exit code 1, and the default is `error` | The CI command fails on `warn`, so a warning blocks the merge too |
| Resolved document | Spectral replaces every `$ref` with the object it points to before it runs a rule, so a rule sees what a client sees | A rule on `parameters` sees the parameters that come from `components` |

The ruleset extends the stock `spectral:oas` rules in their `recommended` set, which find the common mistakes of any OpenAPI document, such as a missing `contact` or an example that does
not match its schema. It states the severity of the two example-validity rules as `error`. Both are errors by default in 6.17.0 (checked 2026-10-11 in the
[source of the ruleset](https://github.com/stoplightio/spectral/blob/v6.17.0/packages/rulesets/src/oas/index.ts)), so the lines keep them errors if a later release lowers the default. It then adds 15 rules named `catalog-...`. Each turns a sentence of plan sections 8.4, 8.6, and 8.7 into a check, and each has a `description` that says what it enforces. [ADR-0028](../../adr/0028-http-api-conventions-of-v1.md) decides the conventions that the rules check:

| Area | Rules |
| --- | --- |
| Paths and operations | `catalog-paths-are-versioned`, `catalog-operation-ids-are-camel-case` |
| Security | `catalog-operation-has-own-security`, `catalog-reads-need-the-read-scope` |
| Creates and changes | `catalog-create-requires-idempotency-key`, `catalog-create-answers-with-location`, `catalog-change-requires-if-match` |
| Reads, ETags, and paging | `catalog-single-resource-get-is-conditional`, `catalog-single-resource-answers-carry-an-etag`, `catalog-collections-paginate` |
| Bodies and errors | `catalog-requests-reject-unknown-properties`, `catalog-errors-are-problem-json`, `catalog-media-types-have-examples` |
| Versioning headers | `catalog-responses-send-api-version`, `catalog-responses-carry-the-deprecation-headers` |

Spectral lints the authored contract, `contracts/openapi/catalog/v1/catalog.openapi.yaml`, and any other `*.openapi.yaml` file under `contracts/`. It reads one document at a time, so two other
checks cover what a linter cannot. The C# tests in `tests/Catalog.Contract.Tests` compare the contract with the error catalog and the permission matrix ([ADR-0027](../../adr/0027-contract-linter-and-conformance-tooling.md)),
and [oasdiff](oasdiff.md) compares the served document with the authored one (planned in WP1.10).

## Decision rationale

The plan left the linter open: "Redocly CLI, Spectral, or vacuum" (plan section 8.6, step 2). [ADR-0027](../../adr/0027-contract-linter-and-conformance-tooling.md) records a time-boxed trial. It linted
the same contract with each candidate, first with the stock rules and then with five trial rules written in each tool's own language, against a contract with one planted defect for each rule (checked 2026-10-11).

| | Redocly CLI 2.62.1 | Spectral 6.17.0 | vacuum v0.33.0 |
| --- | --- | --- | --- |
| License | MIT | Apache-2.0 | MIT |
| Trial rules that caught their planted defect | 2 of 5 | 5 of 5 | 4 of 5 |
| Stock rules on the contract | Valid, 1 warning | 1 warning | 0 errors, 11 warnings, 3 hints |
| Container run, start to end | 1.6 s | 1.8 s | 0.5 s |
| What stopped the rest | A declarative assertion cannot say "this operation has a parameter named X", and a JavaScript plugin would be code to maintain | Nothing, after one change to the filter syntax | A false finding on every response, and one planted defect that it did not report |

Spectral won because it was the only candidate that expressed all five trial rules without a workaround that changed what the rule checks. The rest of the picture:

- **License:** Apache-2.0, which GitHub reports for the [repository](https://github.com/stoplightio/spectral) (checked 2026-10-11). The license is on the allow-list of [ADR-0004](../../adr/0004-licensing-and-dependency-license-policy.md),
  and the repository runs the image as a tool and never links or redistributes it.
- **Maintenance:** the latest release is v6.17.0, published 2026-10-01, and the stack pins it (checked 2026-10-11). The pace is uneven: the releases page lists nothing between v6.15.0 (2025-04-22) and v6.16.1
  (2026-06-30) (same check). ADR-0027 says to revisit the choice when there is no release for 12 months, or when the image stops being published. Dependabot proposes updates to the tag and the digest.
- **Rules are code in a second language:** Spectral uses the `nimma` package for JSONPath, and sometimes falls back to `jsonpath-plus`, so its syntax can differ from other JSONPath libraries and
  from RFC 9535 (checked 2026-10-11 in the [rules guide](https://github.com/stoplightio/spectral/blob/v6.17.0/docs/guides/4a-rules.md)). The filter syntax has no `=~` operator, so a rule that selects operations by the start of their ID writes
  `@.operationId.match(/^create/)` with a guard on `@property`. A rule whose `given` selects nothing passes without a message, so every rule has a test that breaks the contract and expects the rule to fire.
- **The image runs an end-of-life Node release:** the Dockerfile at v6.17.0 starts from `node:16-alpine` (checked 2026-10-11), and Node 16 reached end of life on 2023-09-11 (checked 2026-10-11 in the
  [Node.js release schedule](https://github.com/nodejs/Release/blob/main/schedule.json)). The service mounts the repository read-only and holds no credentials. It sets `NODE_NO_WARNINGS`, because a dependency prints a deprecation notice that is not about this repository.
- **The image reports its start:** the entry point of the image runs a script that sends one anonymous request to `smartbear.gateway.scarf.sh` with the version and the platform, unless `DO_NOT_TRACK` or `SCARF_NO_ANALYTICS=true`
  is set (checked 2026-10-11, in the script inside the pinned image, and in the [README](https://github.com/stoplightio/spectral)). The `contract-lint` service sets neither.

## Setup tutorial

**Prerequisites:** Docker. Step 5 also needs PowerShell 7 (`pwsh`, or `dotnet pwsh` after `dotnet tool restore`; see [PowerShell](powershell.md)), because it runs the Pester tests with the pinned test tools, which the script installs after it checks their checksums. The pinned version is 6.17.0, and the image
is pinned by tag and digest in [tools/lint/compose.yaml](../../../tools/lint/compose.yaml), so there is nothing else to install. Compose prints its own lines about the container before the output of the command.

1. Check the version that the image holds.

   ```bash illustrative
   docker compose -f tools/lint/compose.yaml run --rm -T contract-lint --version
   ```

   Expected output:

   ```text
   6.17.0
   ```

2. Lint the contract. This is the command that the `contract` job of the `ci` workflow runs, so the lines below are a workflow step in YAML. The service runs
   `lint --ruleset contracts/openapi/spectral.yaml --fail-severity warn --format stylish contracts/**/*.openapi.yaml`.

   [!code-bash[Contract lint in CI](../../../.github/workflows/ci.yml#contract-lint)]

   Expected output, with exit code 0:

   ```text
   No results with a severity of 'warn' or higher found!
   ```

3. Read one rule. The rule `catalog-create-requires-idempotency-key` selects every `post` operation whose `operationId` starts with `create`. Its function, `schema`, applies a JSON Schema to the `parameters` of each
   selected operation, and `contains` says that at least one parameter must be a required header named `Idempotency-Key`.

   [!code-bash[The Idempotency-Key rule](../../../contracts/openapi/spectral.yaml#idempotency-rule)]

4. Break a rule on purpose. In `contracts/openapi/catalog/v1/catalog.openapi.yaml`, change `operationId: listCategories` to `operationId: list_categories`, and run the command of step 2 again.

   Expected output, with exit code 1. The columns are the line and column, the severity, the rule, the message, and the path in the document:

   ```text
   /workdir/contracts/openapi/catalog/v1/catalog.openapi.yaml
    49:20  error  catalog-operation-ids-are-camel-case  Every operation ID is lower camel case, so a generated client has idiomatic method names.  paths./api/v1/categories.get.operationId

   ✖ 1 problem (1 error, 0 warnings, 0 infos, 0 hints)
   ```

   Undo the edit. Any finding of `warn` or above fails the command, so a rule at `warn` blocks the merge as well. The stock rule `info-contact` is one example: with the `contact` block removed from `info`,
   the command reports it as a warning and exits with code 1, and Spectral without `--fail-severity warn` reports the same warning and exits with code 0.

5. Prove that every rule can fail. CI runs the script tests, which analyze `scripts` and `tools/ci` and then run every Pester file there.

   [!code-bash[Script tests in CI](../../../.github/workflows/ci.yml#script-tests)]

   One of those files is [tools/ci/ContractRules.Tests.ps1](../../../tools/ci/ContractRules.Tests.ps1). It breaks the contract once for each of the 16 rules that the tests cover (the 15 `catalog-...` rules and the
   built-in example rule), lints all the broken copies in one container run, and expects each copy to trigger its rule. It also expects the unbroken contract to trigger none, so a finding is caused by the break.
   The summary ends with a line like the one below, where the count of passed tests changes as tests are added (337 on 2026-10-11).

   ```text
   Tests Passed: 337, Failed: 0, Skipped: 1, Inconclusive: 0, NotRun: 0
   ```

   To add a rule, write it in `contracts/openapi/spectral.yaml` with a `description`, then add a break for it to the list of mutations in `ContractRules.Tests.ps1` and its name to the list of rules that the test runs. The test
   named "has a break for every catalog rule in the ruleset" fails until you do, so a new rule cannot arrive untested.

**Cleanup:** nothing is left behind. `--rm` removes each container, and the repository is mounted read-only. Undo the edit from step 4.

## Further research

- [Spectral rulesets](https://docs.stoplight.io/docs/spectral/e5b9616d6d50c-rulesets): the ruleset properties, rules, `extends`, and severities (checked 2026-10-11)
- [Spectral CLI](https://docs.stoplight.io/docs/spectral/9ffa04e052cc1-spectral-cli): the options, the formatters, and how `--fail-severity` decides the exit code (checked 2026-10-11)
- [Core functions](https://github.com/stoplightio/spectral/blob/v6.17.0/docs/reference/functions.md): `pattern`, `casing`, `schema`, `truthy`, and the others, with their options (checked 2026-10-11)
- [OpenAPI rules](https://github.com/stoplightio/spectral/blob/v6.17.0/docs/reference/openapi-rules.md): every rule of `spectral:oas`, whether it is recommended, and its default severity (checked 2026-10-11)
- [The Spectral repository](https://github.com/stoplightio/spectral): releases, the license, and the README that describes the Docker image and its telemetry opt-out (checked 2026-10-11)
