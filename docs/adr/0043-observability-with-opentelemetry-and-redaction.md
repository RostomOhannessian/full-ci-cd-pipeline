---
title: "ADR-0043: Observe the API with OpenTelemetry in the composition root, redact by classification, and keep every label a fixed word"
description: "Decide how the API emits traces, metrics, and logs: sources and meters in the layers, the SDK and its two pinned beta instrumentations in the composition root only, an adapter of the telemetry port over a span and a histogram, redaction by classification before any exporter, OTLP through the standard variables, and metric labels that never name an identifier."
status: accepted
date: 2026-11-04
decision-makers: ["@RostomOhannessian"]
supersedes: []
superseded-by: []
tools: [opentelemetry-dotnet, microsoft-extensions-compliance-redaction, fusioncache, efcore, otel-lgtm]
related-adrs: ["0004", "0012", "0020", "0026", "0032", "0035", "0036", "0037", "0038", "0039", "0040"]
evidence:
  - src/Catalog.Api/Composition/TelemetryComposition.cs
  - src/Catalog.Api/Telemetry/TelemetryClassification.cs
  - src/Catalog.Api/Telemetry/RedactionProcessors.cs
  - src/Catalog.CrossCutting/Telemetry/UseCaseTelemetry.cs
  - src/Catalog.Infrastructure/Outbox/OutboxMetrics.cs
  - tests/Catalog.Api.Tests/TelemetryTraceTests.cs
  - tests/Catalog.Api.Tests/TelemetryMetricsTests.cs
  - tests/Catalog.Api.Tests/TelemetryLogTests.cs
  - tests/Catalog.Api.Tests/TelemetryOptionsTests.cs
  - docs/testing/evidence/2026-11-04-observability-run.md
---

# ADR-0043: Observe the API with OpenTelemetry in the composition root, redact by classification, and keep every label a fixed word

## Context and problem statement

Plan section 8.11 asks for traces, metrics, and logs through OpenTelemetry, with `Microsoft.Extensions.Compliance.Redaction` classifications so that tokens, secrets, and personal data never appear, and WP1.4 to WP1.10 left the hooks: the meters `Catalog.Cache`, `Catalog.Outbox`, and `Catalog.Credentials` publish measurements that nobody listens to, `LoggingUseCaseTelemetry` is an interim adapter of the telemetry port ([ADR-0032](0032-decorator-pipeline-and-application-results.md)), and the trace identifier of an error comes from a fallback activity that the request middleware starts when no listener exists ([ADR-0040](0040-http-api-composition-and-token-validation.md)).
WP1.11 turns the hooks into a pipeline. These questions were open:

- **Where the SDK lives.** Domain depends on the base class library only, and the layers must stay testable without a collector.
- **How a span and a metric of a use case are made behind the port**, without a value of a request or the text of an exception reaching either.
- **Whether to take the two beta instrumentations** (EF Core and StackExchange.Redis), as the plan says, or to write spans by hand.
- **How to redact.** The plan names the package, and the package classifies and replaces values. It does not decide what is sensitive in an attribute that a library chose.
- **How to configure the exporter**, so that a developer, a test, and a deployment each get what they need without a secret in a file.
- **How to keep the metric series bounded**, because a label that holds an identifier makes a series for each product and costs money in a hosted backend.

This ADR serves REQ-OBS-001, REQ-OBS-002, REQ-OBS-003, REQ-SEC-006, and REQ-CACHE-003.

Facts that shape the decision (checked 2026-11-04):

- OpenTelemetry .NET 1.20.1 is the newest stable release of the SDK, the OTLP exporter, the hosting package, and the instrumentation of ASP.NET Core, HttpClient, SqlClient, and the runtime (nuget.org, published 2026-10-20 and 2026-10-21). The instrumentation of EF Core and of StackExchange.Redis has only had beta releases, and 1.20.1-beta.1 is the newest of each. All of them are Apache-2.0 licensed (the license expression of each nuspec).
- `Microsoft.Extensions.Compliance.Redaction` 10.11.0 is MIT licensed and depends on `Microsoft.Extensions.Compliance.Abstractions` 10.11.0, which holds `DataClassification`, `Redactor`, and `IRedactorProvider`.
- FusionCache 2.9.0 publishes its spans and metrics through `ActivitySource` and `Meter` of the base class library. The package `ZiggyCreatures.FusionCache.OpenTelemetry` only calls `AddSource` with the names that the core package exposes as constants. The spans of FusionCache carry the cache key, and its metrics carry the name of the cache and never the key or the instance.
- The instrumentation of StackExchange.Redis profiles one `IConnectionMultiplexer` at a time, and `RedisCache` and `RedisBackplane` each open their own and accept a factory for it.
- The exporters read `OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_EXPORTER_OTLP_PROTOCOL`, `OTEL_EXPORTER_OTLP_HEADERS`, and `OTEL_RESOURCE_ATTRIBUTES` themselves, and the headers can carry a credential.
- The OTLP exporter writes an exception of a log record as `exception.type`, `exception.message`, and `exception.stacktrace`, and the message of a driver can hold a server name, a login, or a value.
- A framework logs the profile folder of the user that runs it (`C:\Users\<name>\...`) at Information level in the ASP.NET Core data protection category.

