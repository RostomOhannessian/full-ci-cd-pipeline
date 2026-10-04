---
applyTo: "tests/**/*"
---

# Test rules

Applies from Phase 1 (WP1.1).

- Use xUnit v3 on Microsoft.Testing.Platform for .NET tests; do not introduce NUnit, MSTest, or TUnit without a new ADR.
- Use AwesomeAssertions for fluent assertions and keep FluentAssertions 8+ out of the repository per ADR-0004.
- Use NSubstitute sparingly; prefer hand-written fakes or builders when they make the test easier to read.
- Use Testcontainers for SQL Server, Valkey, and Keycloak integration paths; do not replace real dependencies with mocks where the failure mode matters.
- Use ArchUnitNET for architecture rules, Verify for snapshots, and CsCheck for property-based tests named in ADR-0018.
- Tag every .NET test with `[Trait("Requirement", "REQ-...")]` using the exact `REQ-<AREA>-<NNN>` format from section 10.2.
- Put requirement IDs in metadata blocks for non-.NET tests such as Chainsaw, Kyverno, k6, Pester, and Terraform so `governance trace` can read them.
- Keep test names behavior-first and specific; a reader should see the scenario, action, and expected result from the name alone.
- Keep architecture-rule, contract, and snapshot baselines reviewed like code; an approval change is part of the behavior change.
- Cover the negative and failure cases the plan names: denied authorization, stale or missing concurrency headers, replayed or conflicting idempotency keys, dependency outages, and credential rotation.
- Use `FakeTimeProvider` for time and an injected ID generator for identifiers; never call the system clock or `Guid.NewGuid()` in test logic.
- Run under the invariant culture and log CsCheck seeds so failures can be replayed exactly.
- Pin container images by digest and share them through assembly fixtures; start them once per assembly unless isolation requires more.
- Keep Verify snapshots deterministic by normalizing unstable fields such as timestamps, IDs, and machine-specific paths before approval.
- Do not use `Thread.Sleep`, `Task.Delay`, or time-based polling to make tests pass; wait on observable state with bounded helpers instead.
- Keep tests order-independent; declare any parallelism policy per assembly and never rely on implicit execution order.
- Use builders and synthetic data only; never copy real people, real secrets, or production payloads into fixtures.
- Keep authorization, concurrency, and idempotency matrices exhaustive when an endpoint or command changes.
- Keep Pact provider verification on a real Kestrel socket when that suite arrives; `TestServer` does not satisfy the planned PactNet path.
- Hold the Phase 1 thresholds from section 10.4: Domain line coverage at least 90%, Application line coverage at least 85%, solution line coverage at least 80%, and branch coverage at least 80% for Domain and Application.
- Hold the Domain mutation score at least 70% once Stryker's MTP runner is GA; until then, report the score and improve it instead of weakening the suite.
- Treat zero analyzer warnings as a build rule, not a stretch goal; a test project that needs a suppression must justify it in code review.
- Keep docs and test-plan updates alongside test additions when a requirement, risk, or failure mode changes.
- Follow the zero-tolerance flakiness policy from section 10.5: no blanket retries, at most one reported infrastructure retry, and no quarantined flaky test older than seven days.
