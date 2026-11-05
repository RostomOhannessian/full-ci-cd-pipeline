---
title: "Evidence record: traces, metrics, and redacted logs of the API (2026-11-04)"
description: "A dated record of the first pull request of WP1.11 on Windows with Docker Desktop and in the pinned Linux SDK image: what ran where, the counts, what the telemetry tests showed about traces, metrics, redaction, and the exporter, the findings of the full suite, and what the runs did not show."
type: evidence-record
date: 2026-11-04
status: verified
audience: [maintainers, contributors]
last-verified: 2026-11-04
owner: "@RostomOhannessian"
---

# Evidence record: traces, metrics, and redacted logs of the API (2026-11-04)

This record is the evidence for the first pull request of WP1.11 (Observability): the OpenTelemetry pipeline of the host, the real `IUseCaseTelemetry`, the spans and the metrics of the outbox, the idempotency store, the credential rotation, and the cache connections, and the redaction of
every signal ([ADR-0043](../../adr/0043-observability-with-opentelemetry-and-redaction.md)). It continues [the record of the conformance run](2026-11-04-http-api-conformance-run.md). It says what ran where, so a reader can tell which host proved which claim.

## What ran where

| Where | Command | What it ran | Result |
| --- | --- | --- | --- |
| Windows x86-64, Docker Desktop, .NET SDK 10.0.403 | `CI=true dotnet restore ProductCatalog.slnx`, then `dotnet build ProductCatalog.slnx --configuration Release --no-restore` | The locked restore with the new packages in the lock files, and the build with warnings as errors | Exit 0, 0 warnings, 0 errors |
| Windows | `dotnet test --solution ProductCatalog.slnx --configuration Release --no-build --coverage --coverage-output-format cobertura --report-trx --results-directory TestResults` | Every test project natively. The Api tests start the real composition in process, a SQL Server 2022 container, and a Valkey 8.1 container from the digests of `compose.yaml`, with in-memory exporters for the traces, the metrics, and the logs | 2,294 tests ran, 2,294 passed, 0 failed, 0 skipped, in about 97 seconds. By project: Api 402 (was 291), Application 277 (was 269), Architecture 151 (was 147), Contract 113, Domain 173, Infrastructure 289 (was 272), Documentation auditor 265, Governance auditor 624. The 140 new tests are 111 in the Api project, 8 in the Application project, 17 in the Infrastructure project, and 4 architecture fixtures |
| Windows | `pwsh -NoProfile -File tools/ci/Test-CoverageThresholds.ps1 -CoverageFile 'TestResults/*.cobertura.xml'` | The coverage gate over the cobertura files of the run | Passed: Catalog.Application 99.54% of lines and 99.58% of branches, Catalog.Domain 99.63% of lines and 98.41% of branches, and the solution 97.44% of lines (3,580 of 3,674) |
| Windows | `dotnet format whitespace ProductCatalog.slnx --no-restore`, then `dotnet format ProductCatalog.slnx --verify-no-changes --no-restore` | The formatter | Exit 0 and no change |
| Windows | `dotnet nuget-license -i ProductCatalog.slnx -t -a governance/policies/license-policy.json -override governance/policies/license-overrides.json -err` | The license gate over every package, the new ones included | Exit 0, no error. The new packages are Apache-2.0 (OpenTelemetry) and MIT (`Microsoft.Extensions.Compliance.Redaction`) |
| Windows | `markdownlint`, `links`, `spelling`, and `diagrams` of `tools/lint/compose.yaml`, and `dotnet run --project tools/Documentation.Auditor -- audit` | The documentation gate | 0 issues, 0 broken links, 0 spelling issues, 26 diagrams, and the audit passed with 0 warnings and 18 notes (the tool pages that WP1.13 owes) |
| Windows | `dotnet run --project tools/Governance.Auditor -- status validate`, `status render --check`, `usage validate`, `usage render --check`, `trace --check`, and `security --base origin/phase/1-api-core` | The governance checks | Every command passed with 0 warnings |