## Decision drivers

- A failure is followed from the error answer to the trace by one identifier, and the trace shows which call made which.
- No log record, span tag, or metric label holds a token, a credential, a connection string, a password, or personal data, and a test proves it against the output of the real host.
- The layers publish telemetry without the SDK, so Domain and Application stay free of it and a test of a layer needs no exporter.
- Every label of a metric is a fixed word, so the series stay bounded when the catalog grows.
- A beta dependency is a pin that a person reads again on every upgrade, and the pin says that it is a beta.
- No dependency asks for a declaration, a fee, or a license exception (ADR-0004).

## Considered options

1. Sources and meters in the layers, the SDK in the composition root, the two beta instrumentations pinned, redaction by classification in processors before every exporter (chosen).
2. The SDK in every layer that emits telemetry.
3. Spans written by hand for EF Core and for Valkey, in place of the beta instrumentations.
4. Redaction at the call site with the classification attributes of `Microsoft.Gen.Logging` and `Microsoft.Extensions.Telemetry`.

## Decision outcome

Chosen option: **sources and meters in the layers, the SDK in the composition root, and redaction in processors**, because it keeps the SDK out of the inner layers (REQ-ARC-001), follows the plan on the beta instrumentations, and redacts the output of libraries that a call-site attribute cannot reach.

