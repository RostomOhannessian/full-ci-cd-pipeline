---
title: "ADR-0029: Model the catalog with always-valid value objects, result values, and explicit events, and report mutation scores from Linux"
description: "Decide how the domain model holds its rules, reports a refused rule, records change, and gets proved by example tests, property tests, and a mutation baseline, so WP1.7 to WP1.10 build on a model that is already tested."
status: accepted
date: 2026-10-12
decision-makers: ["@RostomOhannessian"]
supersedes: []
superseded-by: []
tools: [awesomeassertions, cscheck, stryker-net, microsoft-extensions-timeprovider-testing, dotnet-sdk-image]
related-adrs: ["0004", "0017", "0018", "0020", "0023", "0028"]
evidence:
  - src/Catalog.Domain
  - tests/Catalog.Domain.Tests
  - tests/Catalog.Contract.Tests/DomainAgreementTests.cs
  - tests/Catalog.Architecture.Tests/AssemblyReferenceTests.cs
  - docs/reference/domain-model.md
  - docs/testing/evidence/2026-10-12-domain-mutation-baseline.md
---

# ADR-0029: Model the catalog with always-valid value objects, result values, and explicit events, and report mutation scores from Linux

## Context and problem statement

Plan section 8.3 names two aggregates, `Product` and `Category`, five value objects, a lifecycle, four invariants, and eight events. It leaves open how a rule is held, how a refused rule is reported, how a rule that needs a second aggregate is checked,
how time and identifiers reach the model, and how the model is proved. WP1.6 builds the Domain project, and WP1.7 to WP1.10 build the use cases, the persistence, and the endpoints on top of it, so each choice here is paid for by every later package. It serves
`REQ-DOM-001` to `REQ-DOM-005`, `REQ-ARC-001`, `REQ-QUA-004`, `REQ-QUA-005`, and `REQ-QUA-006`.

Facts that shape the choices (checked 2026-10-12):

- `maxLength` and `minLength` in JSON Schema count Unicode characters (code points), and a .NET string counts UTF-16 code units, so an emoji is two units and one character. The contract states its bounds in characters.
- `Guid.CreateVersion7()` returns a UUID version 7 of the RFC 9562 variant. `Guid.Variant` reports the top four bits of the variant byte, so it returns 8 to 11 and not 2, and a check of the variant has to read the two bits that the RFC names.
- A `decimal` keeps the sign of a zero in its bits. A negative zero prints as `0.00`, compares equal to zero, and hashes alike, but `decimal.IsNegative` is true for it, and `-0.00 + 0.00` keeps the sign.
- `TimeProvider` is part of .NET 8 and later, and `Microsoft.Extensions.TimeProvider.Testing` supplies `FakeTimeProvider`.
- CsCheck starts every run from a random seed and names the seed of a failing case. Setting `CsCheck_Seed` replays that case first. A fixed `seed` with `iter` replays only the first iteration, so a pinned seed does not make a whole run repeatable.
- ArchUnitNET 0.13.4 reports a `const` field and an enum member as writable, so the rule against static mutable fields flagged every constant in the first production code.
- Stryker.NET 5.0.0 cannot start on a Windows host that enforces Smart App Control (spike 1.c). Its Microsoft.Testing.Platform runner is a preview, and it initializes static state once per process, so a mutation inside a static initializer never takes effect.

## Decision drivers

- A value that exists is a valid value, so no code downstream needs to check it again.
- A caller that sends ordinary bad input gets a precise answer, and a programming error gets an exception.
- The model depends on the base class library only (`REQ-ARC-001`), so it stays testable without a database, a clock, or a framework.
- The model and the contract state the same bounds, and a test is the only thing that keeps two hand-written documents equal.
- A test result is the same on every host and on every rerun, and a failure can be replayed (`REQ-QUA-004`).
- The strength of the tests is measured, and not guessed from coverage (`REQ-QUA-006`).

## Considered options

1. Always-valid value objects, failures as values, and aggregates that take the collaborating aggregate as an argument (chosen).
2. Primitives in the entities, with validation in the application layer and exceptions for refused rules.

## Decision outcome

Chosen option: **always-valid value objects, failures as values, and aggregates that take the collaborating aggregate as an argument**, because it makes an invalid state impossible to build, it keeps each rule next to the data it protects, and a refused rule becomes a value that the application maps to one row of
the error catalog without a try block.

