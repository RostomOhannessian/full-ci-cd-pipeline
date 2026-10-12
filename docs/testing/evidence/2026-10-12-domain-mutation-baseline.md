---
title: "Evidence record: domain mutation baseline (2026-10-12)"
description: "A dated record of the first Stryker.NET mutation run on the domain model: the score, how the 17 first survivors were triaged, a planted defect that lowered the score, and how to run it again."
type: evidence-record
date: 2026-10-12
status: verified
audience: [maintainers, contributors]
last-verified: 2026-10-12
owner: "@RostomOhannessian"
---

# Evidence record: domain mutation baseline (2026-10-12)

This record is the manual evidence for `REQ-QUA-006`. Stryker.NET 5.0.0 mutated the `Catalog.Domain` project, ran the 172 tests of `tests/Catalog.Domain.Tests` against each mutant, and scored the tests at **94.62 percent**. The plan asks for a score of at least 70 percent before the score can become a gate ([plan section 10.4](../../plans/implementation-plan.md)), and [ADR-0018](../../adr/0018-test-stack-and-quality-gates.md) keeps it a report until the Microsoft.Testing.Platform runner is generally available.

## Scope and limits

- The run was made on the author's Windows x86-64 machine with Docker Desktop 29.8.2, through the `mutation` service of `tools/lint/compose.yaml`. The service runs Stryker.NET in `mcr.microsoft.com/dotnet/sdk:10.0.401`, pinned by digest `sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317`, because Stryker.NET cannot start on a Windows host that enforces Smart App Control (spike 1.c).
- It covers `Catalog.Domain` only. The Application project has no code until WP1.7, so its baseline arrives then.
- Stryker's Microsoft.Testing.Platform runner is a preview and says so in its log, so the figures are a report to read and not a result to enforce.
- No hosted runner took part. A nightly job needs the default branch, so it is a follow-up and not part of WP1.6.
- `REQ-QUA-006` stays `in_progress` in [the requirements register](../../requirements/requirements.yaml), because the Application baseline (WP1.7) and the gate (WP4.8) remain.

## Result

| Measure | First run | Final run |
| --- | --- | --- |
| Tests found | 167 | 172 |
| Mutants created | 266 | 266 |
| Ignored by the block-already-covered filter | 41 | 41 |
| Compile errors | 2 | 2 |
| Mutants tested | 223 | 223 |
| Killed | 206 | 211 |
| Survived | 17 | 12 |
| Mutation score | 92.38 percent | **94.62 percent** |

The score is the killed mutants divided by the killed, the survived, and the uncovered ones (no mutant was uncovered in either run). Stryker itself took 19 seconds, and the service took 44 seconds from start to finish with the image and the packages cached. The console table's `# survived` column counts the skipped mutants too, so the figures above come from the JSON report (`TestResults/mutation/reports/mutation-report.json`, which Git ignores).

## The survivors

The first run left 17 survivors. Five were weaknesses in the tests, and five new tests killed them. The other 12 remain, and each is accounted for.

| Mutant | Why it survives | Class |
| --- | --- | --- |
| `Money.WithScale`: `value == 0m` changed to false, and the `isNegative: false` of the zero changed to true | A negative zero printed as `0.00` and compared equal, so no test saw the sign bit. `decimal.IsNegative` now does | Killed by a new assertion |
| `Product.Create`: the null check of the identifier removed | The category check returns first for a discontinued category, so only that guard throws. A test now covers it | Killed by a new test |
| `AggregateRoot`: the null check of the identifier and of the event removed | No test built an aggregate directly. A probe subclass now does | Killed by two new tests |
| `Category.Create`: the null check of the identifier removed | The constructor of the base class repeats the check with the same exception and parameter name | Equivalent |
| `CategoryId.ToString` and `ProductId.ToString`: the format `"D"` changed to `""` | An empty format string is the format `D` for a `Guid` | Equivalent (2) |
| `Uuid7.IsValid`: `>>` changed to `>>>` on `bytes[6]` | The value is a byte, which is never negative, so both operators give the same result | Equivalent |
| `Money.WithScale`: `value + 0.00` changed to `value - 0.00`, and the `isNegative: false` of the non-zero branch changed to true | Adding or subtracting a zero of either sign leaves a non-zero amount unchanged | Equivalent (2) |
| `Sku`: the initial `previousWasHyphen = false` changed to true | The check on the first character refuses a leading hyphen before the loop runs | Equivalent |
| `Currency`: the constructor body removed, and the four code strings changed to `""` | Static initializers run once per test process, and the preview runner cannot apply a mutant to them | Runner limit (5) |

The five `Currency` survivors are not a gap in the tests. One of them was applied by hand, with `new("USD", 2)` changed to `new("", 2)` in the source, and many tests failed, including the check of the four accepted currencies. The change was then reverted.

## A planted defect

To show that the run reports weak tests, the two files `ProductTests.cs` and `ProductLifecyclePropertyTests.cs` were moved out of the test project, and the service ran again with 150 tests (the 171 of that run, less the 21 in the two files). The score fell from 94.62 to 81.61 percent: 182 mutants were killed, 29 survived, and 12 had no test that covered them. Of those 41 weak mutants, 29 were in `Product.cs` and the other 12 were the survivors above, and the report listed them. The files were put back, and the final run above is the one without the defect.

## Run it again

Docker must be running. From the repository root:

```bash illustrative
docker compose -f tools/lint/compose.yaml run --rm mutation
```

The service copies the repository to a scratch directory, restores it in locked mode, runs Stryker.NET there, and leaves `reports/mutation-report.json` and `reports/mutation-report.md` in `TestResults/mutation`. To list the survivors:

```bash illustrative
pwsh -NoProfile -Command "(Get-Content TestResults/mutation/reports/mutation-report.json -Raw | ConvertFrom-Json).files.PSObject.Properties | ForEach-Object { foreach ($m in $_.Value.mutants | Where-Object status -eq 'Survived') { '{0}:{1} {2}' -f $_.Name, $m.location.start.line, $m.mutatorName } }"
```

Read [the Stryker.NET page](../../reference/tools/stryker-net.md) for how to read the report, and [ADR-0029](../../adr/0029-domain-model-and-mutation-baseline.md) for why the score is a report.
