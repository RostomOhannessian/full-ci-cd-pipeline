---
applyTo: "src/**/*.cs,src/**/*.csproj,src/**/*.props"
---

# C# source and project rules

Applies from Phase 1 (WP1.1).

- Keep the layer boundaries from plan section 8.1 strict: `Domain` depends on nothing but the BCL.
- Keep `Application` dependent only on `Domain`; define ports there, not in `Infrastructure` or `Api`.
- Keep `Infrastructure` and `Api` pointed inward; `Api` is the composition root and must not contain business logic.
- Keep `Contracts` versioned and transport-only; DTO evolution belongs there, not in domain or persistence models.
- Put repositories, `IUnitOfWork`, `ICatalogCache`, `IIdGenerator`, `ICurrentPrincipal`, outbox dispatch, and telemetry ports in `Application`.
- Implement CQRS with explicit `ICommandHandler<,>` and `IQueryHandler<,>` plumbing; endpoints inject the concrete handler interface directly.
- Do not add MediatR or any mediator-style runtime dispatch. Why: ADR-0004 forbids the current MediatR license line, and section 8.2 wants explicit teaching-friendly plumbing.
- Keep handlers `internal sealed` and asynchronous; accept `CancellationToken` on every async boundary.
- Keep endpoint code thin: map HTTP input to a command or query, call the handler, and translate the result.
- Preserve the decorator pipeline order from section 8.2: telemetry and logging, validation, authorization, idempotency or query caching, transaction, then handler.
- Keep decorator registration explicit per feature or per handler family; do not hide business behavior behind reflection-heavy magic.
- Use repository plus unit-of-work patterns over one `DbContext` transaction per command, as required by section 8.4.
- Persist domain events through the transactional outbox; immediate post-commit dispatch is best effort only, never the correctness path.
- Keep outbox handlers idempotent and receipt-aware, because delivery is at least once by design.
- Put cache behavior behind `ICatalogCache`; do not reference FusionCache outside its adapter and do not fall back to HybridCache-only designs.
- Use `TimeProvider` from the BCL and inject it through application services so tests stay deterministic.
- Model concurrency and HTTP semantics exactly as planned: strong `ETag` values from `rowversion`, `If-Match` on writes, `If-None-Match` on reads, and `Idempotency-Key` on `POST` creates.
- Map application errors to RFC 9457 Problem Details through one shared table; return 428, 412, 409, and 422 only for the cases section 8.4 defines.
- Enforce identity with exact issuer and audience checks, the RS256 and ES256 allowlist, scope parsing, and policy-based authorization from section 8.7.
- Treat category writes and product discontinuation as admin-only flows, and keep the permission matrix testable.
- Keep nullable enabled and analyzers as errors in project or props files; do not add warning suppressions without a documented reason.
- Use constructor injection only; `IServiceProvider` is allowed only in the composition root, never in handlers or infrastructure services.
- Keep state non-static and immutable by default; ArchUnitNET will enforce no static mutable state.
- Write structured logs with message templates and named properties; never interpolate secrets, tokens, or personal data into log lines.
- Do not add the other libraries ADR-0004 forbids (AutoMapper 15+, MassTransit 9+, FluentAssertions 8+) or any non-OSI linked dependency.
