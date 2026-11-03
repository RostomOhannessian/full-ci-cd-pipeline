---
title: "ADR-0004: Adopt Apache-2.0 and enforce an open-source dependency license policy"
description: "License the repository under Apache-2.0 and allow only OSI-approved permissive or weak-copyleft linked dependencies, with documented tool-only exceptions."
status: accepted
date: 2026-10-04
decision-makers: ["@RostomOhannessian"]
supersedes: []
superseded-by: []
tools: ["nuget-license", "dependency-review", "vault", "terraform", "k6", "ms-code-coverage", "fluentassertions", "mediatr", "automapper", "masstransit", "sqlclient-sni-runtime"]
related-adrs: ["0002", "0008", "0018", "0020"]
evidence:
  - docs/research/2026-10-04-dotnet-ecosystem-verification.md
  - docs/research/2026-10-04-decision-critical-addendum.md
  - docs/research/spikes/1.c-mtp-coverage-stryker.md
  - docs/testing/evidence/2026-11-03-sni-runtime-license-exception-run.md
---

# ADR-0004: Adopt Apache-2.0 and enforce an open-source dependency license policy

## Context and problem statement

This repository is a public teaching and portfolio project. Its code, docs, and examples should be reusable by
learners and recruiters without license ambiguity, and later phases will publish artifacts, SBOMs, and evidence
bundles. Plan finding F15 showed that several familiar .NET libraries changed from open-source terms to commercial or
non-OSI licenses during 2025 and 2026, so "use whatever is common" is no longer safe.

Plan section 9 requires a license allowlist, vulnerability review, and documented exceptions for tools that are used
but not linked into distributed artifacts. Plan section 2 also frames the repository as a public portfolio, which makes
patent clarity and contribution terms more important than they would be for a private experiment.

The policy must distinguish between linked dependencies, which shape the terms of distributed code, and operational
tools, which may run in development or CI without being linked, redistributed, or offered as a service by this
repository.

## Decision drivers

- Keep the repository straightforward to study, fork, and reuse.
- Prefer an explicit patent grant for public code contributions.
- Prevent accidental adoption of newly commercial or non-OSI linked dependencies.
- Allow operational tooling that is acceptable only when it stays outside shipped binaries and services.
- Make compliance enforceable in CI, not dependent on memory.

## Considered options

1. Apache-2.0 with an enforced linked-dependency allowlist
2. MIT with a lighter dependency policy
3. Leave the repository unlicensed until launch
4. Dual-license the repository

## Decision outcome

Chosen option: **Apache-2.0 with an enforced linked-dependency allowlist**, because it best matches the repository's
public teaching role while giving contributors and downstream users an explicit patent grant.

The repository license is Apache-2.0. Contributions are accepted inbound under the repository license, consistent with
Apache-2.0 section 5, and the project does not use a separate CLA.

Linked dependencies must use OSI-approved permissive or weak-copyleft terms. The allowlist is enforced in CI, and the
policy explicitly forbids FluentAssertions 8 and later, MediatR 13 and later, AutoMapper 15 and later, MassTransit 9
and later, and any non-OSI license such as the current QuestPDF community license.

Tools that are used operationally but are not linked, redistributed, or offered as a service by this repository may
carry other terms, but each such case requires a documented note. On 2026-10-04, the GitHub license API for
`hashicorp/terraform` returned SPDX `NOASSERTION` and license name `Other`, and the same endpoint for `grafana/k6`
returned SPDX `AGPL-3.0` and license name `GNU Affero General Public License v3.0`. The decision-critical addendum
also records Vault as BSL 1.1, and the plan records Microsoft Code Coverage as closed source but free to use. Those
tools are acceptable here only under the tool-only rule.

The same rule covers Vault Agent and the Vault Secrets Operator (both BSL 1.1), and the Grafana, Loki, and Tempo
services (AGPL-3.0, confirmed through the same GitHub license endpoint on 2026-10-04). The repository runs them
unmodified in the local cluster and does not offer any of them as a hosted service. The tool inventory carries the
note for every such case, and a gate in the Documentation.Auditor fails the build when a tool with a non-permissive
license has none.

Enforcement happens at three levels. WP1.1 adds a `nuget-license` gate with a forbidden list. WP2.3 adds dependency
review on pull requests. The v1.0 launch generates third-party notices from the release SBOMs so the published
artifacts carry their final dependency evidence.

### Consequences

- Good: the repository is easy to reuse and contributes a clear patent position for public portfolio code.
- Good: the license policy prevents quiet drift into commercial or non-OSI linked dependencies.
- Bad: operational exceptions need ongoing review; the mitigation is a documented note per exception and CI checks on
  linked packages.
