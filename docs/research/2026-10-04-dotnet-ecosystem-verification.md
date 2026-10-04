---
title: "Research record: .NET ecosystem verification (2026-10-04)"
description: "Source-checked findings on .NET 10, caching, testing, licensing, OpenAPI tooling, Pact, EF Core, OpenTelemetry, DocFX, and dependency automation."
type: research-record
date: 2026-10-04
status: raw
---

# Time-Sensitive Fact Verification Report — as of 2026-10-04

Methodology: 7 parallel research passes, each fetching primary sources directly (learn.microsoft.com, devblogs.microsoft.com, NuGet V3/flat-container API, GitHub Releases/Tags/Issues REST API, raw LICENSE files at tagged commits) rather than relying on search snippets. I independently re-verified the three highest-stakes/most-surprising claims myself (`.NET 10 EOL date`, `WebApplicationFactory.UseKestrel()`, HybridCache's cross-node gap via `dotnet/extensions#5517`) directly against primary sources — all three checked out. Several `web_search` calls returned unverifiable AI-synthesized claims (e.g., "Verify went commercial," "Dependabot supports kustomize," "NetArchTest.eNhancedEdition actively maintained in July 2026") that were checked against primary sources and found **false** — these corrections are flagged inline.

---

## 1. .NET 10

- **GA date:** Nov 11, 2025. **Source:** https://devblogs.microsoft.com/dotnet/announcing-dotnet-10/ ; https://raw.githubusercontent.com/dotnet/core/main/release-notes/10.0/releases.json. **Confidence:** High.
- **LTS / support end date:** LTS, 3-year support window, **EOL = 2028-11-14** (independently re-confirmed by me directly against the canonical `releases.json`: `"release-type":"lts","support-phase":"active","eol-date":"2028-11-14"`). ⚠️ The original Nov-2025 devblogs announcement prose says "Nov 10, 2028" — a stale/uncorrected typo in that post; treat **Nov 14, 2028** as authoritative (it matches the machine-readable release manifest and the dedicated, more-recently-updated support-policy page). **Source:** https://raw.githubusercontent.com/dotnet/core/main/release-notes/10.0/releases.json ; https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core ; https://learn.microsoft.com/en-us/lifecycle/products/microsoft-net-and-net-core. **Confidence:** High.
- **Current SDK/patch as of Oct 2026:** Runtime/SDK patch **10.0.12 / SDK 10.0.401**, released 2026-09-08 (6 CVE fixes). **Source:** same `releases.json`. **Confidence:** High.
- **C# 14:** GA'd with .NET 10 (not preview) — extension members, `field`-backed properties, null-conditional assignment (`?.=`), implicit `Span<T>` conversions, `nameof` on unbound generics, lambda parameter modifiers, partial constructors/events. **Source:** https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-14. **Confidence:** High.
- **global.json / rollForward:** Version must be a full SDK string (e.g. `10.0.401`); `rollForward` values are `patch` (default), `feature`, `minor`, `major`, `latestPatch`, `latestFeature`, `latestMinor`, `latestMajor`, `disable`. .NET 10 adds new `paths` and `errorMessage` fields. **Source:** https://learn.microsoft.com/en-us/dotnet/core/tools/global-json. **Confidence:** High.
  - **Recommendation:** `{"sdk":{"version":"10.0.401","rollForward":"latestPatch"}}` — locks the feature band (identical toolchain/analyzer behavior across Dev Container/Codespaces/CI) while auto-tolerating newer security patches, avoiding both `disable`'s CI-breaking brittleness and `latestFeature`'s toolchain drift. **Confidence:** Medium (reasoned recommendation, not a literal MS directive).
- **Implication:** Pin `10.0.401` + `latestPatch` everywhere (Dockerfile, devcontainer.json, global.json); document the Nov 2028 support horizon in onboarding docs; C# 14 features are safe to use/teach.

## 2. Caching — HybridCache vs. FusionCache (multi-replica correctness)

- **HybridCache package status:** Still a separate NuGet package (`Microsoft.Extensions.Caching.Hybrid`, not in the shared framework), stable/GA since 9.3.0, current **10.10.0** (2026-09-09), MIT. **Source:** https://learn.microsoft.com/en-us/aspnet/core/performance/caching/hybrid?view=aspnetcore-10.0 ; https://api.nuget.org/v3-flatcontainer/microsoft.extensions.caching.hybrid/index.json. **Confidence:** High.
- **Tag invalidation (`RemoveByTagAsync`) semantics:** Logical only — sets an "ignore anything created before this timestamp" rule; **does not physically delete** L1/L2 entries (they linger until natural TTL expiry). **Source:** same Learn doc (verbatim: *"doesn't remove values from either the local or distributed cache... establishes an 'ignore anything created before this point' rule"*). **Confidence:** High.
- **🚨 CRITICAL — no cross-node L1 invalidation in .NET 10:** HybridCache has **no built-in backplane**. When node A calls `RemoveAsync`/`RemoveByTagAsync`, nodes B/C/D's in-process L1 keep serving stale data until local TTL expiry. **I independently re-confirmed this via the GitHub API**: issue **dotnet/extensions#5517** ("Cache Synchronization for Hybrid Caching in Multi-Node Environments") is **still open** (created 2024-10-14, last updated 2026-03-27, labels `waiting-on-team`/`api-suggestion`, milestone `null`); a follow-up, **dotnet/runtime#125602**, targets a fix at milestone `11.0.0` — i.e., not even scheduled before .NET 11 (still RC as of Oct 2026). FusionCache's own docs independently confirm the same gap. **Source:** https://api.github.com/repos/dotnet/extensions/issues/5517 (verified directly) ; https://github.com/dotnet/runtime/issues/125602 ; https://raw.githubusercontent.com/ZiggyCreatures/FusionCache/main/docs/MicrosoftHybridCache.md. **Confidence:** High.
- **Stampede protection:** Single-flight **only within one process/instance**; does NOT coordinate across replicas even sharing the same L2 — N replicas can independently stampede the DB on a cold cache. **Source:** https://learn.microsoft.com/en-us/aspnet/core/performance/caching/hybrid?view=aspnetcore-10.0. **Confidence:** High.
- **Serialization:** `string`/`byte[]` handled natively; everything else via `System.Text.Json` by default; pluggable (protobuf, XML) via `AddSerializer<T,TSerializer>()`. **Confidence:** High.
- **FusionCache:** Current **2.9.0** (2026-09-22), **MIT**. Has a **mature Redis backplane** (`ZiggyCreatures.FusionCache.Backplane.StackExchangeRedis`, lockstep 2.9.0) using lightweight pub/sub (key+timestamp, not payload) with a LAZY+adaptive-PASSIVE strategy that directly closes the HybridCache gap above. Ships an official **`.AsHybridCache()`** adapter — "the first production-ready implementation of HybridCache, including Microsoft's own" (FusionCache's own framing) — and when a backplane is configured, **cross-node invalidation is automatic** through that adapter. Has native OpenTelemetry support (`ZiggyCreatures.FusionCache.OpenTelemetry`, traces+metrics, lockstep 2.9.0). **Valkey**: no dedicated module; works via `StackExchange.Redis` RESP-protocol compatibility — this is a *compatibility inference*, not a ZiggyCreatures-certified combination (Medium confidence), so validate with your own Testcontainers test. **Source:** https://api.nuget.org/v3/catalog0/data/2026.09.22.14.39.49/ziggycreatures.fusioncache.2.9.0.json ; https://raw.githubusercontent.com/ZiggyCreatures/FusionCache/main/docs/Backplane.md ; https://raw.githubusercontent.com/ZiggyCreatures/FusionCache/main/docs/MicrosoftHybridCache.md ; https://raw.githubusercontent.com/ZiggyCreatures/FusionCache/main/docs/OpenTelemetry.md. **Confidence:** High (except Valkey compatibility: Medium).
- **Recommendation:** **Use FusionCache + Redis/Valkey backplane, exposed via `.AsHybridCache()`**, not bare `Microsoft.Extensions.Caching.Hybrid`. This keeps Microsoft's `HybridCache` abstraction in application code (clean-architecture boundary preserved) while fixing the one documented, open, unmilestoned correctness gap that directly threatens this project's multi-replica/canary topology. **Confidence:** Medium-High (synthesized judgment on top of High-confidence facts).
- **Implication:** Without a backplane, a canary rollout or price/stock update can leave other replicas serving stale L1 reads for up to the full local-cache TTL — a real, silent correctness bug this project must design around explicitly.

## 3. Testing platform ecosystem

- **Microsoft.Testing.Platform (MTP):** VSTest remains the **default** for `dotnet test` even on .NET 10 SDK — MTP is opt-in via a new `global.json` field: `{"test":{"runner":"Microsoft.Testing.Platform"}}` ("Available since: .NET 10.0 SDK"). The old `TestingPlatformDotnetTestSupport` MSBuild flag is legacy/being phased out ("removed in MTP version 2 if run with .NET 10 SDK"). Native mode requires MTP ≥ 1.7.0; current MTP package is **2.4.1** (MIT). **Source:** https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-with-dotnet-test ; https://learn.microsoft.com/en-us/dotnet/core/tools/global-json. **Confidence:** High. **Implication:** Add the `global.json` "test" block explicitly; don't use the legacy flag.
- **xUnit v3:** Current **4.0.1**, Apache-2.0, native MTP support (no VSTest shim). **Confidence:** High.
- **TUnit:** Current **1.72.16** (2026-10-02), MIT, MTP-**native only** (no VSTest path at all), ~4,000 GitHub stars, very active (pushed same day as research), single primary maintainer — credible but less battle-tested than xUnit. **Confidence:** High.
- **MSTest:** Current **4.4.1**, MTP support since 3.2.0; `MSTest.Sdk` defaults to MTP. **Confidence:** High.
- **Code coverage:** `Microsoft.Testing.Extensions.CodeCoverage` **18.11.2** — MTP-native but **closed-source** (Microsoft .NET Library License, not MIT — confirmed via `microsoft/codecoverage` README: "closed source"). `coverlet.collector` **10.1.0** is **explicitly, officially NOT compatible with MTP v2** per its own NuGet description — no "coverlet.MTP" package exists. **Source:** https://www.nuget.org/packages/coverlet.collector/ ; https://github.com/microsoft/codecoverage. **Confidence:** High. **Implication:** Native MTP mode forces a choice between the closed-source MS coverage extension or staying on legacy VSTest mode to keep coverlet — a real CI design decision.
- **TRX/JUnit reporting:** `Microsoft.Testing.Extensions.TrxReport` **2.4.1**, stable (`--report-trx`). `Microsoft.Testing.Extensions.JUnitReport` exists but is **still alpha** (`1.0.0-alpha.26466.3`, MIT). **Confidence:** High.
- **Stryker.NET:** Current **5.0.0** (2026-09-11), explicitly targets .NET 10. MTP support (`--test-runner mtp`) is **preview**, not GA — per the official Stryker blog, and ongoing bug-fix churn through 5.0.0 confirms it's not yet fully stable; no .NET Framework support under MTP runner. **Confidence:** High.
- **ArchUnitNET vs NetArchTest:** Original `NetArchTest.Rules` is **abandoned** (last release 2021-05-23). Its fork `NetArchTest.eNhancedEdition` is **dormant** — last release AND last commit both 2025-06-04 (~16 months stale); this directly **corrects** an unverifiable web-search claim of "active as of July 2026." **`TngTech.ArchUnitNET`** (correct NuGet ID, not bare "ArchUnitNET") is actively maintained — **0.13.4** (2026-08-20), commits as recent as 2026-09-25, Apache-2.0, ships a native **`TngTech.ArchUnitNET.xUnitV3`** package. **Recommendation: use TngTech.ArchUnitNET.** **Confidence:** High.
- **Verify (snapshot testing):** Current **33.2.0** (2026-09-30), **MIT** (directly fetched `license.txt` — no subscription clause), targets `net10.0`/`net11.0`. An initial search claim of a commercial-license move was **investigated and found false**. **Confidence:** High.
- **FsCheck vs CsCheck:** FsCheck **3.4.0** (2026-08-20), BSD-3-Clause, xUnit v3 4.x-compatible, but pulls an `FSharp.Core` dependency even from C#. CsCheck **4.9.1** (2026-09-17, commits to 2026-10-03), **Apache-2.0**, pure C#/LINQ, no F# footprint, AOT-friendlier. **Recommendation: CsCheck** (license match + no F# dependency + more recent activity), FsCheck as acceptable alternative. **Confidence:** High.
- **Implication (overall):** This is the most "moving parts" item in the whole report — plan for: `global.json` MTP opt-in, xUnit v3 + TngTech.ArchUnitNET.xUnitV3 + Verify + CsCheck as the stable core, and explicit, documented preview-status caveats for Stryker's MTP runner and JUnit reporting.

## 4. License changes in the .NET ecosystem (2024–2026)

| Library | Change | Effective | New terms | Safe pin / alternative |
|---|---|---|---|---|
| **FluentAssertions** | Apache-2.0 → proprietary "Xceed Community License" | **v8.0.0, Jan 14, 2025** | Free non-commercial/OSS; ~$129.95/yr/seat commercial (Medium confidence on exact price) | Pin `7.x` (frozen) or use **AwesomeAssertions** (fork, Apache-2.0, v**9.6.0**, active) or **Shouldly** (BSD-3-Clause, v**4.3.0**, unaffected) |
| **MediatR** | Apache-2.0 → dual RPL-1.5/commercial (Lucky Penny Software) | **v13.0.0, Jul 2, 2025** | License key required; free tier for OSS/individuals/small cos/non-production; paid for commercial prod | Project already avoids MediatR — **validated decision** |
| **AutoMapper** | MIT → dual RPL-1.5/commercial (same vendor) | **v15.0.0, Jul 2, 2025** | Same model as MediatR | Project already avoids AutoMapper — **validated decision** |
| **MassTransit** | OSS → commercial (new entity "Massient, Inc.") | **v9** (exact date low-confidence, ~Apr 2025 per secondary sources) | No confirmed free tier for v9 | Pin **v8.5.10** (last OSS tag, still actively patched, `pushed_at` 2026-09-30) if ever needed |
| **Moq** | SponsorLink telemetry controversy, then reverted | Added v4.20.0 (Aug 2023), **removed v4.20.2 (Aug 9, 2023)** | Currently clean BSD-3-Clause, v4.21.0 (v5 in dev) | Safe today — historical cautionary tale only |
| **Duende IdentityServer** | No new 2024-2026 change; paid-for-production model is pre-existing | n/a | Paid tiers ~$5.75K–$24.9K/yr (Medium confidence) | Project's choice of **Keycloak** (Apache-2.0) avoids this entirely |
| **ImageSharp (Six Labors)** | Split License v1.0 (2022, not new) | n/a | Free under $1M revenue/direct-dep else paid | Conditional-free only — flag in CI if ever added |
| **QuestPDF** | Custom Community License v3.0 (**not OSI-approved**, not MIT) | Effective **Jul 6, 2026** | Free under $1M revenue, academia, OSS; public-sector/public companies never free | Don't naively allowlist by SPDX string — needs explicit handling |
| **NServiceBus** | RPL-1.5/commercial (pre-existing model) | n/a | Free Community tier: 3 endpoints/10K msgs/day | Not currently used by project |
| **FluentValidation** | **Still Apache-2.0**, confirmed via direct LICENSE fetch | n/a (no change found) | — | Safe, current **12.1.1** |

**Sources:** https://api.github.com/repos/fluentassertions/fluentassertions/releases/tags/8.0.0 ; https://raw.githubusercontent.com/fluentassertions/fluentassertions/7.0.0/LICENSE ; https://github.com/AwesomeAssertions/AwesomeAssertions ; https://raw.githubusercontent.com/shouldly/shouldly/master/LICENSE.txt ; https://api.github.com/repos/LuckyPennySoftware/MediatR/releases/tags/v13.0.0 ; https://raw.githubusercontent.com/LuckyPennySoftware/MediatR/main/LICENSE.md ; https://api.github.com/repos/LuckyPennySoftware/AutoMapper/releases/tags/v15.0.0 ; https://www.nuget.org/packages/MassTransit ; https://api.github.com/repos/MassTransit/MassTransit/tags ; https://api.github.com/repos/devlooped/moq/releases/tags/v4.20.2 ; https://raw.githubusercontent.com/moq/moq/v4/License.txt ; https://docs.duendesoftware.com/general/licensing/ ; https://raw.githubusercontent.com/SixLabors/ImageSharp/main/LICENSE ; https://www.questpdf.com/license/community.html ; https://raw.githubusercontent.com/Particular/NServiceBus/master/LICENSE.md ; https://raw.githubusercontent.com/FluentValidation/FluentValidation/main/License.txt. **Confidence:** High across the board except exact dollar figures and the MassTransit exact date (Medium/Low, clearly flagged above).

**CI license-enforcement tooling:**
- **nuget-license** (`sensslen/nuget-license`): actively maintained, **v4.0.18** (2026-09-21), .NET 10 support, `--allowed-license-types`/`--forbidden-license-types`. **Recommended** — lightweight, single `dotnet tool`, no infra. **Confidence:** High.
- **dotnet-project-licenses**: **explicitly self-described as abandoned** in its own README, pointing users to nuget-license. **Do not adopt.** **Confidence:** High.
- **ClearlyDefined**: active community license-metadata curation service; useful only as a secondary cross-check, not a CI gate by itself. **Confidence:** High.
- **OSS Review Toolkit (ORT)**: very actively maintained (**v95.0.0**, 2026-10-01), supports NuGet, but is a heavyweight multi-stage enterprise SBOM pipeline — disproportionate for this repo's scale. **Confidence:** High.
- **Implication:** Add a GitHub Actions step: `nuget-license --input *.sln --forbidden-license-types forbidden-licenses.json`, failing the build on FluentAssertions≥8/MediatR≥13/AutoMapper≥15/MassTransit≥9/QuestPDF-noncompliant/etc.

## 5. Testcontainers for .NET

- **Core version:** **4.15.0** (2026-09-07), targets `net10.0` natively. **Source:** https://api.nuget.org/v3-flatcontainer/testcontainers/index.json. **Confidence:** High.
- **MsSql module:** Lockstep 4.15.0. The default-image constant and parameterless constructor are now **`[Obsolete]`** — must pass an explicit image string, e.g. `new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")`. SQL Server 2025 has an official image (`mcr.microsoft.com/mssql/server:2025-latest`) but compatibility isn't explicitly certified by the Testcontainers maintainers (Medium confidence on 2025-specific behavior). **Source:** https://raw.githubusercontent.com/testcontainers/testcontainers-dotnet/4.15.0/src/Testcontainers.MsSql/MsSqlBuilder.cs. **Confidence:** High (API), Medium (2025-image certification).
- **Redis/Valkey:** **No dedicated `Testcontainers.Valkey` package exists** (confirmed 404 on NuGet). Official docs explicitly say to reuse `Testcontainers.Redis`'s `RedisBuilder` pointed at a `valkey/valkey:*` image. **Source:** https://dotnet.testcontainers.org/modules/valkey/. **Confidence:** High.
- **Keycloak module:** Lockstep 4.15.0; realm import via `.WithRealm(path)` (Keycloak's native `--import-realm`); auto-detects image major version ≥25 for the new management-port (9000) behavior. **Confidence:** High.
- **Ryuk:** Pinned to `testcontainers/ryuk:0.14.0`; disable via `TESTCONTAINERS_RYUK_DISABLED=true`; official guidance says only disable on environments with their own cleanup mechanism (e.g., ephemeral CI runners — otherwise leave it on). **Confidence:** High.
- **Docker Desktop/Podman/WSL2:** Officially tested only against Docker Desktop (Mac/Windows) and native Docker (Linux) — **Podman is explicitly "not actively tested,"** community-supported only; no official WSL2-specific doc page exists. **Confidence:** High.
- **Sharing containers (xUnit v3):** Use the new **Assembly Fixtures** (`[assembly: AssemblyFixture(typeof(DatabaseFixture))]`) — no `[CollectionDefinition]` boilerplate needed, but fixtures are **not** serialized across tests (no automatic parallelization change), so the fixture must guard idempotent startup itself. **Source:** https://xunit.net/docs/shared-context. **Confidence:** High.
- **Implication:** Update any `new MsSqlBuilder()`/`new RedisBuilder()`/`new KeycloakBuilder()` calls in the plan to pass explicit image strings; use Assembly Fixtures for one shared SQL Server+Valkey+Keycloak trio per test assembly; document Docker Desktop as the only officially-supported path.

## 6. ASP.NET Core 10

- **Built-in OpenAPI generation:** `Microsoft.AspNetCore.OpenApi` default document version is **OpenAPI 3.1** in .NET 10 (3.2 arrives in .NET 11). Override via `OpenApiSpecVersion`. **Source:** https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/aspnetcore-openapi?view=aspnetcore-10.0. **Confidence:** High.
- **Build-time generation:** `Microsoft.Extensions.ApiDescription.Server` generates JSON at build time; **YAML is NOT supported at build time in .NET 10** ("planned for a future preview") — only at runtime via `MapOpenApi("/openapi/{documentName}.yaml")`. **Confidence:** High. **Implication:** keep the hand-authored contract in JSON, or add a conversion step, for CI diffing.
- **Minimal API validation:** Built-in via `AddValidation()`. ⚠️ **Package renamed mid-cycle**: preview-era `Microsoft.AspNetCore.Http.Validation` → GA package **`Microsoft.Extensions.Validation`**. Top-level API is stable/GA; underlying resolver APIs are marked experimental; known gap: nullable value-type parameters aren't validated in .NET 10 (fixed in .NET 11). **Confidence:** High.
- **ProblemDetails:** `AddProblemDetails()` + `UseExceptionHandler()`/`UseStatusCodePages()` + `IProblemDetailsService.TryWriteAsync()`; current docs cite RFC 9457 (inconsistently alongside older RFC 7807 references on the same page — cosmetic doc issue, not a behavior difference). **Confidence:** High.
- **✅ WebApplicationFactory + real Kestrel (independently re-verified by me):** `WebApplicationFactory<T>.UseKestrel()` / `UseKestrel(Action<KestrelServerOptions>)` / `UseKestrel(int port)` are **real, shipped APIs in `Microsoft.AspNetCore.Mvc.Testing v10.0.0`** — confirmed directly from learn.microsoft.com's API reference page, with direct links into the `dotnet/dotnet` monorepo source. Must be called before `CreateClient()`/`StartServer()`. This hosts on a real TCP Kestrel listener reachable by external processes. **Source:** https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.mvc.testing.webapplicationfactory-1.usekestrel?view=aspnetcore-10.0 (fetched directly). **Confidence:** High. **Implication:** This is the exact mechanism needed for the Pact provider verifier (see Item 8) — no separate `dotnet run` process required.
- **Asp.Versioning:** Current **v10.2.0** (2026-08-06); v10.0.0 was "the first release to officially support both ASP.NET Core 10 and the new built-in OpenAPI support" via `Asp.Versioning.OpenApi`; actively maintained under `dotnet` org, subject of an official devblogs guest post. **Confidence:** High.
- **Rate limiting:** No .NET-10-specific additions found; core capability unchanged since .NET 7/8. **Confidence:** Medium-High.
- **Forwarded headers:** Must explicitly call `UseForwardedHeaders()`; defaults trust only loopback. A **security-hardening breaking change shipped in 8.0.17/9.0.6** (carried into 10/11): unknown-proxy headers are now silently dropped unless the proxy IP/CIDR is explicitly in `KnownProxies`/`KnownNetworks`. **Source:** https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/8/forwarded-headers-unknown-proxies?view=aspnetcore-10.0. **Confidence:** High. **Implication:** every replica behind the canary LB must explicitly configure `KnownProxies`/`KnownNetworks`, or Keycloak OIDC redirects, client-IP rate limiting, and canary routing silently break.

## 7. OpenAPI tooling

- **oasdiff:** Current **v1.33.0** (2026-10-01); OpenAPI 3.1 "generally available starting with v1.15.0," full 3.1 rule coverage, can even read 3.2 docs. **Confidence:** High.
- **Microsoft Kiota:** Current **v1.35.0** (2026-09-04); C# generation confirmed; OpenAPI 3.1 input support shipped and closed as "completed" under milestone "Kiota v1.24" (2025-02-24) — stable for well over a year. **Confidence:** High.
- **NSwag:** Current **v14.7.1** (2026-04-20) — **no release in ~5.5 months**, a markedly slower cadence than the others. Its own live README still describes it as **"a Swagger/OpenAPI 2.0 and 3.0 toolchain"** — no 3.1 claim. **Recommendation: do not make NSwag the primary tool** for this OpenAPI-3.1-by-default project. **Confidence:** Medium-High.
- **Microsoft.OpenApi v2:** **v2.0.0 shipped GA**, native OpenAPI 3.1 support, `System.Text.Json`-based (~50% faster). **Confirmed: ASP.NET Core 10 uses Microsoft.OpenApi v2.0.0 internally** (explicit .NET 10 release-note entry). The library has already moved past v2 (current **v3.10.2**, adding OpenAPI 3.2 support) — pin to the 2.x line to match what ASP.NET Core 10 actually loads; v2.x and v3.x are documented as mutually incompatible pairings. **Confidence:** High.
- **Recommended CI enforcement pattern:** `dotnet build` (via `Microsoft.Extensions.ApiDescription.Server`) → generated JSON spec → `oasdiff breaking` (or `oasdiff-action/breaking@v0`) diffed against the checked-in hand-authored contract, failing on disallowed breaking changes. **Confidence:** Medium-High (synthesized from verified building blocks; no single official end-to-end recipe exists).
- **Implication:** Build the contract-enforcement pipeline around `Microsoft.AspNetCore.OpenApi` (3.1) + `Microsoft.OpenApi` 2.x + oasdiff + Kiota; treat NSwag as legacy/unsuitable here.

## 8. PactNet

- **Current version:** **5.0.1** (2025-03-22) — **no release in ~19 months**, confirmed stalled by Pact's own Sept-2026 ecosystem blog (PRs #551/#548 "still open and awaiting review"). **Confidence:** High.
- **.NET 10 support:** Targets only `netstandard2.0` — no explicit `net10.0` dependency group, and no explicit maintainer statement validating .NET 10. Expected to work via binary compatibility, but **budget a validation spike**. **Confidence:** Medium.
- **Pact Specification V4:** Supported since 5.0.0 ("compliant up to and including Pact Specification Version 4.0, excluding pact plugins"). **Confidence:** High.
- **Native FFI platform support:** **win-x64 ✅, linux-x64 ✅, osx-arm64 ✅ (since 5.0.0), linux-arm64 ✅** — all four requested RIDs supported. The one real gap: **Linux musl (Alpine) is NOT supported at all**, any architecture (open issue #374). **Source:** https://github.com/pact-foundation/pact-net (README support table, fetched live). **Confidence:** High. **Implication:** Do not run Pact provider-verification tests inside Alpine-based .NET images.
- **Provider verification requires a real HTTP server:** Officially, emphatically documented: **`WebApplicationFactory`/`TestServer` cannot be used** — "the Rust internals can't call the API." Must host on a real TCP socket. This pairs directly with Item 6's `WebApplicationFactory.UseKestrel()` finding. **Confidence:** High.
- **Provider states:** Middleware-based by convention (`WithProviderStateUrl` → POST to a `/provider-states` endpoint you write yourself, following the official sample's `ProviderStateMiddleware` pattern) — not a built-in NuGet feature. **Confidence:** High.
- **Pact Broker (OSS):** Very actively maintained — **v2.122.0** (2026-10-01), repo pushed 2026-10-04 (same day as research). Healthy independent of PactFlow's changes. **Confidence:** High.
- **`can-i-deploy`:** Still the core workflow (`pact-broker can-i-deploy`), but tooling has shifted to a new unified Rust **Pact CLI** (`pact broker can-i-deploy`) that — notably — **does** support Linux musl (unlike PactNet itself), useful for an Alpine-based deployment-gate CI stage. **Confidence:** High.
- **PactFlow free tier:** ⚠️ **Rebranded to "Swagger Contract Testing" under SmartBear** (confirmed via docs.pact.io Sept-2026 blog + live redirect from docs.pactflow.io). **Exact free-tier numeric limits could not be verified** — pricing pages are JS-rendered; historical "5 free contracts" figures are secondary-sourced only and may be stale post-rebrand. **Confidence:** High (rebrand), Low (numeric limits). **Recommendation: self-host the OSS Pact Broker** rather than design around a commercial free tier in flux.
- **Implication:** PactNet is the most dormant dependency in this whole stack — flag it explicitly as a risk, avoid Alpine for the Pact test stage, build the provider-verification harness around `WebApplicationFactory.UseKestrel()`, and self-host Pact Broker OSS instead of depending on PactFlow/Swagger Contract Testing's uncertain free tier.

## 9. EF Core 10

- **Headline features:** Native SQL Server 2025 `vector` type (`SqlVector<float>`) + native `json` column type; **named query filters** (`HasQueryFilter("Name", predicate)`); `LeftJoin`/`RightJoin` LINQ operators; `ExecuteUpdateAsync` now accepts a **regular non-expression lambda** (ordinary `if`/loops) and supports JSON-mapped complex types; constant-redaction from logs by default. **Source:** https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/whatsnew. **Confidence:** High.
- **Migration bundles for Kubernetes:** Official current guidance **explicitly covers this scenario**: *"Generate the bundle during the build and run it as a one-shot deployment job after the database is healthy. Don't... make every application replica run migrations from its entrypoint."* **Source:** https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying (section "Containers and deployment jobs," updated 2026-08-11). **Confidence:** High. **Implication:** directly validates a single one-shot K8s `Job` (not a per-replica init-container) gated before the Deployment rolls out.
- **SaveChanges interceptors / outbox:** `ISaveChangesInterceptor`/`SaveChangesInterceptor` is current and fully supported; no official MS outbox sample exists, but the mechanism (`SavingChangesAsync` fires before the single transaction) is exactly what's needed — add `OutboxMessage` rows to the same `ChangeTracker` before `SaveChangesAsync`. **Confidence:** High (API), Medium (that this is "the" blessed recipe — community consensus, not an official sample).
- **SQL Server 2025 support:** Explicit — `vector`/`json` types, plus `Microsoft.Data.SqlClient`'s own new `SqlDbType.Vector`. **Confidence:** High.
- **Compiled models:** Unchanged concept; explicitly **not** recommended unless "hundreds to thousands" of entity types; **incompatible with global query filters** — likely irrelevant for a single product-catalog domain. **Confidence:** High.
- **ExecuteUpdate/Delete:** Only `ExecuteUpdate` got new capability (non-expression lambdas, JSON complex-type updates) in EF Core 10; `ExecuteDelete` unchanged. **Confidence:** High.
- **Concurrency tokens:** Unchanged, current guidance: `[Timestamp] byte[]` or `.IsRowVersion()` → SQL Server `rowversion`; zero affected rows ⇒ `DbUpdateConcurrencyException`; resolve via `entry.GetDatabaseValuesAsync()` + retry. **Confidence:** High.

## 10. Microsoft.Data.SqlClient

- **Current version:** **7.1.1** (2026-09-29). **Source:** https://github.com/dotnet/SqlClient/releases/tag/v7.1.1. **Confidence:** High.
- **Credential rotation at runtime:** Pooled connections are **keyed by the credential/connection string used to open them** and are **not automatically invalidated** when a credential file changes on disk — no filesystem-watch mechanism exists in the pool itself. This is an active engineering area: an official **draft spec** (`dotnet/SqlClient` "001-pool-clear", dated 2026-04-10) documents generation-counter-based **lazy invalidation** (idle connections closed immediately; in-flight connections finish and are destroyed only on return to the pool), and v7.1.1 itself shipped a fix for a `ClearPool`/`ClearAllPools` race against in-flight `Open()`. **Source:** https://github.com/dotnet/SqlClient/blob/main/specs/001-pool-clear/spec.md. **Confidence:** High. **Implication:** a credential-rotation sidecar must **actively call `SqlConnection.ClearAllPools()`** after rotation — simply rewriting the file is not sufficient.
- **Per-DbContext dynamic connection strings:** EF Core's own docs give an **official recommended pattern**: configure `UseSqlServer()` with no connection string, then implement `IDbConnectionInterceptor.ConnectionOpeningAsync` to set `connection.ConnectionString` from a cached/periodically-refreshed factory the first time each physical connection opens — explicitly **not** a fresh lookup on every open ("can be very slow... cache and refresh periodically"). **Source:** https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors ("Example: Lazy initialization of a connection string"). **Confidence:** High.
- **DbContext pooling interaction:** `OnConfiguring` on a pooled `DbContext` runs **once**, at pool-fill time — a connection string baked in there will **not** pick up a rotated credential for that pooled instance's lifetime. The `IDbConnectionInterceptor` approach (above) is compatible with pooling because it resolves at the ADO.NET connection level, not the `DbContext`-configuration level. **Confidence:** Medium-High (pooling mechanics are documented; the explicit "avoid pooling for rotating creds" framing is a reasoned synthesis, not a literal MS warning). **Implication:** keep `AddDbContextPool` but pair it with the connection-interceptor pattern — don't rely on a static connection string with pooling if credentials rotate.

## 11. OpenTelemetry .NET

- **Core SDK:** Current **1.19.1** (2026-09-21); core API has been stable ~4 years (GA ~Oct 2022). **Confidence:** High.
- **HTTP semconv:** **Fully stable/GA.** `OpenTelemetry.Instrumentation.AspNetCore` has emitted only stable names since its first stable release (v1.6.0, ~Dec 2023) — the `OTEL_SEMCONV_STABILITY_OPT_IN` dual-emit flag is a historical relic of pre-1.6.0 betas, **not needed today**. **Confidence:** High.
- **DB semconv:** Spans and the primary metric `db.client.operation.duration` are **stable**; secondary connection-pool-level DB metrics remain "Development"/not yet stable. **Confidence:** High.
- **OTLP exporter:** Lockstep with core SDK, **1.19.1**, stable/GA. **Confidence:** High.
- **ASP.NET Core 10 built-in metrics (no OTel package needed):** Confirmed current names: `http.server.request.duration` (histogram), `http.server.active_requests` (gauge) from `Microsoft.AspNetCore.Hosting`; `kestrel.active_connections`; `aspnetcore.routing.match_attempts`; new in 10.0: `aspnetcore.components.*` (Blazor), `aspnetcore.authorization.attempts`, `aspnetcore.authentication.challenges`. **Source:** https://learn.microsoft.com/en-us/aspnet/core/metrics/http?view=aspnetcore-10.0 ; https://learn.microsoft.com/en-us/aspnet/core/metrics/built-in?view=aspnetcore-10.0. **Confidence:** High.
- **EF Core instrumentation:** `OpenTelemetry.Instrumentation.EntityFrameworkCore` is at **1.19.1-beta.1** and has **never shipped a stable release** in its entire history — permanently experimental, explicit breaking-change risk warning in its own README. **Confidence:** High.
- **SqlClient instrumentation:** `OpenTelemetry.Instrumentation.SqlClient` is **stable** — current **1.19.0**, GA since v1.15.0, actively maintained. (SqlClient itself still uses the older `DiagnosticSource` pattern, not a native modern `ActivitySource`.) **Confidence:** High.
- **StackExchange.Redis instrumentation:** `OpenTelemetry.Instrumentation.StackExchangeRedis` is at **1.19.0-beta.1**, also **never stable**, same experimental-breaking-change caveat. **Confidence:** High.
- **Implication:** HTTP/DB tracing + SqlClient instrumentation are safe hard dependencies; **EF Core and Redis instrumentation must be explicitly flagged in docs as pinned, experimental dependencies**, reviewed on every upgrade — a real observability-completeness risk for this project's two most important data paths.

## 12. DocFX

- **Current version:** **v2.81.0** (2026-09-25); adds .NET 11 RC target while retaining 8/9/10. **Confidence:** High.
- **Maintenance status:** **.NET Foundation / community-governed**, not Microsoft, since **Nov 2022** ("Microsoft Learn no longer uses docfx and do not intend to support the project"). Despite that, it is demonstrably active: a commit landed on the exact research date (2026-10-04). **Source:** https://github.com/dotnet/docfx (README) ; https://api.github.com/repos/dotnet/docfx/commits. **Confidence:** High.
- **Mermaid support:** Built into the **"modern" template natively** (not the legacy "default" template) — just fence ```` ```mermaid ```` blocks. Client-side JS only, so **not rendered in PDF output** without an extra plugin. **Confidence:** High.
- **.NET 10 metadata support:** Full — Roslyn 4.13.0, .NET 8/9/10 all first-class targets. **Confidence:** High.
- **GitHub Pages deployment:** Official quick-start recipe: `docfx build` → `actions/upload-pages-artifact@v3` → `actions/deploy-pages@v4` (OIDC). **Confidence:** High.
- **Alternatives:** Neither Docusaurus nor mkdocs-material natively extracts .NET XML-doc/assembly metadata — both would need DocFX anyway for API reference content. **Recommendation: stay on DocFX** given the project's API-doc-heavy emphasis. **Confidence:** Medium (comparative framing from non-authoritative blogs).
- **Implication:** Document DocFX honestly as "community/.NET-Foundation-maintained, not a Microsoft product" but currently healthy; set `"template":["modern"]` for Mermaid.

## 13. Dev Containers

- **Lockfile (`devcontainer-lock.json`):** **Implemented, not just proposed** — records resolved OCI ref/sha256/version per feature; generated automatically by `devcontainer build`/`up` (`--no-lockfile`/`--frozen-lockfile` flags). **Confidence:** High.
- **`devcontainers/ci` Action:** Actively maintained (`@v0.3` tag), recent substantive work (native multi-platform builds replacing QEMU, merged PR #435, 2026-06-01). **Confidence:** High.
- **Docker-in-Docker + `kind`:** The DinD feature itself is current and official; running `kind` inside it is a **known, community-documented pattern** (open `kubernetes-sigs/kind` issue thread), **not an officially endorsed combination**. **Confidence:** Medium. **Implication:** budget a smoke test; consider docker-outside-of-docker as a more reliable fallback.
- **Codespaces free quota (personal, 2026):** **Free**: 15 GB-months storage / **120 core-hours/month**. **Pro**: 20 GB-months / 180 core-hours. Machine types range 2-core/8GB up to 32-core/128GB (intermediate tiers' exact RAM only medium-confidence/interpolated). **Source:** https://docs.github.com/en/billing/concepts/product-billing/github-codespaces. **Confidence:** High (endpoints), Medium (intermediate tiers).
- **Prebuilds:** Current, GA; recommended once codespace creation exceeds ~2 minutes; runs `onCreateCommand`/`updateContentCommand` ahead of time (not `postCreateCommand`); counts against the same storage quota. **Confidence:** High.
- **Implication:** Commit the lockfile for reproducibility; configure a prebuild given this stack's heavy devcontainer (SQL Server+Valkey+Keycloak+.NET 10 SDK); document the 120 free core-hours/month limit for student contributors.

## 14. Dependency update automation

| Ecosystem | Dependabot | Renovate |
|---|---|---|
| NuGet + **Directory.Packages.props (CPM)** | ✅ Full support (rewritten C#/MSBuild-native updater) | ✅ |
| GitHub Actions (+ SHA pinning) | ✅ maintains existing SHA pins, but **does not proactively convert tag→SHA**, and **security alerts don't fire for SHA-pinned actions** | ✅ (with extra config) |
| Docker / docker-compose | ✅ both, directly | ✅ |
| Dev Containers (+ lockfile) | ✅ GA since Jan 2024 — version updates only, **no security updates** | ✅ |
| Terraform | ✅ (0.13–1.15.x) | ✅ |
| Helm (charts + embedded image tags) | ✅ | ✅ (dedicated `helm-values` manager too) |
| **Kustomize** | ❌ **Not supported** — no kustomize row in GitHub's own docs (an AI-search claim of support was directly disproven) | ✅ native `kustomize` manager |
| pre-commit | ✅ **New, GA 2026-03-10** | ✅ |
| Argo CD Applications | ❌ | ✅ (`argocd` manager, but needs manual file-pattern config) |
| Arbitrary custom/regex version strings, digest pinning | ❌ | ✅ (`customType:"regex"`, `pinDigests`) |

**Source:** https://raw.githubusercontent.com/github/docs/main/data/reusables/dependabot/supported-package-managers.md (fetched directly) ; https://devblogs.microsoft.com/dotnet/the-new-dependabot-nuget-updater/ ; https://github.blog/changelog/2026-03-10-dependabot-now-supports-pre-commit-hooks/ ; https://docs.renovatebot.com/modules/manager/kustomize/ ; https://docs.renovatebot.com/modules/manager/argocd/. **Confidence:** High.
**Implication:** Dependabot alone covers ~90% of this stack (NuGet/CPM, Actions, Docker, docker-compose, devcontainers, Helm, Terraform, pre-commit). **The only gap is kustomize (and Argo CD)** — add Renovate scoped only to those files if/when K8s kustomize manifests are introduced; running both tools simultaneously, scoped to different files, is a common supported pattern.

## 15. Aspire

- **Fact:** Renamed from **".NET Aspire" to "Aspire"** (dropped the ".NET" prefix) at **major version 13 (Nov 2025)**, reflecting broader multi-language orchestration (Python/JS/TS/Java/Rust hosting). Current latest: **v13.6.0** (2026-09-29). New home: **aspire.dev**; `learn.microsoft.com/.../dotnet/aspire/...` now redirects there; GitHub repo moved to `github.com/microsoft/aspire`. **Source:** https://devblogs.microsoft.com/aspire/aspire13/ ; https://github.com/microsoft/aspire/releases.atom. **Confidence:** High.
- **Implication:** Document as "**Aspire (formerly .NET Aspire), v13.6.0**" — evaluated as a local-orchestration alternative to Docker Compose but not adopted, consistent with the project's existing docker-compose-based plan.

---

## Recommended .NET library and tooling baseline (with licenses)

*Licenses marked "✅ verified" were confirmed this pass via a direct LICENSE/metadata fetch. Licenses marked "📋 not re-verified" are well-documented publicly but weren't independently re-fetched in this research pass — treat as Medium confidence and let the `nuget-license` CI gate (Item 4) be the actual enforcement mechanism, not this table.*

| Concern | Package | Version (Oct 2026) | License | Status |
|---|---|---|---|---|
| SDK | .NET SDK | 10.0.401 (runtime 10.0.12) | MIT | ✅ verified, LTS to 2028-11-14 |
| Caching L1+L2 | ZiggyCreatures.FusionCache + `.Backplane.StackExchangeRedis` + `.OpenTelemetry` | 2.9.0 | MIT | ✅ verified — recommended over bare HybridCache |
| Test framework | xunit.v3 | 4.0.1 | Apache-2.0 | ✅ verified |
| Test platform | Microsoft.Testing.Platform | 2.4.1 | MIT | ✅ verified |
| Code coverage | Microsoft.Testing.Extensions.CodeCoverage | 18.11.2 | Microsoft .NET Library License (closed-source, free-to-use) | ✅ verified — coverlet incompatible with MTP |
| TRX reporting | Microsoft.Testing.Extensions.TrxReport | 2.4.1 | 📋 not re-verified (MIT per sibling packages) | stable |
| Mutation testing | dotnet-stryker | 5.0.0 | 📋 not re-verified (commonly MIT) | MTP support = preview |
| Architecture tests | TngTech.ArchUnitNET + `.xUnitV3` | 0.13.4 | Apache-2.0 | ✅ verified, active |
| Snapshot testing | Verify (+ `.XunitV3`) | 33.2.0 | MIT | ✅ verified via LICENSE fetch |
| Property-based testing | CsCheck | 4.9.1 | Apache-2.0 | ✅ verified; preferred over FsCheck (no F# dep) |
| Assertions | AwesomeAssertions **or** Shouldly | 9.6.0 / 4.3.0 | Apache-2.0 / BSD-3-Clause | ✅ verified — **avoid FluentAssertions ≥8.0.0** (commercial) |
| Request validation | FluentValidation | 12.1.1 | Apache-2.0 | ✅ verified, confirmed still clean |
| Minimal API validation | Microsoft.Extensions.Validation (built-in) | ships w/ 10.0.x | MIT | ✅ GA (resolver APIs experimental) |
| ORM | Microsoft.EntityFrameworkCore.SqlServer | 10.0.x | MIT | 📋 not re-verified (well-known) |
| DB driver | Microsoft.Data.SqlClient | 7.1.1 | 📋 not re-verified (well-known MIT) | active pool-clear work |
| API versioning | Asp.Versioning.Http + `.OpenApi` | 10.2.0 | 📋 **not verified this pass** — confirm before relying on it for compliance | actively maintained, MS-endorsed |
| Integration test containers | Testcontainers (+ .MsSql/.Redis/.Keycloak) | 4.15.0 | 📋 not re-verified (well-known MIT) | explicit image strings now required |
| Contract testing (consumer/provider) | PactNet | 5.0.1 | 📋 not re-verified (well-known MIT) | ⚠️ dormant ~19 months, no linux-musl |
| Contract broker | Pact Broker (self-hosted OSS) | 2.122.0 | 📋 not re-verified | recommended over PactFlow's uncertain free tier |
| OpenAPI breaking-change diff | oasdiff | 1.33.0 | 📋 not re-verified (commonly Apache-2.0) | full 3.1 support |
| OpenAPI client generation | Microsoft Kiota | 1.35.0 | 📋 not re-verified (commonly MIT) | 3.1 input support stable since Feb 2025 |
| OpenAPI object model | Microsoft.OpenApi | 2.0.0 (pin 2.x line) | 📋 not re-verified (commonly MIT) | what ASP.NET Core 10 uses internally |
| OpenTelemetry core+OTLP | OpenTelemetry / `.Exporter.OpenTelemetryProtocol` | 1.19.1 | 📋 not re-verified (commonly Apache-2.0) | stable 4+ years |
| OTel SqlClient instrumentation | OpenTelemetry.Instrumentation.SqlClient | 1.19.0 | 📋 not re-verified | **stable** |
| OTel EF Core instrumentation | OpenTelemetry.Instrumentation.EntityFrameworkCore | 1.19.1-beta.1 | 📋 not re-verified | ⚠️ **permanently beta** |
| OTel Redis instrumentation | OpenTelemetry.Instrumentation.StackExchangeRedis | 1.19.0-beta.1 | 📋 not re-verified | ⚠️ **permanently beta** |
| Docs site | DocFX | 2.81.0 | 📋 not re-verified (commonly MIT) | .NET-Foundation/community-governed since Nov 2022 |
| License CI gate | nuget-license | 4.0.18 | 📋 not re-verified (commonly MIT) | actively maintained, recommended |
| Local orchestration (evaluated, not adopted) | Aspire | 13.6.0 | 📋 not re-verified | renamed from ".NET Aspire" Nov 2025 |

---

## Top risks (ranked by severity)

1. **🔴 Silent multi-replica cache staleness — HybridCache has no cross-node L1 invalidation.** Confirmed open/unresolved upstream (`dotnet/extensions#5517`, no milestone), not fixed before .NET 11. A canary deployment or stock/price update can leave other replicas serving stale reads for a full local-cache TTL, with no error or log signal. **Mitigation:** adopt FusionCache + Redis/Valkey backplane via `.AsHybridCache()`; add a Testcontainers-based multi-instance invalidation test before trusting it in production.

2. **🔴 License contamination of a public Apache-2.0 repo.** FluentAssertions ≥8.0.0, MediatR ≥13.0.0, AutoMapper ≥15.0.0, and MassTransit ≥9.0.0 are all now commercial/RPL-licensed; a well-intentioned contributor PR (or an IDE auto-suggest) could easily reintroduce one. **Mitigation:** `nuget-license` CI gate from day one with an explicit forbidden-license list, not just a one-time manual audit.

3. **🔴 Forwarded-headers hardening silently breaks auth/routing behind the canary proxy.** The 8.0.17/9.0.6 security change (carried into 10) drops all `X-Forwarded-*` headers from any IP not explicitly in `KnownProxies`/`KnownNetworks` — this fails silently (no exception), manifesting as broken Keycloak OIDC redirects, wrong client-IP rate limiting, and broken canary routing only after deployment. **Mitigation:** explicitly enumerate proxy/pod-network CIDRs in every replica's config; add an integration test asserting forwarded-header behavior behind a simulated proxy.

4. **🟠 DbContext pooling + credential rotation can silently serve stale credentials.** `OnConfiguring` on a pooled context runs once at pool-fill time; a naive static connection string will not pick up a rotated credential for that pooled instance's remaining lifetime, risking intermittent auth failures after a rotation. **Mitigation:** pair `AddDbContextPool` with an `IDbConnectionInterceptor.ConnectionOpeningAsync` cached-refresh pattern, and call `ClearAllPools()` on rotation.

5. **🟠 coverlet is explicitly incompatible with native MTP `dotnet test` mode.** This is a foundational CI-pipeline decision (MTP + closed-source MS coverage extension, vs. legacy VSTest mode + coverlet) that must be made upfront, not discovered mid-project. **Mitigation:** decide explicitly; if open-source coverage tooling is a hard requirement, stay on legacy VSTest mode and skip native MTP for now.

6. **🟠 PactNet is dormant (~19 months, no release) and cannot run in Alpine/musl containers.** Risk to long-term contract-testing pipeline maintenance and to any CI image-slimming effort. **Mitigation:** pin version explicitly, keep the Pact test stage on a glibc-based (Debian/Ubuntu) image, and monitor the stalled gRPC/plugin PRs for a future release.

7. **🟡 PactFlow/"Swagger Contract Testing" free-tier terms are opaque mid-rebrand.** Don't architect around an unverified commercial free tier. **Mitigation:** self-host Pact Broker OSS (confirmed healthy, v2.122.0, pushed same day as this research).

8. **🟡 EF Core and StackExchange.Redis OpenTelemetry instrumentation packages have never shipped a stable release.** Both are permanently `-beta`, with an explicit upstream breaking-change warning. **Mitigation:** pin exact versions, review changelogs on every bump, document these two as "experimental, reviewed-on-upgrade" in the observability docs rather than presenting them as GA.

9. **🟡 Dependabot cannot update kustomize image tags or Argo CD manifests.** Invisible dependency drift if K8s manifests are added later. **Mitigation:** add Renovate scoped only to `kustomization.yaml`/Argo CD files alongside Dependabot for everything else.

10. **🟢 DocFX's build-time generator can't emit YAML in .NET 10; only JSON.** Minor pipeline friction for a YAML-authored contract. **Mitigation:** standardize the hand-authored contract on JSON, or add a conversion step before diffing.

11. **🟢 Several testing-ecosystem components are preview/alpha quality** (Stryker.NET's MTP runner, `Microsoft.Testing.Extensions.JUnitReport`, TUnit as a sole framework). Acceptable to showcase in a teaching repo but shouldn't be the only/critical path if CI stability is paramount. **Mitigation:** keep xUnit v3 + `Microsoft.Testing.Extensions.TrxReport` as the stable backbone; treat the others as clearly-labeled "experimental" additions.

12. **🟢 NetArchTest (original and eNhancedEdition fork) are both abandoned/dormant.** Low severity only because the fix is trivial. **Mitigation:** start directly with `TngTech.ArchUnitNET` + `.xUnitV3` — no migration needed if chosen from day one.
