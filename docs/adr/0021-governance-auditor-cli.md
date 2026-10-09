---
title: "ADR-0021: Build the governance auditors as one deterministic .NET command line"
description: "Run status, traceability, security, blast-radius, test-summary, and GitHub sync checks from one first-party .NET tool whose rules are data in the repository, because a pull request check must give the same answer on every machine."
status: accepted
date: 2026-10-08
decision-makers: ["@RostomOhannessian"]
supersedes: []
superseded-by: []
tools: [governance-auditor, system-commandline, yamldotnet, github-cli, github-actions, copilot-skills, nuget-license]
related-adrs: ["0004", "0006", "0007", "0013", "0018", "0020", "0022"]
evidence: []
---

# ADR-0021: Build the governance auditors as one deterministic .NET command line

## Context and problem statement

The plan makes two auditors part of every pull request from WP1.2: a security auditor and a blast-radius evaluator
([plan section 2.1](../plans/implementation-plan.md), row "All PRs run `/advanced-security-auditor` and `/blast-radius-evaluator`").
Plan sections 6 and 10.2 add a status command, a traceability command, a test summary, and a GitHub sync. The requirements
`REQ-SEC-001`, `REQ-SEC-007`, `REQ-QUA-003`, `REQ-CI-002`, `REQ-CI-004`, `REQ-GOV-002`, and `REQ-AI-002` depend on them.

A check that decides whether a pull request may merge has to give the same answer on a laptop, in a Dev Container, and in CI. It also
has to read text that an outsider controls: workflow files, commit messages, test results, and file names. Four facts shape the choice
(all checked 2026-10-08):

