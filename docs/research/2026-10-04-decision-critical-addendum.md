---
title: "Research record: decision-critical addendum (2026-10-04)"
description: "Facts checked directly against primary sources during the v2 plan review that changed or confirmed a decision."
type: research-record
date: 2026-10-04
status: verified
---

# Research record: decision-critical addendum

This record holds the facts that were checked first-hand, by reading the primary source or querying the GitHub API, and that
changed or confirmed a decision during the review that produced plan v2. The three agent-produced records next to it
([GitHub and supply chain](./2026-10-04-github-supply-chain-verification.md),
[Kubernetes platform](./2026-10-04-kubernetes-platform-verification.md), and
[.NET ecosystem](./2026-10-04-dotnet-ecosystem-verification.md)) are broader but were compiled by research agents.
Where this addendum disagrees with them, this addendum wins.

Every section names its source and the date it was read. Re-verify a section before relying on it after 180 days.

## 1. CodeQL CLI license terms

Source: [CodeQL CLI terms and conditions](https://raw.githubusercontent.com/github/codeql-cli-binaries/main/LICENSE.md), read 2026-10-04.

The terms define an "Open Source Codebase" as one "released under an OSI-approved License". They permit, "only with an Open Source Codebase",
analysis of that codebase and, when it is hosted and maintained on GitHub.com, generating CodeQL databases during automated analysis, CI, or CD.

The restrictions section does not authorize using the software "in connection with any codebase that is not an Open Source Codebase
(e.g., code in a private repo in GitHub)". The only exception is use under a paid customer license for GitHub Advanced Security.

What this means here:

- CodeQL may run only after the repository is public and carries its Apache-2.0 license (decisions: [ADR-0002](../adr/0002-repository-visibility-and-capability-strategy.md), [ADR-0013](../adr/0013-shift-left-security-toolchain.md)).
- The restriction concerns running the analysis at all, not only uploading results, so keeping a SARIF file as a private artifact is not a workaround.
- A learner's private copy of this repository must skip CodeQL and say so.

## 2. OpenBao database plugins

Sources, read 2026-10-04: the [`openbao/openbao`](https://github.com/openbao/openbao) repository tree and releases,
the [`openbao/openbao-plugins`](https://github.com/openbao/openbao-plugins) repository tree and releases, and the
[OpenBao database secrets engine documentation](https://openbao.org/docs/secrets/databases/).

- The latest OpenBao release is v2.7.1, published 2026-10-01.
- The built-in database plugins live under `internal/builtin/database`: `cassandra`, `influxdb`, `mysql` (plus a legacy MySQL plugin), `postgresql`, and `valkey`.
- The first-party external plugin collection (`openbao-plugins`, MPL-2.0) has one database plugin, `database/mongodb` (release `database-mongodb-v0.0.1`). Its other directories are auth methods, KMS seals, and secrets engines.
- A search of both repository trees for `mssql` and `sqlserver` found nothing, and the documentation URL `/docs/secrets/databases/mssql/` returns HTTP 404.

Conclusion: OpenBao 2.7.1 cannot issue dynamic SQL Server credentials. This is the deciding fact in [ADR-0008](../adr/0008-secrets-broker-vault-over-openbao.md).

## 3. HashiCorp Vault Community Edition

Sources, read 2026-10-04: the [`hashicorp/vault`](https://github.com/hashicorp/vault) repository tree, releases, and
[LICENSE](https://raw.githubusercontent.com/hashicorp/vault/main/LICENSE); the releases of
[`hashicorp/vault-secrets-operator`](https://github.com/hashicorp/vault-secrets-operator) and
[`hashicorp/vault-k8s`](https://github.com/hashicorp/vault-k8s).

- The latest Vault release is v2.1.1, published 2026-09-16.
- `plugins/database/` contains `cassandra`, `hana`, `influxdb`, `mongodb`, `mssql`, `mysql`, `postgresql`, and `redshift`.
- Vault Secrets Operator's latest release is v1.6.0 (2026-09-24). The `vault-k8s` agent injector's latest release is v1.7.6 (2026-08-07).
- The license is the Business Source License 1.1. The licensor is International Business Machines Corporation (IBM) and the licensed work is Vault 1.15.0 or later.
  GitHub's license detector reports it as "Other", not as an SPDX identifier.
- The additional use grant permits production use that does not offer the software to third parties on a hosted or embedded basis to compete with
  IBM's paid versions. It states that hosting or using the software for internal purposes within an organization is not a competing offering,
  and that a product not provided on a paid basis is not competitive.
- The excerpt read covered the parameters and the additional use grant. The change date and change license terms were not read.

Conclusion: Vault can issue dynamic SQL Server credentials, and this repository's use (a free tool, never redistributed or offered as a service) fits the
additional use grant as read. [ADR-0004](../adr/0004-licensing-and-dependency-license-policy.md) records the tool-only rule.

## 4. Kargo promotion steps

Sources, read 2026-10-04: the Kargo reference pages for [`git-open-pr`](https://docs.kargo.io/user-guide/reference-docs/promotion-steps/git-open-pr)
and [`git-merge-pr`](https://docs.kargo.io/user-guide/reference-docs/promotion-steps/git-merge-pr).

- `git-open-pr` opens a pull request from a source branch to a target branch. It normally follows `git-push` and precedes `git-wait-for-pr`.
  Supported providers are Azure DevOps, Bitbucket, Gitea, GitHub, and GitLab. It outputs the pull request number (`pr.id`) and URL (`pr.url`).
  Labels are supported on GitHub and GitLab.
- `git-merge-pr` merges an open pull request, synchronously only. It cannot place a pull request on a merge queue or wait for one.
  If branch protection requires a merge queue and the caller cannot bypass it, the merge attempt fails.
  With `wait: true` it reports a running status instead of failing while the pull request is not yet mergeable, and retries at `pollInterval`.
- Git steps use Kargo's repository credentials. SSH repository URLs are deprecated as of v1.10.0 and will be removed in v1.13.0, so use HTTPS URLs.

Conclusion: the dev and staging promotion flow (open pull request, wait for required checks, merge) and the prod flow (open pull request, wait for a human merge)
are expressible with documented steps. [ADR-0010](../adr/0010-promotion-engine-kargo.md) builds on this.

## 5. Keycloak and SQL Server

Source: [Keycloak server database guide](https://www.keycloak.org/server/db), read 2026-10-04.

The vendor table for database TLS lists MSSQL with a truststore in JKS or PKCS#12 format and no keystore, and states that mutual TLS is not
supported for MSSQL. The unified options are `db-tls-mode` (`disabled` or `verify-server`) and `db-tls-trust-store-file`.

Conclusion: Keycloak can use SQL Server with server-verified TLS but not with client certificates, so its database credential is a password,
managed as a Vault static role ([ADR-0008](../adr/0008-secrets-broker-vault-over-openbao.md)). The full list of supported database versions was not read.

## 6. Tempo deployment modes

Source: [Grafana Tempo deployment modes](https://grafana.com/docs/tempo/latest/set-up-for-tracing/setup-tempo/plan/deployment-modes/),
found through a cited web search on 2026-10-04 (the page was not read in full; confidence: medium).

In Tempo 3.x the Kafka-compatible queue is required for the microservices deployment. Monolithic (single-binary) mode runs without it.
The Kubernetes record's earlier statement that Tempo 3.x needs Kafka in every mode is withdrawn.

## 7. Repository and account facts at review time

Read through the GitHub API on 2026-10-04:

- The account is on the GitHub Free plan.
- The repository was private, with default branch `master`, no files, no pull requests, no issues, and no milestones.
- The rulesets API returned HTTP 403 with the message "Upgrade to GitHub Pro or make this repository public to enable this feature."
- The repository's original initial commit was empty and was authored with a personal email address. The address and the commit ID are deliberately not recorded here;
  [ADR-0006](../adr/0006-commit-identity-and-privacy.md) explains how identity is handled.

## 8. Corrections to the agent-produced records

These statements in the agent-produced records are wrong or not supported by the source they cite. The records are preserved verbatim, so the
corrections live here.

- **tfsec migration guide.** The GitHub record (item 5) says Aqua's migration guide recommends `trivy fs --scanners misconfig` and states that
  `AVD-*` rule IDs carry over unchanged. Read on 2026-10-04, the
  [guide](https://github.com/aquasecurity/tfsec/blob/master/tfsec-to-trivy-migration-guide.md) shows `trivy config <dir>` as the replacement
  for `tfsec <dir>`, covers variable files, report formats, and skipping paths, and says nothing about `trivy fs` or about rule identifiers.
  Whether and how tfsec rule IDs map to Trivy's is therefore unverified. WP3.1 verifies it by running both scanners over one fixture and
  publishing the mapping ([ADR-0014](../adr/0014-iac-scanning-trivy-as-tfsec-successor.md)).
- **Tempo and Kafka.** See section 6.
- **OpenBao and SQL Server.** The Kubernetes record (item 8) listed the MSSQL plugin as unverified; section 2 settles it.

## 9. How to extend this record

Add a numbered section with the claim, the primary source with a link, the date read, and what you did not read. Keep each section to facts and one conclusion
that names the ADR it affects. Do not paste secrets, tokens, or personal data.