The Linux run is recorded below, in the section "The Linux run".

## What the tests showed

| Question | Answer | Test |
| --- | --- | --- |
| Does a request make one trace from the server span to the data access and the cache? | A request that carries a `traceparent` header gives one trace with the ASP.NET Core server span, the use case, FusionCache, EF Core, SqlClient, the Redis commands, and the outbox handler, each with its parent. The trace identifier in the body of an error is the identifier of that trace, so a failure is followed from the answer to its trace | `TelemetryTraceTests` |
| Does the shape of a trace hold? | The edges of the trace of a read and of a write (which kind of span called which) match a committed snapshot, and they do not depend on the number of calls | `TelemetryTraceTests` |
| Do the outbox and the credential rotation have spans? | `outbox.process_batch`, `outbox.deliver`, and `outbox.handle` have the parent links of the plan, a failed handler sets the status to error without its text, and `credentials.rotate` and `credentials.clear_pool` have spans | `OutboxTelemetryTests`, `CredentialRotationTests` |
| Does the telemetry port record each outcome? | One span and one measurement of `catalog.usecase.duration` for each run, with the outcome `success`, `refused`, `fault`, or `canceled`, and the kind of refusal. A fault carries `error.type` and never the text of the exception. The clock is the injected `TimeProvider` | `UseCaseTelemetryTests` |
| Does the host export the series that the plan asks for? | The defaults of ASP.NET Core and of the runtime, the duration of each use case, the cache, the outbox backlog, the dead letters, the oldest pending age, the age of the last reading, the delivery age, the handler duration, the idempotency decisions, and the credential rotations, all read from an in-memory exporter of the real host | `TelemetryMetricsTests`, `OutboxTelemetryTests`, `IdempotencyTelemetryTests` |
| Are the labels of the series bounded? | Every label of every exported series is in a documented list, no value is an identifier, and no label value holds the identifier or the SKU of a product that the test wrote | `TelemetryMetricsTests` |
| Does a signal hold a secret or personal data? | The classifier is tested on 65 cases of names and values, the ones that must be redacted and the ones that must stay readable. The real host gets sentinels (a token, a connection string, an email address, the text of a driver exception, and the passwords of the settings), and no log record, span tag, or metric series holds one afterwards | `TelemetryRedactionTests`, `TelemetryLogTests`, `TelemetryTraceTests` |
| Does the text of an exception leave the process? | It does not. A log record keeps the type of the exception and its frames without the file paths, and a span keeps `error.type` | `TelemetryLogTests`, `TelemetryRedactionTests` |
| Does the log have the shape that the snapshot holds? | A fault is logged as a template and a dictionary of redacted values, with the trace identifier of the request | `TelemetryLogTests` |
| Does the OTLP exporter send what the in-memory exporters see? | The host started against a listener on the loopback address sends OTLP bodies for the three signals, and the bodies hold no sentinel | `TelemetryOptionsTests` |
| Is a wrong setting refused at the start? | A service name, a sampling ratio, an exporter, an endpoint, or a protocol that is not valid fails the validation, and a host with a wrong endpoint does not start. The message names the setting and never repeats the value. When the exporter is off the variables are not checked | `TelemetryOptionsTests` |
| Does the host answer when the collector is down? | With an endpoint that nobody serves, 20 requests are answered as before. The exporters batch off the request path and drop what they cannot send | `TelemetryOptionsTests` |
| Does a degraded cache show in the telemetry? | With a Valkey that nobody runs, the requests succeed, the gauge of the shared cache is 1, the exported counters carry the degraded reads and the incomplete removals, and readiness is degraded and not unhealthy | `TelemetryCacheDegradationTests` |
| Are the two beta instrumentations pinned? | The EF Core and StackExchange.Redis instrumentations are at the exact version 1.20.1-beta.1, and the test fails when either pin moves or stops being a beta | `TelemetryPackageTests` |
| Does a layer keep the SDK out? | `Catalog.Infrastructure` and `Catalog.CrossCutting` may not reference the OpenTelemetry SDK packages, and four violating fixtures show that the rule can fail | `ReferenceRulesTests` |