| Area | Decision | Why |
| --- | --- | --- |
| Value objects | `Sku`, `ProductName`, `CategoryName`, `Description`, `Money`, `Currency`, `ProductId`, and `CategoryId` are sealed records with a private constructor and a static `Create` that returns a `DomainResult`. Equality is by value | A value that exists is valid, and two spellings of one value are one value |
| Failures | A rule that ordinary input can break returns a failed `DomainResult` with a `DomainError` of a code, a field, and a message. A null argument is a programming error and throws | A bad name is an expected outcome, and a null is a bug. The application maps a code to an entry of the error catalog |
| Error fields | A `DomainError.Field` is the JSON path of the value in the version 1 contract, such as `name`, `price.amount`, or `categoryId`, and `Within("price")` nests a path | The application lists the error under `errors` without a translation table, and a contract test checks that each path exists in the request schemas |
| Text | Names are trimmed, one to 100 or 200 characters, one line, and valid Unicode. A description is trimmed and at most 2000 characters, may hold line breaks and tabs, and blank text means none. A SKU is three to 32 ASCII letters and digits with single hyphens, upper-cased, and never trimmed | The contract's `Sku` pattern refuses white space, so the model refuses it too. Length is counted in characters, as the contract counts it |
| Money | A decimal that is not negative, below 10 to the power of 12, with exactly as many decimal places as the currency has. An amount that needs rounding is refused and is never rounded. A zero is rebuilt without its sign | Rounding money silently is a defect. Plan section 8.3 asks for currency-specific precision |
| Currencies | USD, EUR, and GBP with two decimal places, and JPY with none, the set of the contract's `CurrencyCode`, and a test compares the two | Adding a currency is a change to the contract first (ADR-0028) |
| Identifiers | `ProductId` and `CategoryId` accept only a UUID version 7 of the RFC 9562 variant. The aggregates take an identifier and never make one | The `IIdGenerator` port belongs to the application and arrives in WP1.7. The model stays free of a generator, and a test builds identifiers from a counter |
| Time | Every operation that changes state takes a `TimeProvider`, and every event and every `UpdatedAt` uses its `GetUtcNow()` | A test controls the clock, and no test reads the system clock |
| Collaborating aggregates | `Product.Create` and `Product.Update` take the `Category`, and not only its identifier | The compiler stops code that skips the check. The application loads the category first, which it does anyway to answer a missing one |
| Category rule | A product may be created in, or moved to, an active category only. A product that keeps the category that it is in may still change when that category was discontinued after the assignment. The contract text and ADR-0028 say so | Plan section 8.3 says "active when assigned". Without this, a repeated `PUT` would start to fail because someone else discontinued the category |
| Lifecycle | `Active` to `Discontinued` only. A discontinued product refuses a new price and a new category with `ProductDiscontinued` and still changes its name and description. Discontinuing again changes nothing and records nothing | Plan section 8.3 and `REQ-DOM-003` |
| Updates | `Product.Update` replaces the name, the description, the price, and the category in one step. Either every change is accepted or none is. A value that equals the current one records nothing | It is the `PUT` of the contract, and a repeated `PUT` must be harmless |
| Events | Eight sealed records implement `IDomainEvent` with `EventType` (the type name), `SchemaVersion` (1), and `OccurredAt`. An update records one event for each part that changed, in the order details, price, category. A golden list of the properties of each event fails when a shape changes | The outbox (WP1.8) stores them as JSON, so a shape is a contract with the rows already stored. Payload snapshots arrive with the outbox |
| Aggregate base | `AggregateRoot<TId>` holds the identifier and the recorded events, and the persistence layer clears them after it saves | Plan section 8.4 |
| Reference rule | The Domain project has no package and no project reference. A test reads the assembly references from the metadata of the built assemblies and accepts an assembly only when it ships in the .NET runtime directory | The architecture rule matches the namespace `System`, and a package can declare types in it |
| Static state rule | The rule against static mutable fields skips literal fields, which the assembly metadata identifies, because ArchUnitNET reports every constant as writable. The violating fixture still fails the rule | Constants cannot change, and the rule must not flag the first production code |
| Instrumented assemblies | The architecture tests exclude the production assemblies from the coverage extension (`coverage.settings.xml`), and a test fails when an assembly holds a type outside its own namespace. On Linux the extension adds a tracker type to each assembly that has code, and the layer rules then judged that type (found by the first hosted build, 2026-10-12) | The rules must judge what ships. The tests never run production code, so they have no coverage to lose, and the Domain's coverage comes from its own tests |
| Example tests | AwesomeAssertions on xUnit v3, with synthetic builders (`Make`) and identifiers built from a counter (`TestIds`) | FluentAssertions 8 and later have commercial terms (ADR-0004) |
| Property tests | CsCheck tests that state each rule as an exactly-when property against an oracle that shares no code with the model, plus one model-based test of the product lifecycle. A helper runs 500 iterations on one thread and pins no seed | A random seed explores more on each run, and CsCheck prints the seed of a failure so that the run can be replayed |
| Contract agreement | `Catalog.Contract.Tests` references the Domain and compares the bounds, the currency and status sets, the SKU pattern over every short string, the error fields, and the error codes with the contract and the error catalog | The two documents are written by hand in two places |
| Mutation | Stryker.NET 5.0.0 is a local tool, and the `mutation` service of `tools/lint/compose.yaml` runs it in the pinned Linux SDK image on a scratch copy of the repository. The score is reported and never gates a merge, and no nightly job exists yet | The runner is a preview (ADR-0018), and Stryker cannot start on a Windows host that enforces Smart App Control |