- Bad: some familiar libraries are intentionally off-limits; the mitigation is to document safe alternatives early and
  enforce the policy in tooling rather than in reviewer memory.
- Neutral: some tools remain acceptable only as build-time, CI-time, or local-only tooling and never as linked runtime
  dependencies.

### Confirmation

WP1.1 confirms this decision by failing the build when the solution adds a forbidden dependency license.

WP2.3 confirms it by running dependency review for pull requests and checking both vulnerabilities and license posture.

The v1.0 launch gate confirms it by generating SBOM-backed third-party notices for released artifacts and by reviewing
the exception notes for tool-only licenses.

Execution record (WP1.1, 2026-10-08): the license gate and the forbidden list exist and were shown to fail on purpose. CI runs
`nuget-license` over the solution with transitive packages (28 packages at the time) against `governance/policies/license-policy.json`.
Removing the one exception, or removing Apache-2.0 from the allow-list, made the gate exit with 1. The forbidden list from this ADR
(FluentAssertions 8 and later, MediatR 13 and later, AutoMapper 15 and later, MassTransit 9 and later, and QuestPDF) is in
`governance/policies/forbidden-packages.json` and is checked from the lock files by the architecture tests, because `nuget-license` has no
forbidden-list option. [ADR-0020](0020-dependency-intake-controls.md) records the design. The tool-only rule is enforced for the one
exception: the coverage extension's override names the exact version 18.11.2, and a test fails if it appears in a production project.

Execution record (WP1.3, 2026-10-08): the documentation auditor enforces the tool-only rule on the tool inventory. An entry whose license is
not on the permissive list in `governance/policies/documentation-policy.yaml` must carry a note, so the exception to this ADR cannot sit in the
inventory without a reason ([ADR-0023](0023-documentation-auditor.md)). A test runs the rule against a conforming and a violating inventory,
and the audit of the real inventory passes. The decision does not change.
Execution record (WP1.0, 2026-10-06): [spike 1.c](../research/spikes/1.c-mtp-coverage-stryker.md) read the license of
`Microsoft.Testing.Extensions.CodeCoverage` 18.11.2 from the package itself. Its `License.txt` holds the Microsoft Software License
Terms for the Microsoft .NET Library, the package metadata uses a license file instead of an SPDX expression, and the project page
describes the component as closed source. It is a test-time tool and is not linked into any shipped binary, so it falls under the
tool-only rule, and the `ms-code-coverage` inventory entry carries the required license note. The other packages the spikes used
were MIT or Apache-2.0 by package metadata: Microsoft.Testing.Platform, the TRX extension, xunit.v3.mtp-v2, PactNet, and
Stryker.NET. [Spike 1.d](../research/spikes/1.d-testcontainers-mssql.md) also found that the Testcontainers SQL Server builder accepts
the SQL Server end-user license on the user's behalf. The decision does not change.

## Pros and cons of the options

### Apache-2.0 with an enforced linked-dependency allowlist

- Good: explicit patent grant, clear reuse terms, and a policy that can be automated.
- Good: separates acceptable linked code from acceptable operational tooling.
- Bad: more policy detail to maintain than a bare top-level license file.

### MIT with a lighter dependency policy

- Good: familiar and simple.
- Bad: weaker patent language for a public engineering portfolio.
- Bad: a lighter policy does not address the concrete license drift found in the .NET ecosystem review.

### Leave the repository unlicensed until launch

- Good: defers a decision while the codebase is small.
- Bad: blocks clear reuse and undermines the public teaching goal from the start.
- Bad: makes contribution and dependency-policy decisions ambiguous during the exact phases when they matter most.

### Dual-license the repository

- Good: could preserve flexibility for future commercialization.
- Bad: unnecessary complexity for a solo-maintained teaching repository.
- Bad: complicates contribution terms and the portfolio story without solving the linked-dependency problem.

## Revisit when

- A proposed linked dependency arrives under non-OSI terms or changes license after adoption.
- The licenses or operational terms of Vault, Terraform, k6, Microsoft Code Coverage, Grafana, Loki, or Tempo change
  materially, or the repository modifies one of the AGPL services.
- The repository starts redistributing, embedding, or offering a currently tool-only component in a way that would make
  the exception rule invalid.

## Amendment of 2026-11-03: a native Windows runtime that no shipped artifact contains

The maintainer approved this amendment on 2026-11-03. It adds a second kind of license exception. The decision above, the
allow-list, the forbidden list, and the tool-only rule are unchanged.