## A manual run against the Compose stack

A scratch copy of the sources ran against the Compose stack on Windows, with `./dev compose up`, so that a real collector received the OTLP of the host. The copy was deleted afterwards.

| Step | Result |
| --- | --- |
| The API on the host, with no `OTEL_*` variable, answered a request with no token | A 401 with a `traceId` in the body |
| Tempo in the otel-lgtm container, queried for that trace identifier and for the service | The trace was there, with the root span `GET /api/v{version:apiVersion}/products/` and the service `catalog-api` |
| `OTEL_EXPORTER_OTLP_PROTOCOL=udp` | The host stopped at the start with "OTEL_EXPORTER_OTLP_PROTOCOL must be grpc or http/protobuf." |
| `Catalog__Telemetry__Exporter=None` | The error answer still had a `traceId`, and nothing reached Tempo |

The run did not show the deeper spans of a request that reaches SQL Server and Valkey, because from the host the API could reach neither of the two containers in that run (SqlClient error 258 and a Redis connect timeout, although the port answered a connection test). The tests above prove those spans against containers that Testcontainers starts, and the second pull request of WP1.11 shows the dashboards with the traffic of a real request.

## Findings

- **A test process with several hosts records some spans twice.** The EF Core and SqlClient instrumentations listen to diagnostic sources of the whole process, so with many hosts alive in the full suite one command became two spans, and the second was the child of the first. The snapshot of the shape passed alone and failed in the full suite until the shape left out a span below a span of its own kind. A production host is one host in its process, and the effect is a property of the test process.
- **A listener sees the series of every host in the process.** A test that counted the series of `http.server.request.duration` before and after twelve writes found eight more series than expected in the full suite, because other hosts added theirs in between. The test now checks the property itself: no label value of any series holds the identifier, the SKU, or the category that the test wrote.
- **The first full run on Linux lost series that a run of the class alone had.** Three tests of `TelemetryMetricsTests` failed in the full suite: the runtime metric `dns.lookup.duration` has the label `dns.question.name` on Linux (the name of a host that the process calls, which the list of labels now allows, and which a configuration bounds), and the series of the outbox and of the cache counters were missing from one collection of the real host, although the class alone passed on Linux. The metrics SDK identifies a stream by the name of the meter and of the instrument and not by the instance, so a host of another test that disposes its meters is the likely cause, and the evidence is the pattern (only in the full suite, order dependent, and gone after the change below). Two tests that need the series of the catalog (`TelemetryMetricsTests` and `TelemetryCacheDegradationTests`) now run alone in a collection that disables parallelization, and the first reads until the cache has counted a hit and the outbox has reported, because the outbox removes the entry of a product a moment after its write, which can fall between two reads. The change was followed by four runs of the Api project on Windows with no failure and a full run on Linux with none. A production host is one host in its process.
- **Sampling is not isolated between hosts of one process.** The listener of an activity source belongs to the process, so with a host that samples every trace alive in another test, the request of a host with a ratio of zero was recorded and exported too, and `A_ratio_of_zero_records_no_trace...` failed in four of five runs of the Api project alone. The test is now in its own class in the same collection, and it passed in the four runs that followed.
- **A margin of one second failed once.** `JwtValidationTests` checks the lifetime against the real clock, and its accepted token had a margin of one second. A first request on a loaded machine took longer, and the full suite failed once with a 401, although the class passed three times alone. The accepted token of the expiry case and the refused token of the not-yet-valid case now keep ten seconds, and the comment of the class no longer says that the clock is a fake one.
- **The snapshot of the trace shape failed once on Linux because a background span was not there yet.** FusionCache writes the second level and publishes to the backplane in the background, so the span of the publication can end after the spans that the test waited for. The tests of the traces now wait for those spans by source and name, and a Windows run of the Api project three times and two full runs on Linux passed afterwards.
- **The traces of the outbox do not join the trace of the request.** A message does not carry the trace context of the command that wrote it, so the processor starts its own traces. Carrying it needs a column and a migration, which ADR-0043 records as a later change.
- **The dead-letter gauge scans the table.** The reading runs every 30 seconds, and a filtered index would make it cheap. It is a later expand migration, recorded in ADR-0043.
- **The standard output of the host is not redacted.** The console logger is not part of the pipeline. Risk R24 records it, and the Compose stack and the cluster collect telemetry through OTLP and not through the console.
- **The OTLP defaults of the .NET exporter are gRPC on port 4317.** The page of otel-lgtm says that the defaults of OpenTelemetry are HTTP on port 4318. Both ports work with the image, and the second pull request corrects the page.