### Consequences

- Good: the model has no framework, no clock, no generator, and no database in it. 172 example and property tests run in about five seconds, with 99.6 percent line coverage and 98.4 percent branch coverage of the Domain.
- Good: the first mutation run scored the Domain at 92.38 percent, five new tests raised it to 94.62 percent, and the 12 survivors are classified: seven are equivalent mutants and five are static-initializer mutants that the preview runner cannot apply. The record is in `docs/testing/evidence/2026-10-12-domain-mutation-baseline.md`.
- Good: the contract and the model cannot drift apart without a failing test, and the work found and fixed one wording difference in the contract before any endpoint existed.
- Bad: `Create` returns a result, so every call site unwraps one. Mitigation: WP1.7 maps a result to a response in one place, and the tests use a builder.
- Bad: a category can be discontinued between the load and the save of a product. The rule is the state at the time of the command, as the plan says. Mitigation: the foreign key keeps the category present, and the exposure is one request wide.
- Bad: the Domain carries no persistence attributes, so WP1.8 maps it with configuration classes and needs private constructors that EF Core can call. Mitigation: that is the Infrastructure layer's work, and the architecture rules keep EF Core out of the Domain.
- Neutral: the mutation baseline is a report and a manual run until the runner is generally available. A nightly job needs the default branch, so it is a follow-up and not part of this package.

### Confirmation

- **WP1.6** delivers the model, the tests, the contract agreement, the reference test, and the mutation baseline. `REQ-DOM-001` to `REQ-DOM-005` stay in progress, because the identifier generator, the persistence of events, and the endpoints arrive in WP1.7, WP1.8, and WP1.10.
- `tests/Catalog.Domain.Tests` fails when a rule changes, `tests/Catalog.Contract.Tests/DomainAgreementTests.cs` fails when the model and the contract disagree, and `tests/Catalog.Architecture.Tests/AssemblyReferenceTests.cs` fails when the Domain references anything but the base class library.
- The Domain thresholds of 90 percent of lines and 80 percent of branches are checked by `tools/ci/Test-CoverageThresholds.ps1` in the `ci` workflow.
- **WP1.7** adds the identifier generator and the use cases that map a `DomainError` to the error catalog, and **WP1.8** stores the events and checks their payloads against snapshots.

## Pros and cons of the options

### Always-valid value objects, failures as values, and aggregates that take the collaborating aggregate as an argument

- Good: an invalid state cannot be built, and the rules are tested without any infrastructure.
- Good: a refused rule is data, so the application and the tests handle it without a try block.
- Bad: more types and more unwrapping than a model of primitives.

### Primitives in the entities, with validation in the application layer and exceptions for refused rules

- Good: fewer types, and a validation library can express most rules.
- Bad: every path that builds an entity must remember to validate, and a missed one stores bad data.
- Bad: an exception for ordinary bad input is slow, and it hides the rule that failed behind a catch.

## Revisit when

- WP1.8 finds that an aggregate cannot be mapped without a change to its shape, and the change is made here first.
- Stryker.NET's Microsoft.Testing.Platform runner becomes generally available, so the score can become a gate, and static-initializer mutants can be applied.
- A client needs a currency, a precision, or a text bound that the contract does not allow, and the contract and the model change together.
- The maintainer approves a nightly mutation job, or a mutation score below 70 percent appears in a report.

## Further reading

- [RFC 9562: Universally Unique IDentifiers (UUIDs)](https://www.rfc-editor.org/rfc/rfc9562.html) (checked 2026-10-12)
- [JSON Schema validation: minLength and maxLength count characters](https://json-schema.org/draft/2020-12/json-schema-validation#name-maxlength) (checked 2026-10-12)
- [TimeProvider and ITimer in .NET](https://learn.microsoft.com/dotnet/core/extensions/timeprovider) (checked 2026-10-12)
- [Stryker.NET introduction](https://stryker-mutator.io/docs/stryker-net/introduction/) (checked 2026-10-12)
- [CsCheck repository](https://github.com/AnthonyLloyd/CsCheck) (checked 2026-10-12)