| Area | Decision | Why |
| --- | --- | --- |
| Where the SDK is | Only `Catalog.Api` references the OpenTelemetry packages and the redaction package. `ReferencePolicy` forbids them in Infrastructure and CrossCutting (and the Application rule already forbids `OpenTelemetry*`), with a violating fixture for each. The layers create an `ActivitySource` and a `Meter` of the base class library, and the composition root names them in `TelemetryComposition` | The listener model of the base class library needs no SDK on the side that publishes |
| The telemetry port | `UseCaseTelemetry` in `Catalog.CrossCutting.Telemetry` implements `IUseCaseTelemetry` over the source and the meter `Catalog.UseCases`. A run is one span named after the use case, with the tags `catalog.use_case`, `catalog.use_case.kind`, `catalog.outcome`, and for a refusal `catalog.refusal`, and one measurement of the histogram `catalog.usecase.duration` (seconds) with the labels `use_case`, `kind`, and `outcome` (`success`, `refused`, `fault`, `canceled`). A fault sets the status to error and the tag `error.type`, and never adds the exception, so its text never reaches the span. A cancellation is not a fault. The clock is the injected `TimeProvider`. `LoggingUseCaseTelemetry` is removed, and the one registration changes | One adapter behind the port, and a test that sets the duration |
| Other sources and meters | The outbox owns `Catalog.Outbox` (spans `outbox.process_batch`, `outbox.deliver`, and `outbox.handle`, and the counters and histograms of delivery), the credential rotation owns `Catalog.Credentials` (spans `credentials.rotate` and `credentials.clear_pool`), the idempotency store owns `Catalog.Idempotency` (the counter `catalog.idempotency.decisions` by outcome), and the cache keeps `Catalog.Cache`. An empty pass of the outbox starts no span. The writing node delivers in process inside the span of the request, so the handler is a child of the use case | The links the plan describes, and no trace for an idle poll |
| The outbox gauges | `catalog.outbox.backlog`, `catalog.outbox.dead_letters`, `catalog.outbox.oldest_pending_age`, and `catalog.outbox.observation_age` read a reading that the processor takes at most once for each `ObservationInterval` (30 seconds by default): one count over the filtered index of the pending messages, one seek for the oldest due time, and one count of the dead letters. A dead letter that this node makes is added to the reading at once. The age of the oldest message is its time since it became due, so it is zero while a message waits for a backoff, and it grows while the processor is stuck. The age of the reading grows while the database cannot be read, so an alert can fire for that case too. A gauge reports nothing before the first reading | A gauge must not query the database in a callback, and a series that does not exist must not read as zero |
| FusionCache | The traces include its operations, its shared level, and its backplane, and the metrics include all four of its meters (the hits, the misses, and the stale hits among them). `CacheTelemetryNames` in the caching namespace of CrossCutting names them, so the composition root never names the library. The package `ZiggyCreatures.FusionCache.OpenTelemetry` is not added, because it only calls `AddSource` | One package less, and the rule that only the caching namespace names FusionCache holds |
| The beta instrumentations | `OpenTelemetry.Instrumentation.EntityFrameworkCore` and `OpenTelemetry.Instrumentation.StackExchangeRedis` are used at 1.20.1-beta.1 and pinned exactly in `Directory.Packages.props`. `TelemetryPackageTests` fail when either pin is a range, is a stable version, or is not on the release of the SDK. The instrumentation of Redis needs each connection object, so `ValkeyConnection` takes an optional `ValkeyConnectionObserver` and gives the cache and the backplane a factory that opens the connection as before and reports it. The observer passes a connection to the instrumentation whichever comes first, and never twice. The statements of EF Core and of SqlClient carry placeholders and never a parameter value (`SetDbQueryParameters` stays off, and a test searches the spans) | The plan labels both as beta, and a hand-written span would copy what the packages maintain |
| Traces of the request | The server span of ASP.NET Core is the root, so the trace identifier of an error is the identifier of the trace (`RequestTrace.IdOf` reads `Activity.Current`), and the fallback activity of the middleware remains for a host with no listener. The sampler is parent-based over a ratio (`Catalog:Telemetry:TraceSamplingRatio`, default 1), and an unsampled request still has an identifier. The health endpoints are left out of the traces, because a probe every few seconds would be most of them | A failure is followed from the answer to the trace |
| Configuration | `Catalog:Telemetry` holds the exporter (`Otlp` by default, or `None`), the service name, and the sampling ratio. The endpoint, the protocol, the headers, and the resource attributes come from the standard `OTEL_*` variables, which the SDK reads itself, so `appsettings.Development.json` names no endpoint and the defaults reach the Compose stack. The validator checks the options and the endpoint and protocol variables when the host starts, and a problem names a variable and never repeats a value. A test host sets the exporter to `None` | A deployment sets the variables as it does for any service, and the headers never enter a file |
| Redaction | `CatalogTaxonomy` defines two classes, `Secret` and `Personal`, and `AddRedaction` maps both to `MarkerRedactor`, which replaces a value with `[redacted]`. `TelemetryClassifier` classifies an attribute by its name (a word such as `password`, `token`, `authorization`, `cookie`, `connection_string`, `email`, or `client.address`) and a text value by its shape (a JSON web token, a bearer or basic credential, a connection string, an email address, or the profile folder of a person). The processors `RedactingActivityProcessor` and `RedactingLogProcessor` run first, so every exporter that is added after them sees the redacted value. A log record never leaves with its exception: the processor removes it and adds `exception.type` and the frames of the stack without the file paths. The formatted message is not exported, because the attributes hold every value of the template | The package replaces a value, and a classifier is what says which one, for an attribute that a library chose |
| Labels | A label of a catalog metric is a fixed word, the stable name of a handler, or an event type, and never an identifier, a key, a caller, or a text of a request. `TelemetryMetricsTests` search every exported series for a label that is not in a documented list and for a value that has the shape of an identifier, and they write many products to show that no series is added | The series stay bounded |

### Consequences

- Good: a request is one trace from the HTTP request through the use case, the cache, the database, and Valkey to the outbox handler, and an error answer names that trace.
- Good: a secret, a token, or an address that a library puts in a tag or a log record is replaced before an exporter sees it, and a test sends sentinels through the real host to show it.
- Good: the layers need no collector, and `Exporter=None` makes a host that sends nothing.
- Bad: the redaction lists lean toward redacting too much, so a harmless attribute can lose its value (the address of a peer in a Redis span is one). Mitigation: a test names the attributes that must stay readable, and a change to a list is a pull request.
- Bad: the text of an exception never leaves the process, so a fault shows its type and its frames and not its message. Mitigation: the log of the host keeps the full exception, and the trace identifier joins the two.
- Bad: the standard output of the host is not redacted, because it does not pass through the OpenTelemetry pipeline. Mitigation: the code never logs a value that the plan forbids (a rule since WP1.8, and tests search every line), and the threat model records the limit.
- Bad: EF Core and SqlClient both make a span for a statement, so a trace has two spans for it. Mitigation: they are parent and child, and the child shows the network call.
- Bad: the poll of the outbox makes a root span for its claim statement every few seconds. Mitigation: the sampling ratio applies, and a later work package can suppress it once the plan decides how an idle poll should look.
- Neutral: a message of the outbox does not carry the trace of the request that wrote it, so a delivery by the processor starts a trace of its own. Linking it needs a column in the outbox table, which is an expand migration that this work package does not make.
- Neutral: the activity listeners and the metric streams belong to the process, so the tests of the telemetry share them when several hosts live in one test process. The instrumentations of EF Core and SqlClient then record a command twice, the sampler of one host is not isolated from the listener of another, and a metric stream that two hosts share by name can lose its series when one of them disposes its meters. A production process has one host. The tests that need the series of the catalog or a sampling ratio of zero run alone in a collection that disables parallelization, and the snapshot of the trace shape leaves out a span below a span of its own kind (checked 2026-11-04).