## Limits

- The tests read what the in-memory exporters receive after the processors of the host, and the OTLP body test covers the three signals. A collector or a backend that the repository does not run is not covered.
- The sampling ratio is proven for the default of one (every trace) and for zero (no trace, and the request still has a trace identifier). An intermediate ratio is the `TraceIdRatioBasedSampler` of the SDK and was not measured.
- The performance proof of the series and of the overhead of the instrumentation belongs to WP4.4, and the alerts and the dashboards belong to the second pull request of WP1.11.
- The privacy scan of the repository, the release assets, the issues, and the workflow logs belongs to the publication review (LAUNCH).
- Timings are single samples from one machine.
- No hosted run existed for this branch when this record was written.

## The Linux run

| Where | Command | What it ran | Result |
| --- | --- | --- | --- |
| Linux, the pinned SDK 10.0.401 image, Docker socket mounted, `TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal` | `docker compose -f tools/lint/compose.yaml run --rm linux-tests` | A scratch copy of the repository: locked restore, Release build with warnings as errors, every test project with the code coverage extension, and the coverage thresholds. The Api tests start the SQL Server and Valkey containers from the digests of `compose.yaml` through the Docker socket | 2,294 tests ran, 2,294 passed, 0 failed, 0 skipped, in about 80 seconds. The coverage gate passed with nearly the same numbers as on Windows: Catalog.Application 99.54% of lines and 99.58% of branches, Catalog.Domain 99.63% of lines and 98.41% of branches, and the solution 97.44% and 97.5% of lines in the last two runs (3,580 and 3,582 of 3,674). Four full runs were made on the way: the first failed in three tests of `TelemetryMetricsTests`, the second passed, the third failed once in the snapshot of the trace shape, and the last two passed. The findings above say why and what changed |
| Linux, the same image, a filter on the class | `dotnet test --project tests/Catalog.Api.Tests/Catalog.Api.Tests.csproj --configuration Release --no-build -- --filter-class '*TelemetryMetricsTests'` twice, in a scratch copy | The class alone | Both runs failed in one test only, for the label `dns.question.name`, which showed that the other two failures of the full run came from the other hosts of the process |
| Linux target on Windows | `dotnet publish src/Catalog.Api/Catalog.Api.csproj --configuration Release -r linux-x64 --self-contained false -o <folder>`, then `pwsh -NoProfile -File tools/ci/Test-NoWindowsNativeRuntime.ps1 -Path <folder>` | The publish output with the OpenTelemetry packages and the redaction package | No native Windows runtime of Microsoft.Data.SqlClient.SNI.runtime in the output. The publish rewrote the lock files of six source projects, and they were restored from a copy that was made before the publish and checked with a `CI=true` locked restore |

The oasdiff step of the conformance check does not run in this image (no Docker Compose). The contract did not change in this pull request, and the step runs in hosted CI.