### Context of the amendment

WP1.8 (persistence and outbox, Issue #10) is the first work package that links Microsoft.Data.SqlClient. The license gate
(`dotnet nuget-license ... -err`) then failed on exactly one package, `Microsoft.Data.SqlClient.SNI.runtime` 7.1.0
(checked 2026-11-03 on the WP1.8 branch, see the [evidence record](../testing/evidence/2026-11-03-sni-runtime-license-exception-run.md)).
Facts about the package, all read from the package itself on 2026-11-03:

- It is a transitive dependency of Microsoft.Data.SqlClient 7.1.1 (`[7.1.0, 8.0.0)`) and of Microsoft.Data.SqlClient 6.1.7
  (`6.0.3`), the version that the EF Core SQL Server provider 10.0.14 depends on. It appears in the lock files of
  `src/Catalog.Api`, `src/Catalog.Infrastructure`, and two test projects. Its own description says it is
  an internal implementation package that must not be referenced directly.
- It holds three native Windows libraries and nothing else: `runtimes/win-x64`, `runtimes/win-x86`, and `runtimes/win-arm64`,
  each with `Microsoft.Data.SqlClient.SNI.dll`.
- It carries "Microsoft Software License Terms" in a `LICENSE.txt`, which `nuget-license` cannot map to an SPDX identifier
  (the gate reports "Unable to determine license from the given license file"). The terms are not OSI-approved.
- The terms give the right to "install and use any number of copies of the software to develop and test your applications".
  They allow distribution of the object code only inside an application, and only if the distributor binds distributors and end
  users to terms that protect it at least as much and indemnifies Microsoft. The terms also forbid offering it as a stand-alone
  product.

The one exception that exists is tool-only, and `DependencyPolicyTests` keeps every tool-only package out of production lock
files. The package is in production lock files, so neither the gate nor the test could pass without a decision.

### Options considered for the amendment

1. A narrow exception for a native runtime of the Windows host that no shipped artifact contains, by exact package and exact
   version (chosen).
2. Keep the policy, and make the Windows native assets unreachable from the projects. That means a Windows host cannot run the API
   or the persistence tests until the managed networking mode of SqlClient on Windows is decided and tested. It was not tested.
3. Use another SQL Server driver. None exists for EF Core.

### Decision of the amendment

Chosen option: **a `windows-native-runtime` exception for `Microsoft.Data.SqlClient.SNI.runtime` 7.1.0 and nothing else**, because
every SqlClient version in play depends on the package, the Phase 2 images are Linux-only and do not contain it, and a publish for
Linux does not copy it.

Each override in `governance/policies/license-overrides.json` now states its kind. The two kinds differ in where they may appear:

| Kind | Where its package may appear | Entries |
| --- | --- | --- |
| `tool-only` (unchanged) | Test and tool projects only. Never in `src` | `Microsoft.Testing.Extensions.CodeCoverage` 18.11.2 |
| `windows-native-runtime` (new) | Any lock file, but only as a transitive dependency, at the exact approved version | `Microsoft.Data.SqlClient.SNI.runtime` 7.1.0 under `LicenseRef-Microsoft-SqlClient-SNI-WindowsRuntime` |

The scope of the new exception is the native Windows assets of that one package version, as a dependency of the SQL Server driver.
It does not cover another package, another version of this package, a direct reference, or any file that a shipped artifact
contains. The override records its scope and its reason, and a test fails when either is missing.

The exact version is pinned in four places that must change together: the override, the allow-list entry, the approval in
`governance/policies/security-policy.yaml`, and `DependencyPolicy.ApprovedWindowsNativeRuntime` in the architecture tests. A
Dependabot update of SqlClient that moves the package to another version fails the license gate and both test suites until a
person reads the new terms and amends this record.

### Why the exception is acceptable

- **No shipped artifact contains it.** The Phase 2 images are Linux-only: WP2.1 plans a multi-stage Dockerfile with a chiseled
  non-root runtime for amd64 and arm64 (planned, [Phase 2 plan](../plans/phases/phase-2-secure-ci.md), checked 2026-11-03). On
  2026-11-03 a publish of `src/Catalog.Api` with a Linux runtime identifier held no file of the package and no mention of it in
  `Catalog.Api.deps.json`. The WP1.8 branch ran its persistence suites against SQL Server in the Linux SDK image on 2026-11-02, so
  the driver connects without the Windows library there. A publish with no runtime identifier does copy the three Windows
  libraries, so the image build must publish for a Linux runtime identifier. The publish check below fails when it does not.
- **The use is the licensed use.** The package runs on Windows developer hosts and in the tests that they run, which the terms
  allow ("develop and test your applications"). The repository holds the package ID, the version, and a content hash in lock
  files, never the library itself, and it does not offer the package as a service.
- **The distribution terms are never accepted.** The indemnity and the protective-terms duty for distributors are the cost of
  shipping the library. The repository ships no Windows artifact, so it takes on neither. The conditions below say when that stops
  being true.

### Conditions that make the exception invalid

The exception stops holding, and this record must be revisited, when any of these happens:

1. A shipped artifact contains the package: a publish output, an image, an archive, or an SBOM that lists it.
2. A Windows container image is added, or a publish with a Windows runtime identifier, self-contained or not, is added.
3. The version of the package changes, through a SqlClient update or any other change.
4. The license terms of the package change. Read `LICENSE.txt` again on every version.
5. SqlClient offers a managed-only option on Windows that makes the native package unnecessary, so the exception can be removed.

### What enforces it

| Check | State | Where |
| --- | --- | --- |
| The gate accepts the override only for the exact version | Enforced today | The `license` job of `ci.yml`, with the override file |
| Each override has a known kind. A native override must be the approved package, version, and license, and must record a scope and a reason. A tool-only package stays out of `src`. The native package stays at the approved version and is never a direct reference | Enforced today, with a violating fixture for each rule | `DependencyPolicyTests` and the `license-policy` rule of `governance security` |
| No production project selects a Windows runtime identifier, and no Dockerfile builds on a Windows image (condition 2) | Enforced today, with violating fixtures | `WindowsArtifactPolicyTests` |
| A folder of publish output holds no file of the package (condition 1) | The script and its tests exist and passed on fixtures. It ran by hand on two real publishes on 2026-11-03: it failed on the publish with no runtime identifier and passed on the Linux publish. No pipeline step runs it yet | `tools/ci/Test-NoWindowsNativeRuntime.ps1` |
| The image build publishes for a Linux runtime identifier and runs the publish check on the real output and on the image file system | Planned, WP2.1 | The production Dockerfile and its CI job |
| The SBOM of each image does not list the package | Planned, WP2.4 | The SBOM step of the release workflow |
| Dependency review on pull requests honors the same exact exception | Planned, WP2.3 | The dependency review configuration |

### Consequences of the amendment

- Good: the WP1.8 persistence layer can merge without weakening the gate for any other package, and the exception names the exact
  package, version, scope, and reason in machine-readable files.
- Good: the two kinds of exception are different in the tests, so a tool-only package can still never reach production code, and a
  new native exception cannot enter without a change to the approval, to the test constant, and to this record.
- Bad: a closed-source native library runs on Windows developer hosts. Mitigation: it comes from nuget.org with the required
  repository signature and a content hash in the lock file ([ADR-0020](0020-dependency-intake-controls.md)), and only the Windows
  development and test path loads it.
- Bad: the exception is only as good as the promise that no Windows artifact ships. Mitigation: the table above names what is
  enforced today and what is planned, and the planned checks are part of the WP2.1, WP2.3, and WP2.4 deliveries.
- Neutral: a Dependabot update of SqlClient now surfaces a review of the terms of this package.
- Neutral: [the threat model](../security/threat-model.md) does not change. The exception adds no boundary, asset, or actor, because
  dependency intake (TB2) already covers a package from nuget.org with a required signature, a content hash in the lock file, and
  the license gate. The change is to a license posture, and the checks above hold it.

## Further reading

- Implementation plan v2: [../plans/implementation-plan.md](../plans/implementation-plan.md) (checked 2026-10-04)
- .NET ecosystem verification: [../research/2026-10-04-dotnet-ecosystem-verification.md](../research/2026-10-04-dotnet-ecosystem-verification.md) (checked 2026-10-04)
- Decision-critical addendum: [../research/2026-10-04-decision-critical-addendum.md](../research/2026-10-04-decision-critical-addendum.md) (checked 2026-10-04)
- Secrets broker ADR: [./0008-secrets-broker-vault-over-openbao.md](./0008-secrets-broker-vault-over-openbao.md) (checked 2026-10-04)
- Microsoft.Data.SqlClient.SNI.runtime 7.1.0 on NuGet: [nuget.org](https://www.nuget.org/packages/Microsoft.Data.SqlClient.SNI.runtime/7.1.0) (checked 2026-11-03)
- Evidence record of the amendment: [../testing/evidence/2026-11-03-sni-runtime-license-exception-run.md](../testing/evidence/2026-11-03-sni-runtime-license-exception-run.md) (checked 2026-11-03)