- `JsonSchema.Net` 9.4.0, the best-known .NET JSON Schema library, ships an end-user license agreement (`OSMFEULA.txt`) and asks
  organizations that make revenue to pay a monthly maintenance fee ([json-everything README](https://github.com/json-everything/json-everything)).
  That is not an OSI license, so [ADR-0004](0004-licensing-and-dependency-license-policy.md) does not allow it as a linked dependency.
- The other .NET schema libraries that were checked bring more than the repository needs. `NJsonSchema` 11.6.1 (MIT) depends on
  Newtonsoft.Json, Namotion.Reflection, and System.Text.Json. `LateApexEarlySpeed.Json.Schema` 4.2.0 (BSD-3-Clause) targets .NET
  Standard 2.1 and depends on `Microsoft.Extensions.Http` 3.1.0.
- `System.CommandLine` 2.0.12 is a stable MIT release with no dependencies on net8.0 and later, and `YamlDotNet` 18.1.0 is MIT with no
  dependencies on net10.0.
- `YamlDotNet` resolves a YAML alias to the node it names, so a nested alias can multiply a small file into a very large tree.

## Decision drivers

- A check gives one answer everywhere, and a rule can be shown to fail (REQ-QUA-003, ADR-0020).
- Every rule is data that a reviewer can read, and a change to it is a reviewed change (plan section 5.3 puts `governance/` under CODEOWNERS).
- Hostile input is the normal case: a pull request controls what the tool reads.
- The linked-dependency policy holds for the tool too, and the fewer packages the better.
- The same tool serves a person, CI, and the repository skills, so the skills never hold policy.

## Considered options

1. One first-party .NET command line, `governance`, in `tools/Governance.Auditor`, with its rules as policy files in
   `governance/policies/` (chosen).
2. PowerShell scripts for each check, with Pester tests.
3. Third-party tools only: gitleaks for secrets, zizmor and actionlint for workflows, and a custom action for the rest.
4. A GitHub Action from the Marketplace for status and traceability.

## Decision outcome

Chosen option: **one first-party .NET command line**, because it is the only option that gives every check the same engine, the same
tests, and the same policy files, and it keeps the dependency list to two MIT packages.

The design:

- **Commands.** `status validate` and `status render [--check]`, `trace [--check] [--all] [--phase N]`, `test-summary`, `security`,
  `blast-radius`, and `github-sync`. Exit code 0 means every check passed, 1 means a check failed or the arguments were wrong, and 2
  means the tool could not run, for example because a file is missing. A nonzero code always means "do not merge".
- **Rules are data.** `governance/policies/security-policy.yaml` holds the secret patterns, the workflow exceptions, the denied license
  prefixes, and the allowed commit emails. `governance/policies/blast-radius-map.yaml` holds the path-to-layer map. The tool also reads
  the license, forbidden-package, and coverage policy files from WP1.1, so there is one source for each rule.
- **Security rules (core).** `secret-patterns`, `workflow-permissions`, `action-pinning`, `workflow-secrets`, `checkout-credentials`,
  `pull-request-target`, `workflow-limits`, `license-policy`, and `commit-identity`, plus `workflow-parse`, which reports a workflow
  that cannot be read, because a rule that skips a file silently is worse than no rule. WP1.12 adds Dockerfile, infrastructure, and
  manifest rules.
- **Blast radius.** Each path maps to a layer, an area, jobs, documentation, reviews, and risk flags. Several rules may match one path
  and the results are combined. A path that no rule matches gets a strict fallback that runs every job.
- **Own validator for a JSON Schema subset.** The tool validates `status.yaml`, the requirements register, and the evidence register
  against their schemas with a small validator that implements the keywords those schemas use. It rejects a schema that uses any other
  keyword, so a schema can never silently check less than it says. The `JsonSchema.Net` license rules out the usual choice, and the
  other libraries add dependencies for features the repository does not use.
- **Hostile input.** The tool starts programs with an argument list and no shell, refuses XML with a DTD, refuses YAML anchors and
  aliases, gives every regular expression a timeout, accepts only plain Git reference characters, and never prints a secret or an email
  address. A finding names the rule, the file, and the line.
- **GitHub sync.** `github-sync` compares labels, milestones, and work-package issues with the repository and prints a diff. It never
  deletes anything, it changes only the labels it manages and adds a missing marker to an issue body, and it needs `--apply` to write.
  It talks to GitHub through the signed-in GitHub CLI, so the tool stores no token, and `--apply` refuses to run in GitHub Actions.
- **Workflow.** `governance.yml` runs on every pull request to `master` and `phase/**` with a read-only token, and publishes the blast
  radius as job outputs. Nothing consumes the outputs yet. Job selection arrives with the workflow architecture in WP2.2.
- **Skills.** `/advanced-security-auditor` and `/blast-radius-evaluator` run the CLI and explain the result. They have no write scope,
  and the tests run the CLI over a fixture and compare the output with a reviewed file.

### Consequences

- Good: the status check, the traceability check, and the two auditors share one engine, one set of tests, and the policy files that
  CODEOWNERS already protects.
- Good: the tool adds two packages, both MIT with no dependencies on net10.0, and the architecture tests now check the tool's lock file
  against the forbidden-package list.
- Bad: the tool is built from the pull request's own source, so a pull request that changes the tool is audited by the changed tool.
  Mitigation: the blast-radius map flags a change under `tools/Governance.Auditor/` for a close human read, the threat model records
  the residual risk, and the maintainer decides whether to add the path to CODEOWNERS. That is the maintainer's decision, and this
  work package does not change CODEOWNERS.
- Bad: the schema validator is code that the project maintains. Mitigation: it is small, it fails on unknown keywords, and the tests
  cover each keyword with a passing and a failing value.
- Bad: refusing YAML anchors means a workflow that uses them fails the `workflow-parse` rule. GitHub Actions has supported anchors and
  aliases since 2025-09-18 ([GitHub changelog](https://github.blog/changelog/2025-09-18-actions-yaml-anchors-and-non-public-workflow-templates/),
  checked 2026-10-08), so this is a deliberate restriction. It can be lifted by resolving aliases with a size limit if a workflow needs
  them.
- Neutral: a secret pattern is precise and limited, so it finds fewer things than gitleaks. It complements gitleaks and push
  protection, and does not replace them.

### Confirmation

- **WP1.2** delivers the tool, the policy files, the workflow, the skills, and the tests, and the tests run every rule against a
  conforming fixture and a violating fixture.
- The tests run the real repository through the same commands the workflow runs, so a repository that breaks a rule fails the unit
  tests as well as the workflow.
- **Owner decision pending.** Making `Governance auditors` a required check in the `master` and `phase/**` rulesets is a settings
  change, and the maintainer has not approved it yet. Until then, the check runs and reports but does not block.
- **WP1.12** adds the deeper rules and the `/test-plan-evaluator` skill, and **WP2.2** consumes the job outputs.

## Pros and cons of the options

### One first-party .NET command line

- Good: one engine, one test suite, and one set of policy files for every check.
- Good: the same code serves a person, CI, and the skills.
- Bad: the project maintains it, and it is audited by its own changes.

### PowerShell scripts with Pester tests

- Good: no build step, and PowerShell is already a prerequisite.
- Bad: parsing YAML and schemas in PowerShell needs modules from the PowerShell Gallery that are not covered by lock files or the
  license gate, and large scripts are harder to type-check and to test than .NET code.

### Third-party tools only

- Good: maintained by others, and gitleaks and zizmor are strong.
- Bad: no tool knows the repository's status file, requirement IDs, blast-radius map, or commit identity rule, so a custom step is
  needed anyway. Phase 2 adds zizmor and actionlint next to this tool and does not replace it.

### A Marketplace action

- Good: nothing to build.
- Bad: a third-party action holds the repository's token, which the supply-chain rules in plan section 9 are written to limit.

## Revisit when

- A permissively licensed JSON Schema library with no extra dependencies appears, which could replace the validator.
- `System.CommandLine` 3.0 is stable, or `YamlDotNet` changes its license or its alias behavior.
- The tool grows past what one maintainer can review, which would argue for splitting `Governance.Auditor` and `Documentation.Auditor`
  further, or for running the base branch's copy of the tool against a pull request.
- GitHub Actions workflows start to rely on YAML anchors.

## Further reading

- [JsonSchema.Net and the other json-everything libraries](https://github.com/json-everything/json-everything) (checked 2026-10-08)
- [System.CommandLine on NuGet](https://www.nuget.org/packages/System.CommandLine/2.0.12) (checked 2026-10-08)
- [YamlDotNet on NuGet](https://www.nuget.org/packages/YamlDotNet/18.1.0) (checked 2026-10-08)
- [ADR-0020: Control dependency intake](0020-dependency-intake-controls.md) for the license gate and the forbidden-package list that the
  security auditor reads
