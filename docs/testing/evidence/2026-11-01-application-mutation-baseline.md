---
title: "Evidence record: application mutation baseline (2026-11-01)"
description: "A dated record of the first Stryker.NET mutation run on the application layer: the score, how the 26 first survivors were triaged, and how to run it again."
type: evidence-record
date: 2026-11-01
status: verified
audience: [maintainers, contributors]
last-verified: 2026-11-01
owner: "@RostomOhannessian"
---

# Evidence record: application mutation baseline (2026-11-01)

This record is the manual evidence for the application half of `REQ-QUA-006`. Stryker.NET 5.0.0 mutated the `Catalog.Application` project, ran the tests of `tests/Catalog.Application.Tests` against each mutant, and scored the tests at **96.92 percent** after the survivors were triaged and the first run had scored **90.00 percent**. The plan asks for a score of at least 70 percent before the score can become a gate ([plan section 10.4](../../plans/implementation-plan.md)), and [ADR-0018](../../adr/0018-test-stack-and-quality-gates.md) keeps the score a report while the Stryker runner for Microsoft.Testing.Platform is a preview. The domain baseline is in [its own record](2026-10-12-domain-mutation-baseline.md).

## Scope and limits

- The run was made on the author's Windows x86-64 machine with Docker Desktop 29.8.2, through the `mutation` service of `tools/lint/compose.yaml` with the argument `application`. The service runs Stryker.NET in `mcr.microsoft.com/dotnet/sdk:10.0.401`, pinned by digest `sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317`, because Stryker.NET cannot start on a Windows host that enforces Smart App Control (spike 1.c). The pins stay as they are until the maintainer decides to move them.
- It covers `Catalog.Application` only: the results, the mapping table, the permission table, the paging, the views, the handlers, and the feature registration. The decorators and the validators are in `Catalog.CrossCutting`, which their own tests cover, and a mutation run of that project is a follow-up.
- The tests run the handlers through the production chain of decorators over an in-memory catalog, so a survivor can mean that a test never reaches a rule, and a kill can come from a decorator test.
- Stryker's Microsoft.Testing.Platform runner is a preview and says so in its log, so the figures are a report to read and not a result to enforce.
- `REQ-QUA-006` stays `in_progress` in [the requirements register](../../requirements/requirements.yaml), because the gate (WP4.8) remains.

## Result

| Measure | First run | Final run |
| --- | --- | --- |
| Tests in the project | 180 | 192 |
| Mutants created | 360 | 360 |
| Ignored by the block-already-covered filter | 45 | 45 |
| Compile errors | 55 | 55 |
| Mutants tested | 260 | 260 |
| Killed (with the timeout) | 234 | 252 |
| Survived | 24 | 8 |
| Not covered | 2 | 0 |
| Mutation score | 90.00 percent | **96.92 percent** |

The score is the killed mutants (a mutant that makes the tests time out counts as killed) divided by the killed, the survived, and the uncovered ones. Stryker took 28 seconds on the first run. The figures come from the JSON report (`TestResults/mutation/application/reports/mutation-report.json`), because the console table counts the skipped mutants too.

## The survivors

The first run left 26 mutants alive or uncovered. Eighteen were weaknesses in the tests, and 12 new tests in `BoundaryTests` and in the handler tests killed them. The other 8 remain, and each is accounted for.

| Mutant | Why it survives or how it was killed | Class |
| --- | --- | --- |
| `GetCategoryHandler` and `GetProductHandler`: the refusal of a bad identifier removed (not covered) | The validator refused it first. A test runs the handlers without validators | Killed by a new test |
| `CategoryView.From` and `ProductView.From`: the null checks removed (4) | No test passed null. One test does for each argument | Killed by a new test |
| `Pages.Build`: `rows.Count <= size` changed to `<` | No test returned a page that holds exactly the page size. One does | Killed by a new test |
| `PageCursor.TryDecode`: `Length > 512` changed to `>=` | No test used a cursor of exactly 512 characters. One builds it | Killed by a new test |
| `EntityTag.TryCreate`: `Length <= 64` changed to `<`, and the message of `FromBytes` | No test used 64 characters, and none read the message | Killed by two new tests |
| The messages of the list refusals, and the edges `< 1 or > 100` of the search text (6) | The tests read the field and not the message. One test now compares each message, and another uses 1, 100, and 101 characters | Killed by new tests |
| The cursor of a page by creation time: the format `"O"` changed to `""` | The in-memory store parsed either form. A test now reads the cursor and compares the text | Killed by a new test |
| `ListProducts.Hash`: the dropped left side of `??` | A test now changes each parameter and compares the cache keys | Killed by a new test |
| `ListProducts.Hash`: the format `"D"` of the category changed to `""`, and the four `string.Empty` fallbacks for a missing value changed to other text | The format `D` is the default of a `Guid`, and a missing value gets the same stand-in text every time, so the key of a query stays the same | Equivalent (5) |
| The messages of `ArgumentOutOfRangeException` in `ApplicationError.From`, `ProblemMapping.Map`, and `PermissionPolicy.For` changed to `""` | The tests check the exception type and the parameter, and the message is for a developer who sees the stack | Equivalent (3) |

## Run it again

```bash illustrative
docker compose -f tools/lint/compose.yaml run --rm mutation application
```

The run needs Docker and the network, takes a few minutes, and leaves the reports in `TestResults/mutation/application/reports`. The score of 90.00 percent is from the tests as they were before `BoundaryTests` existed, and it is the run that the survivors above came from.
