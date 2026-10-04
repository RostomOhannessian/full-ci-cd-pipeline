# Review checklist

Use this checklist for AI and human review. Mark a finding as **blocking** when it would violate the plan, an ADR, a security boundary, or a required gate; mark it as **should fix** when it should be corrected before merge but is not a release-threatening defect; mark it as **nit** when it is a small improvement with no policy or correctness impact. Cite the plan section, ADR, or rule behind each finding and suggest a fix.

## Scope and plan alignment

- [ ] The change stays within the approved work-package or phase scope, or it includes an explicit plan amendment.
- [ ] The change does not alter plan outcomes or brief-traceability rows without explicit maintainer approval.
- [ ] The change does not describe planned tooling, code, workflows, or infrastructure as already implemented.
- [ ] The change updates status and evidence references when scope, progress, or completion state changed.

## Architecture

- [ ] The change preserves the layer boundaries in plan section 8.1 when code exists (from WP 1.1).
- [ ] Domain code depends only on the BCL and contains no framework or persistence types (from WP 1.1).
- [ ] Application code depends only on Domain and defines ports instead of calling infrastructure directly (from WP1.7).
- [ ] Infrastructure implements application ports without leaking infrastructure types inward (from WP 1.8).
- [ ] API code stays a composition root and transport layer, not a home for business logic (from WP 1.10).
- [ ] The change uses explicit CQRS handlers and decorators rather than mediator-style runtime dispatch by default (from WP 1.7).
- [ ] The change does not introduce banned libraries or non-OSI linked dependencies (from WP 1.1).

## Security

- [ ] The change does not add secrets, personal data, private paths, or unsafe sample credentials.
- [ ] The change keeps untrusted input as data and does not combine untrusted content with write tokens or privileged execution.
- [ ] The change preserves least privilege for GitHub tokens, service accounts, identities, and runtime permissions.
- [ ] The change keeps security controls honest by failing, blocking, or documenting risk rather than silently skipping checks.
- [ ] The change updates the threat model when it adds or changes a trust boundary, data flow, or credential.

## Tests and traceability

- [ ] The change adds or updates tests or evidence for every affected requirement when executable code or behavior changes (from WP 1.1).
- [ ] The change tags tests with requirement IDs or updates non-code evidence so traceability stays complete (from WP 1.2).
- [ ] The change covers negative paths as well as happy paths when authorization, concurrency, idempotency, or resilience behavior changes (from WP 1.7).
- [ ] The change respects the published coverage, mutation, and flakiness policy instead of moving thresholds informally (from WP 1.1).

## Documentation

- [ ] The change updates directly affected docs, ADRs, runbooks, or tool records instead of leaving drift for later.
- [ ] The change uses US English, sentence-case headings, and the repository house style.
- [ ] The change adds `(checked YYYY-MM-DD)` to facts that can go stale.
- [ ] The change keeps internal links within the approved repository targets.
- [ ] The change keeps examples concrete and avoids placeholders such as TODO or TBD.

## Migrations and data

- [ ] The change follows the expand-and-contract migration policy and makes destructive changes explicit (from WP 1.8).
- [ ] The change keeps application replicas from running migrations directly (from WP 1.8).
- [ ] The change uses synthetic seed or demo data and never introduces real personal or production data.
- [ ] The change explains rollback expectations for schema, data, and cache effects when it touches persistence (from WP 1.8).

## Supply chain and CI

- [ ] The change uses SHA-pinned GitHub Actions and digest-pinned images only.
- [ ] The change keeps workflow permissions minimal and compatible with the repository's read-only default token.
- [ ] The change does not execute pull request code from `pull_request_target` or similar privileged paths.
- [ ] The change respects the dependency license policy and the tool-only exception rules.
- [ ] The change records or updates evidence for scanners, SBOMs, attestations, or policy checks when those surfaces change (from WP2.4).

## Infrastructure and Kubernetes

- [ ] The change keeps ownership boundaries clear between Terraform, Argo CD, Kargo, and repository-managed manifests (from WP 3.1).
- [ ] The change preserves namespace, network, identity, and policy isolation assumptions from the threat model (from WP 3.2).
- [ ] The change avoids retired or rejected platform choices such as ingress-nginx, tfsec, or OpenBao for SQL Server credentials.
- [ ] The change documents profile, host-support, or resource-budget impact when platform requirements change (from WP 3.0).

## Contracts

- [ ] The change keeps the authored OpenAPI contract as the source of truth and aligns implementation with it (from WP 1.5).
- [ ] The change does not expose domain types directly through contracts (from WP 1.5).
- [ ] The change preserves HTTP semantics such as ETags, `If-Match`, idempotency, pagination, and Problem Details where relevant (from WP 1.10).
- [ ] The change calls out compatibility risk when it modifies externally visible contracts or client expectations (from WP 4.1).

## Privacy

- [ ] The change keeps the noreply commit-identity policy and does not introduce personal email addresses.
- [ ] The change does not reveal workstation-specific absolute paths, session-state paths, or local environment details that do not belong in the repository.
- [ ] The change keeps logs, screenshots, traces, and examples free of personal data and live secrets.
- [ ] The change does not suggest or normalize mirror-pushes, history rewriting, or settings changes outside the approved ADRs.

## Do not flag

- Do not flag formatting, lint, or link issues that the required linters already enforce unless the change bypasses those gates.
- Do not flag subjective style preferences that are not grounded in the plan, an ADR, or a documented repository rule.
- Do not flag unrelated pre-existing issues unless the current change makes them worse or depends on them.

## How to review a docs-only change in Phase 0

- [ ] Confirm that the text matches the plan, ADRs, and research records instead of inventing new policy.
- [ ] Confirm that planned items are labeled as planned with the correct work package.
- [ ] Confirm that links stay within the approved repository targets and that status, governance, and evidence docs stay aligned.
- [ ] Confirm that the current docs checks are enough evidence for the touched files and that no code or infrastructure claims slipped into Phase 0 by accident.
