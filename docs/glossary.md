---
title: "Glossary"
description: "Plain-language definitions for the architecture, security, testing, and delivery terms used throughout this repository."
audience: [learners, contributors, maintainers]
type: reference
last-verified: 2026-10-04
owner: "@RostomOhannessian"
---

# Glossary

This glossary defines the terms the plans, ADRs, threat model, and learning content use most often. Definitions assume you know the syntax of the tools and languages involved, but not the concept behind them.

- **ADR (Architecture Decision Record):** A short document that records a significant technical or process decision, the options considered, and the consequences. Here: this repository keeps ADRs under [docs/adr](adr/README.md) and follows [ADR-0001](adr/0001-record-architecture-decisions.md).
- **API contract:** The formal description of what an API accepts and returns, including schemas, headers, errors, and versioning rules. Here: the plan commits to an authored OpenAPI 3.1 contract that later must match the served API.
- **Argo CD:** A GitOps controller that watches Git, compares desired state to cluster state, and reconciles differences. Here: it is the planned pull-based delivery engine in [ADR-0011](adr/0011-gitops-vs-traditional-cicd.md).
- **Attestation:** A signed statement about an artifact, such as who built it or what SBOM belongs to it. Here: Phase 2 plans provenance and SBOM attestations alongside signatures.
- **Blast radius:** The set of layers, environments, tests, and reviews that a change can affect. Here: the repository plans a blast-radius evaluator so CI can run the right checks for each pull request.
- **Bounded context:** A boundary inside which one model and vocabulary stay consistent. It keeps terms clear and prevents one subsystem's rules from leaking everywhere else. Here: the planned domain is the product catalog.
- **Canary release:** A rollout that sends a small share of traffic to a new version before full promotion. Here: Phase 4 plans canary steps and analysis-backed promotion in the GitOps flow.
- **Clean Architecture:** A layered architecture that keeps business rules independent from frameworks, databases, and delivery mechanisms. Here: the dependency rules live in the [implementation plan](plans/implementation-plan.md) and become executable tests in Phase 1.
- **Composition root:** The place where an application wires together its dependencies and runtime configuration. Here: the planned `Api` project is the composition root, not the business logic layer.
- **Consumer-driven contract:** A contract style where consumers state what they need and the provider proves it still honors that expectation. Here: Phase 4 plans consumer-driven contracts with Pact.
- **Contract-first:** Designing the API contract before implementing the server so behavior, names, and compatibility rules are explicit up front. Here: the repository chooses that approach in [ADR-0017](adr/0017-contract-first-api-design.md).
- **Conventional Commits:** A commit and pull request naming convention that uses structured prefixes such as `feat` or `docs` to describe intent. Here: Phase 1 plans a Conventional Commit pull-request-title check.
- **CQRS (Command Query Responsibility Segregation):** A pattern that separates write operations from read operations so each can have its own rules and pipeline. Here: the plan uses direct handler interfaces and decorators instead of a mediator library.
- **CycloneDX:** An SBOM format that describes software components, versions, and relationships for security and compliance work. Here: Phase 2 plans CycloneDX SBOMs next to SPDX output.
- **Dependency inversion:** A design rule that makes high-level policy depend on abstractions rather than low-level details. It is what lets infrastructure plug into application ports instead of the other way around.
- **Dev Container:** A reproducible development environment defined as code and opened by compatible tools such as VS Code or Codespaces. Here: the Dev Container is the primary developer environment planned for Phase 1.
- **Diataxis:** A documentation framework that separates tutorials, how-to guides, reference, and explanation because readers come with different needs. Here: [ADR-0005](adr/0005-documentation-system.md) makes Diataxis the documentation structure.
- **Digest pinning:** Referring to a container image by immutable content digest instead of a mutable tag. Here: the plan requires digest pinning for images and later enforces it with policy.
- **Domain event:** A record that something meaningful happened inside the domain model, such as a product being created or discontinued. Here: the catalog domain publishes planned events and later persists them through an outbox.
- **Dynamic secret:** A short-lived credential minted on demand instead of stored as a long-lived shared password. Here: the plan uses Vault-issued runtime credentials for SQL Server and, if feasible, Valkey.
- **Envoy Gateway:** A Kubernetes gateway implementation built around Envoy and the Gateway API. Here: the project plans it as the single entry point in [ADR-0015](adr/0015-gateway-api-with-envoy-gateway.md).
- **ETag:** A version token on an HTTP resource that lets clients make conditional reads and writes. Here: the API plan uses strong ETags with `If-Match` and `If-None-Match`.
- **Freight:** Kargo's term for a specific artifact version, usually an image digest, that is available to promote through environments. Here: the Phase 4 plan tracks one verified digest as Freight from dev to prod.
- **Gateway API:** Kubernetes APIs for ingress and traffic management that replace older, less expressive ingress patterns. Here: the project plans Gateway API as its ingress model, fronted by Envoy Gateway.
- **GitOps:** Operating a system by storing desired state in Git and reconciling live systems back to that state. Here: Git is planned to stay authoritative for deployments and rollback evidence.
- **Idempotency key:** A client-supplied key that makes retried write requests safe by letting the server recognize that the same command already ran. Here: the API plan requires `Idempotency-Key` on creates.
- **Kargo:** A GitOps promotion controller that moves already-built artifacts through environments by opening and managing promotion pull requests. Here: [ADR-0010](adr/0010-promotion-engine-kargo.md) proposes Kargo for promotion.
- **Keycloak:** An identity and access management server that issues tokens, manages realms, and maps roles and scopes. Here: the plan uses Keycloak for both application and platform sign-in.
- **kind:** A local Kubernetes distribution that runs cluster nodes as Docker containers. Here: the platform phases use kind to make the teaching environment reproducible on one machine.
- **Kyverno:** A Kubernetes policy engine that validates, mutates, and verifies resources with Kubernetes-native policy definitions. Here: the plan uses Kyverno for admission policy and image verification.
- **Least privilege:** Giving each person, workload, or tool only the permissions it needs and no more. Here: it shapes GitHub tokens, Kubernetes RBAC, Vault policies, and GitHub App permissions throughout the plan.
- **NetworkPolicy:** A Kubernetes resource that limits which pods can talk to which other pods and services. Here: the platform design uses default deny plus explicit allows between the public, private, data, and platform tiers.
- **OIDC (OpenID Connect):** An identity layer over OAuth 2.0 that lets systems validate who a token represents and what issued it. Here: the repository plans OIDC both for GitHub-to-Vault federation and for API token validation.
- **Outbox pattern:** A way to store domain-side effects as messages in the same transaction as the main write, then deliver them asynchronously later. Here: [ADR-0012](adr/0012-domain-events-outbox-vs-direct-orchestration.md) proposes a transactional outbox.
- **Pact:** A contract-testing toolset for consumer-driven contracts. It stores expectations from consumers and later verifies them against the provider. Here: Phase 4 plans PactNet-based consumer and provider verification.
- **Pod Security Admission:** Kubernetes admission control that enforces baseline or restricted pod-security profiles at the namespace level. Here: Phase 3 plans Pod Security Admission as part of the platform baseline.
- **Policy-based authorization:** Authorization that checks named policies, claims, roles, or scopes instead of scattering one-off checks through handlers and endpoints. Here: the API plan maps scopes to policies and tests denied paths explicitly.
- **Promotion:** The act of moving the same already-built artifact into a later environment after checks pass. Here: promotion is planned to happen through Kargo and Git-authored environment changes, not rebuilds.
- **Provenance:** Build history that says what source, workflow, and identity produced an artifact. Provenance answers "where did this binary or image come from?" Here: Phase 2 plans signed provenance tied to the published digest.
- **Repository pattern:** An abstraction that hides persistence details behind methods that work in domain terms. Here: repositories are planned as application ports implemented by infrastructure.
- **Resource profile:** A named environment size such as `api`, `lite`, `full`, or `ci` that sets expectations for CPU, memory, and which services run. Here: [ADR-0019](adr/0019-resource-profiles-and-host-support.md) proposes the profile model.
- **Rollout:** A deployment strategy resource, or the process it represents, that controls how a new version replaces an old one. Here: the Phase 4 plan converts the application deployment into an Argo Rollouts `Rollout`.
- **SBOM (Software Bill of Materials):** A machine-readable inventory of the components inside a build artifact. Here: the repository plans SBOMs in both SPDX and CycloneDX formats.
- **Shift-left security:** Moving security checks earlier in development so problems are caught during authoring and pull-request review, not after release. Here: [ADR-0013](adr/0013-shift-left-security-toolchain.md) proposes the layered toolchain.
- **Sigstore and Cosign:** Sigstore is the public trust system behind keyless signing, certificates, and transparency logs; Cosign is the signing and verification tool most people use with it. Here: Phase 2 plans keyless Cosign signatures and verification.
- **SLA:** A formal service contract between provider and customer. This repository does not plan SLAs; it teaches SLOs as internal engineering targets instead.
- **SLO (Service-Level Objective):** A measurable reliability target, such as latency or error-rate thresholds, used to decide whether the system is healthy enough. Here: the plan defines teaching SLOs and later uses them for canary analysis.
- **SLSA:** A supply-chain security framework for describing build integrity levels and provenance expectations. Here: the repository plans SLSA-style provenance and verification for published images.
- **SPDX:** A standard format for describing software packages, licenses, and SBOM content. Here: the repository plans SPDX output as one of its SBOM artifacts.
- **Testcontainers:** A testing approach and library that starts real dependency containers for automated tests. Here: Phase 1 uses Testcontainers for SQL Server, Valkey, and Keycloak-backed testing.
- **Threat model:** A structured view of what must be protected, who can attack it, where the trust boundaries are, and which controls reduce which risks. Here: the Phase 0 threat model is in [docs/security/threat-model.md](security/threat-model.md).
- **Trust boundary:** A point where data, identity, or control crosses from one trust level into another. Threat modeling is easier when these boundaries are explicit and named.
- **Unit of Work:** A pattern that groups related data changes into one transactional save. Here: the plan uses `IUnitOfWork` around one EF Core `DbContext` transaction.
- **Valkey:** An open-source in-memory data store compatible with the Redis ecosystem. Here: the repository plans Valkey for distributed caching and cache-backplane pub/sub.
- **Vault:** HashiCorp's secrets-management product for brokering credentials, policies, and auth methods. Here: [ADR-0008](adr/0008-secrets-broker-vault-over-openbao.md) proposes Vault over OpenBao.
- **VEX (Vulnerability Exploitability eXchange):** A machine-readable statement that explains whether a known vulnerability is actually exploitable in a specific artifact. Here: Phase 2 plans time-bounded OpenVEX exceptions for rare justified cases.
- **Work package:** The project's smallest planned delivery unit, with a scope, evidence, and exit criteria. Here: every phase is split into numbered work packages such as WP1.8 or WP4.6.
- **Workload identity:** An identity assigned to running automation or a pod so it can authenticate as itself instead of sharing a static secret. Here: the plan uses workload identity through OIDC federation and Kubernetes service accounts.
- **Zero trust:** A security model that assumes no network location is automatically trusted and every request, workload, and connection must prove itself. Here: Phase 3 builds the zero-trust platform boundary by boundary.