### Confirmation

- `TelemetryTraceTests` start a trace with a `traceparent` header and read the spans of the real host over SQL Server and Valkey: the request, the use case, the cache, EF Core, SqlClient, Valkey, and the outbox handler, with their parents. They also prove that the identifier of an error is the trace of the request, that a health probe leaves no trace, and that no value of a request reaches a span.
- `TelemetryMetricsTests` read the exported series, the labels, and the bounds, `TelemetryCacheDegradationTests` show the gauges and the readiness of a host whose Valkey is down, and `TelemetrySamplingTests` show that a ratio of zero records no trace and keeps the trace identifier.
- `TelemetryLogTests` and `TelemetryRedactionTests` send sentinels through the real host and the processors, and `ErrorSnapshotTests`-style snapshot `TelemetryLogTests.The_redacted_log_of_a_fault_has_the_shape_that_the_snapshot_holds` holds the shape of a redacted record.
- `TelemetryOptionsTests` prove the validation, the OTLP export to a listener on the loopback address, and the off switch. `TelemetryPackageTests` hold the pins of the beta packages.
- The architecture tests (`ReferenceRulesTests`) reject the SDK in Infrastructure, CrossCutting, and Application.
- `OutboxTelemetryTests`, `IdempotencyTelemetryTests`, `UseCaseTelemetryTests`, `ValkeyConnectionObserverTests`, and `CredentialRotationTests` prove the components with a fake clock.

## Pros and cons of the options

### Sources and meters in the layers, the SDK in the composition root

- Good: the layers are testable with a listener of the base class library, and the architecture tests keep the SDK out of them.
- Bad: the composition root must name every source and meter, so a new one that nobody names is silent. Mitigation: the tests list the names that the host exports.

### The SDK in every layer

- Good: a layer can configure its own instrumentation.
- Bad: Infrastructure and CrossCutting would depend on a package that Domain and Application must not know, and a test of a layer would build a provider.

### Spans written by hand for EF Core and for Valkey

- Good: no beta dependency.
- Bad: a copy of what the packages maintain, for the statements and for the commands, and the plan names the beta packages (REQ-OBS-001).

### Redaction at the call site

- Good: a value is classified where it is written.
- Bad: it needs the generator of `Microsoft.Gen.Logging` and the replacement of the logger factory, it covers only the calls that someone annotated, and it cannot reach what a framework or a driver logs.

## Revisit when

- The instrumentation of EF Core or of StackExchange.Redis has a stable release, or a release that changes its attributes (the semantic conventions of databases are moving).
- The plan decides how a message of the outbox carries the trace of its request (a column and a link), or how an idle poll should be traced.
- The collector of Phase 3 (WP3.7) redacts attributes of its own, and the two lists must agree.
- A hosted backend prices series, and the limits of cardinality must be set in views.
- A new log source writes a value that no word of the lists catches.

## Further reading

- [OpenTelemetry .NET: getting started with ASP.NET Core](https://opentelemetry.io/docs/languages/dotnet/getting-started/): the registration that this decision follows (checked 2026-11-04)
- [OpenTelemetry .NET: logs](https://opentelemetry.io/docs/languages/dotnet/logs/): the logs pipeline and its processors (checked 2026-11-04)
- [OTLP exporter configuration](https://opentelemetry.io/docs/languages/sdk-configuration/otlp-exporter/): the standard variables (checked 2026-11-04)
- [StackExchange.Redis instrumentation](https://github.com/open-telemetry/opentelemetry-dotnet-contrib/tree/main/src/OpenTelemetry.Instrumentation.StackExchangeRedis): the beta package and how it takes a connection (checked 2026-11-04)
- [Data redaction in .NET](https://learn.microsoft.com/en-us/dotnet/core/extensions/data-redaction): classifications and redactors (checked 2026-11-04)
- [FusionCache: OpenTelemetry](https://github.com/ZiggyCreatures/FusionCache/blob/main/docs/OpenTelemetry.md): the sources, the meters, and the tags (checked 2026-11-04)
