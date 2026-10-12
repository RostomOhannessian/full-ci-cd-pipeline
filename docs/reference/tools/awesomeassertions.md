---
title: "AwesomeAssertions"
description: "The fluent assertion library of the domain tests: an Apache-2.0 community fork of FluentAssertions 7, pinned at 9.6.0 so that FluentAssertions 8 and later stay out of the repository."
audience: [learners, contributors]
tier: B
tools: [awesomeassertions]
introduced: "phase-1"
prerequisites: [".NET SDK 10.0.401, which global.json pins", "A clone of the repository and basic knowledge of xUnit tests"]
estimated-time: "20 minutes"
last-verified: 2026-10-12
verified-against: { awesomeassertions: "9.6.0" }
owner: "@RostomOhannessian"
---

# AwesomeAssertions

## Overview

A failed test should say what it expected and what it found, so that nobody opens a debugger to read a boolean. AwesomeAssertions is a set of .NET extension methods that read as a sentence and write that message for you.
After `using AwesomeAssertions;`, every value has a `Should()` method, and the rest of the call states the expectation (checked 2026-10-12 in the [introduction](https://awesomeassertions.org/introduction)). The library is a helper and not a runner. xUnit v3 on Microsoft.Testing.Platform, which [global.json](../../../global.json) selects, finds and runs the tests, and a failed assertion throws an exception that the runner reports as a failed test. The project lists xUnit v3 among the frameworks it supports (checked 2026-10-12 on [awesomeassertions.org](https://awesomeassertions.org/)).

| Concept | What it is | Example from this repository |
| --- | --- | --- |
| Subject | `Should()` wraps the value under test and returns the assertions that fit its type | `result.IsSuccess.Should()` |
| Assertion | A method that checks one thing and throws when it fails | `BeTrue()`, `Be(...)`, `Equal(...)`, `ContainSingle()` |
| Chain | `.And` stays on the subject, and `.Which` moves to the item or property that the assertion selected | `product.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ProductDetailsChanged>()` |
| Because | The optional last argument of every assertion. The text goes into the failure message | `BeInRange(8, 11, "that is how .NET reports the RFC 9562 variant")` |

## Decision rationale

[ADR-0004](../../adr/0004-licensing-and-dependency-license-policy.md) forbids a linked dependency with commercial terms, and FluentAssertions 8 and later have them. [ADR-0018](../../adr/0018-test-stack-and-quality-gates.md) and [ADR-0029](../../adr/0029-domain-model-and-mutation-baseline.md) record the move to this fork.

| Option | Used here | Reason |
| --- | --- | --- |
| AwesomeAssertions 9.6.0 | Yes, in `Catalog.Domain.Tests` | A fork of FluentAssertions 7 that the community controls, under Apache-2.0 (checked 2026-10-12 in the [README](https://github.com/AwesomeAssertions/AwesomeAssertions)) |
| FluentAssertions 8 and later | Forbidden | Commercial terms. [forbidden-packages.json](../../../governance/policies/forbidden-packages.json) lists it, and a test shows that 8.0.0 is rejected and 9.6.0 is not |
| xUnit `Assert` | Yes, in the other test projects | It needs no dependency. `Catalog.Contract.Tests`, `Catalog.Architecture.Tests`, and `Governance.Auditor.Tests` use it |

- **License:** Apache-2.0 on the [NuGet license page](https://www.nuget.org/packages/AwesomeAssertions/9.6.0/license) (checked 2026-10-12), which is on the allow-list of ADR-0004. The `nuget-license` gate scans it with the rest of the solution.
- **Maintenance:** a community runs the fork and no company does, which is its purpose and its risk. Assertions live in test code, so a project that goes quiet would cost a rewrite of the tests and would not touch production.

## Setup tutorial

**Prerequisites:** the .NET SDK that `global.json` pins. There is nothing to install, because the locked restore fetches the package. The commands run from the repository root, and `dotnet test --project` uses the Microsoft.Testing.Platform runner.

1. Run the tests that have "Money" in their names. The summary is the part to read.

   ```bash illustrative
   dotnet test --project tests/Catalog.Domain.Tests --configuration Release --filter-method "*Money*"
   ```

   Expected output, without the `duration` line, which varies (checked 2026-10-12; the whole project has 172 tests when you leave out `--filter-method`):

   ```text
   Test run summary: Passed!
     total: 38
     failed: 0
     succeeded: 38
     skipped: 0
   ```

2. Read one test. It is in [DomainResultTests.cs](../../../tests/Catalog.Domain.Tests/DomainResultTests.cs). The first two assertions check values, and the last one wraps a property read in a delegate, because the library has to run it to see the exception.

   ```csharp
   var result = DomainResult.Failure<int>(SomeError);

   result.IsSuccess.Should().BeFalse();
   result.Error.Should().Be(SomeError);

   var read = () => result.Value;
   read.Should().Throw<InvalidOperationException>().WithMessage("*Invalid*'name'*Check IsSuccess first*");
   ```

3. Break a test on purpose. In `The_accepted_currencies_are_the_four_of_the_contract_in_a_fixed_order`, in [MoneyTests.cs](../../../tests/Catalog.Domain.Tests/MoneyTests.cs), swap the last two codes to `"USD", "EUR", "JPY", "GBP"`, and run the class.

   ```bash illustrative
   dotnet test --project tests/Catalog.Domain.Tests --configuration Release --filter-class "*CurrencyTests"
   ```

   Expected output, with exit code 2. The message names the expression under test and the first index that differs. The listing leaves out the library's stack frames:

   ```text
   failed Catalog.Domain.Tests.CurrencyTests.The_accepted_currencies_are_the_four_of_the_contract_in_a_fixed_order (126ms)
     Expected Currency.All.Select(currency => currency.Code) to be equal to {"USD", "EUR", "JPY", "GBP"}, but {"USD", "EUR", "GBP", "JPY"} differs at index 2.
       at Catalog.Domain.Tests.CurrencyTests.The_accepted_currencies_are_the_four_of_the_contract_in_a_fixed_order() in ...MoneyTests.cs:17

   Test run summary: Failed!
     total: 14
     failed: 1
     succeeded: 13
   ```

4. Read a reason. Undo the edit of step 3, change `BeInRange(8, 11,` to `BeInRange(0, 7,` in [IdentifierTests.cs](../../../tests/Catalog.Domain.Tests/IdentifierTests.cs), and run the command of step 3 with `--filter-class "*IdentifierTests"`. The run fails with exit code 2 and 1 of 12 tests failed, and the `because` text sits in the middle of the message: `Expected TestIds.Category(7).Value.Variant to be between 0 and 7 because that is how .NET reports the RFC 9562 variant, but found 8.`
**Cleanup:** undo the edit of step 4. Nothing else is left behind.

## How this project uses it

Only test projects reference the package. [Directory.Packages.props](../../../Directory.Packages.props) pins `AwesomeAssertions` at 9.6.0 for the whole repository, and [Catalog.Domain.Tests.csproj](../../../tests/Catalog.Domain.Tests/Catalog.Domain.Tests.csproj) has `<PackageReference Include="AwesomeAssertions" />` without a version (central package management). Today 11 files in `tests/Catalog.Domain.Tests` use it (checked 2026-10-12). The production projects do not, and the reference rules of the architecture tests allow the Domain project no package at all ([ADR-0029](../../adr/0029-domain-model-and-mutation-baseline.md)). The calls below are the ones that the domain tests repeat:

| What the test proves | Call | File |
| --- | --- | --- |
| A rule failed with this exact error. `Be` compares records by value, which the value objects rely on | `result.Error.Should().Be(DomainError.Invalid("currency", "The currency must be one of USD, EUR, GBP, JPY."));` | [MoneyTests.cs](../../../tests/Catalog.Domain.Tests/MoneyTests.cs) |
| A programming error throws and names its parameter | `create.Should().Throw<ArgumentNullException>().WithParameterName("id");` | [AggregateRootTests.cs](../../../tests/Catalog.Domain.Tests/AggregateRootTests.cs) |
| A list holds these items in this order | `Currency.All.Select(currency => currency.Code).Should().Equal("USD", "EUR", "GBP", "JPY");` | [MoneyTests.cs](../../../tests/Catalog.Domain.Tests/MoneyTests.cs) |
| An aggregate recorded one event of one type | `product.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ProductDetailsChanged>();` | [ProductTests.cs](../../../tests/Catalog.Domain.Tests/ProductTests.cs) |

## Validation and troubleshooting

Check the resolved version with `dotnet list tests/Catalog.Domain.Tests/Catalog.Domain.Tests.csproj package`, which prints `> AwesomeAssertions  9.6.0  9.6.0` for the requested and the resolved version. Then run `dotnet test --project tests/Catalog.Architecture.Tests --configuration Release --filter-class "*DependencyPolicyTests"`, which passed with 18 tests on 2026-10-12 and rejects a forbidden package version in any lock file.

| Symptom | Cause | Fix |
| --- | --- | --- |
| `error CS0246: The type or namespace name 'FluentAssertions' could not be found` | The code or a copied example uses the old namespace | Write `using AwesomeAssertions;` |
| A failure shows values and no explanation | The assertion has no reason | Pass a `because` text as the last argument, as in `properties.Should().Equal(expected, "because ...")` in [DomainEventTests.cs](../../../tests/Catalog.Domain.Tests/DomainEventTests.cs) |
| The build fails with CA1861 on an array passed to `Equal` with a `because` text | The analyzer rejects a constant array argument, and warnings are errors | List the items as `params` without a reason, or keep the array in a `static readonly` field, as `DomainEventTests` does |
| The build fails with CA1826 on `.Last()` | The analyzer prefers an indexer on a list | Write `category.DomainEvents[^1].Should()...` |
| A test with a lone UTF-16 surrogate in `[InlineData]` sees U+FFFD | The data of a theory goes through UTF-8 on its way to the runner | Build the string inside the test, as [NameTests.cs](../../../tests/Catalog.Domain.Tests/NameTests.cs) does |
| `Should().Be` passes for two different objects | `Be` uses `Equals`, which compares records by value | Use `BeSameAs` to ask for the same instance |

The project's [tips page](https://awesomeassertions.org/tips/) adds one rule: put `Should()` as early as you can, so that the message names more than "True" and "False" (checked 2026-10-12).

## Security and operations

The library runs inside the test process, with no credentials, no network access at run time, and no part in a production image. The restore fetches it from NuGet, and `packages.lock.json` records the hash of the resolved package, so a locked restore fails when the package changes. Dependabot proposes version updates to `Directory.Packages.props`. A major version needs the license check again, which the `nuget-license` gate and the forbidden-package test provide. If a release were compromised, it would run with the permissions of a developer or of the `ci` workflow, and the repository's default token is read-only.

## Lab

None yet. WP1.13 writes the Phase 1 labs and concept pages (see the [lab index](../../labs/index.md)), and none of the planned labs teaches assertions. Step 3 and step 4 of the tutorial are the hands-on exercise for this tool.

## Further research

- [Introduction](https://awesomeassertions.org/introduction): the first assertions, chaining with `.Which`, and the global configuration (checked 2026-10-12)
- [About](https://awesomeassertions.org/about/): why failure messages matter, the `because` argument, and the supported frameworks (checked 2026-10-12)
- [Tips](https://awesomeassertions.org/tips/): weaker assertions next to the ones that give better messages (checked 2026-10-12)
- [The AwesomeAssertions repository](https://github.com/AwesomeAssertions/AwesomeAssertions): the license, the reason for the fork, and the releases (checked 2026-10-12)
