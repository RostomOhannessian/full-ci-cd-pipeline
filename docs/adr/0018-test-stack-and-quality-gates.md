---
title: "ADR-0018: Standardize on the Phase 1 test stack and quality gates"
description: "Use the verified .NET 10 testing stack and the plan's explicit quality gates so every phase builds on the same deterministic test baseline."
status: proposed
date: 2026-10-04
decision-makers: ["@RostomOhannessian"]
accepted-in: "WP1.1"
supersedes: []
superseded-by: []
tools: [xunit-v3, microsoft-testing-platform, ms-code-coverage, awesomeassertions, nsubstitute, archunitnet, verify, cscheck, stryker-net, testcontainers-dotnet, pactnet, pact-broker, schemathesis, k6, chainsaw, pester, psscriptanalyzer, kyverno-cli, coverlet, netarchtest, fluentassertions]
related-adrs: ["0004", "0017"]
evidence:
  - docs/research/2026-10-04-dotnet-ecosystem-verification.md
---

# ADR-0018: Standardize on the Phase 1 test stack and quality gates

## Context and problem statement

Plan section 10 and [the Phase 1 plan](../plans/phases/phase-1-api-core.md) make testing infrastructure part of the architecture, not a later add-on. Every later phase depends on the same baseline: deterministic unit tests, architecture rules, integration tests with real dependencies, contract checks, traceability, and explicit thresholds.

The .NET ecosystem verification record supports a specific stack for .NET 10. It says Microsoft.Testing.Platform remains opt-in through `global.json`, xUnit v3 has native MTP support, `coverlet.collector` is explicitly incompatible with MTP v2, `TngTech.ArchUnitNET.xUnitV3` is active while NetArchTest is dormant, Verify is MIT, CsCheck is a good fit for C# property tests, and PactNet 5.0.1 is dormant and requires a real socket rather than `TestServer` (checked 2026-10-04). It also confirms the license-driven move from FluentAssertions to AwesomeAssertions and the need to treat Stryker's MTP runner as preview only.

The plan adds the policy layer. Plan section 10.2 requires requirement IDs and generated traceability. Plan section 10.3 sets the determinism rules. Plan section 10.4 sets explicit thresholds. Plan section 10.5 sets a zero-tolerance flakiness policy and CI time budgets. This ADR captures that baseline and points readers to [the implementation plan](../plans/implementation-plan.md) for the full text.

## Decision drivers

- The default stack must work on .NET 10 without hidden compatibility traps.
- Test output must support deterministic CI and traceability from requirements to evidence.
- License policy from [ADR-0004](./0004-licensing-and-dependency-license-policy.md) must hold for testing dependencies too.
- The stack must scale from Phase 1 code tests to later infrastructure, performance, and rollback drills.

## Considered options

1. Stay on VSTest and coverlet to keep the older .NET test defaults.
2. Use Microsoft.Testing.Platform with Microsoft Code Coverage and the verified companion tools.
3. Standardize on TUnit as the main framework.
4. Standardize on MSTest or NUnit for the core test framework.

## Decision outcome

Chosen option: **Use Microsoft.Testing.Platform with Microsoft Code Coverage and the verified companion tools**, because it best matches .NET 10, the plan's determinism goals, and the repository's teaching scope.

### Quality gates in summary

- **Traceability:** tests carry `REQ-<AREA>-<NNN>` identifiers, and `governance trace` fails if an in-scope requirement has no test or evidence.
- **Determinism:** tests use `FakeTimeProvider`, injected ID generation, invariant culture, pinned container images, explicit parallelism, and synthetic data builders.
- **Thresholds:** line coverage is at least 90% for Domain, 85% for Application, and 80% across the solution; branch coverage is at least 80% for Domain and Application; mutation score is at least 70% for Domain once Stryker's MTP runner is GA; builds allow zero analyzer warnings; docs allow zero DocFX warnings and zero broken internal links; performance uses the k6 SLOs from plan section 8.11.
- **Flakiness policy:** zero tolerance, no blanket retries, one reported infrastructure retry at most, and any quarantined flaky test expires after 7 days while still running nightly.

### Consequences

- Good: the repository gets one coherent .NET 10 testing baseline: xUnit v3, MTP, ArchUnitNET, Verify, CsCheck, AwesomeAssertions, Testcontainers, and focused use of NSubstitute.
- Bad: MTP-native coverage depends on Microsoft's closed-source coverage extension, and some companion tools are preview or dormant. Mitigation: document those status flags honestly, keep PactNet on a real Kestrel socket, and keep Stryker score reported rather than gated until its MTP runner is GA.
- Neutral: this ADR standardizes the baseline, but not every suite runs on every PR. Plan section 10.1 still uses blast-radius-driven execution.

### Confirmation

- **WP1.0** spikes the riskiest integrations: MTP plus Microsoft Code Coverage, Stryker's preview runner, PactNet with `UseKestrel()`, and Testcontainers behavior on supported hosts.
- **WP1.1** establishes the solution, `global.json` opt-in for MTP, coverage collection, architecture tests, and the initial CI gate.
- **WP1.2** adds `governance trace`, which enforces the requirement-ID mechanism.
- Later work packages in plan section 10.1 validate the rest of the stack: contract conformance, Pact, Schemathesis, Chainsaw, k6, Pester, and rollback drills.

## Pros and cons of the options

### Stay on VSTest and coverlet to keep the older .NET test defaults

- Good: avoids the MTP transition and keeps fully open-source coverage tooling.
- Bad: rejects the .NET 10 direction the plan already chose and gives up native xUnit v3 plus MTP integration benefits.

### Use Microsoft.Testing.Platform with Microsoft Code Coverage and the verified companion tools

- Good: matches current .NET 10 support, keeps xUnit v3 native, and fits the verified tool choices for architecture, snapshot, property, and integration testing (checked 2026-10-04).
- Bad: requires one closed-source coverage component and careful caveats around preview or dormant tools.

### Standardize on TUnit as the main framework

- Good: modern MTP-native design and active development.
- Bad: it is newer and less battle-tested than xUnit for the repository's core baseline.

### Standardize on MSTest or NUnit for the core test framework

- Good: mature ecosystems and familiar workflows.
- Bad: they do not align as cleanly with the specific evidence gathered for xUnit v3, ArchUnitNET.xUnitV3, and the planned teaching materials.

## Revisit when

- coverlet becomes compatible with native MTP in a stable release.
- Stryker's MTP runner reaches GA and the mutation threshold can move from reported to blocking.
- PactNet receives an actively maintained release that changes its risk profile or hosting requirements.
- The team adopts a different .NET test baseline through a later ADR with updated evidence.

## Further reading

- [.NET ecosystem verification record](../research/2026-10-04-dotnet-ecosystem-verification.md) (checked 2026-10-04)
- [Implementation plan, sections 10.2 to 10.5](../plans/implementation-plan.md) for the full traceability, determinism, threshold, and flakiness rules
