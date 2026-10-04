---
title: "Research record: external AI skill review (2026-10-04)"
description: "Security, license, and fit review of twelve external AI agent skills considered for this public repository."
type: research-record
date: 2026-10-04
status: verified
---

# Research record: external AI skill review (2026-10-04)

## Purpose and scope

This record reviews twelve external AI agent skills for possible use with this repository. It covers security posture, license fit, repository fit, and whether each skill should be rejected, deferred, trialed in isolation, or treated as an adoption candidate.

The Agent Finder relevance score is a search-relevance signal, not a trust, safety, or quality rating. All upstream repository facts, license signals, commit SHAs, directory contents, and GitHub CLI behavior in this record are point-in-time observations (checked 2026-10-04).

## Method and limits

- I treated every `SKILL.md`, script, reference file, asset, and linked documentation page as untrusted data and did not execute any upstream script.
- For each candidate, I used `gh api` to inspect repository facts, the repository license endpoint, the most recent commit touching the skill directory, the recursive file tree for that commit, and the raw contents of the skill directory files.
- I also fetched GitHub's documentation for adding skills and the `gh skill` section on managing skills with GitHub CLI at [Adding agent skills for GitHub Copilot](https://docs.github.com/en/copilot/how-tos/copilot-on-github/customize-copilot/customize-cloud-agent/add-skills).
- This review does not measure runtime quality. It evaluates observable instructions, declared tool permissions, embedded scripts, license signals, and fit against this repository's planned skills and delivery phases.

## Summary table

| Candidate | Publisher | License | Scripts present | Pinned commit | Recommendation |
| --- | --- | --- | --- | --- | --- |
| Wiki Architect | microsoft/skills | Repo MIT; front matter MIT | No | `e1f9cce11758` | `defer` |
| Find Untested Sources | dotnet/skills | Repo MIT; front matter MIT | Yes: `.cs`, `.py` | `1eb71366fe1b` | `trial-in-isolation` |
| Code Testing Agent | dotnet/skills | Repo MIT; front matter MIT | No | `a824bbb8a24e` | `defer` |
| Coverage Analysis | dotnet/skills | Repo MIT; front matter MIT | Yes: `.ps1` | `e8ed8473d94a` | `trial-in-isolation` |
| Test Tagging | dotnet/skills | Repo MIT; front matter MIT | No | `a824bbb8a24e` | `defer` |
| Create Architectural Decision Record | github/awesome-copilot | Repo MIT; no front matter license | No | `caab1f623bb6` | `reject` |
| GitHub Actions Hardening | github/awesome-copilot | Repo MIT; no front matter license | No | `df566343b3a6` | `adopt-candidate` |
| Gha Security Review | getsentry/skills | Repo Apache-2.0; no front matter license | No | `b11ee5163295` | `trial-in-isolation` |
| Security Review | getsentry/skills | Repo Apache-2.0; skill-local CC-BY-SA notice | No | `3482d8dd3531` | `reject` |
| Agentic Actions Auditor | trailofbits/skills | Repo CC-BY-SA-4.0; no front matter license | No | `123037ec8aed` | `trial-in-isolation` |
| Differential Review | trailofbits/skills | Repo CC-BY-SA-4.0; no front matter license | No | `123037ec8aed` | `reject` |
| Security Best Practices | openai/skills | Repo license endpoint not verified; skill-local Apache-2.0 file | No | `5c8f1e26803b` | `reject` |

### 1. Wiki Architect

- Source: [Wiki Architect](https://github.com/microsoft/skills/blob/main/.github/plugins/deep-wiki/skills/wiki-architect/SKILL.md)
- Publisher and repository facts: Microsoft, repository `microsoft/skills`, not archived, repository license MIT, front matter license MIT (checked 2026-10-04).
- Pinned commit: `e1f9cce11758d305e6c77683fe34ccc394586291` from 2026-04-20.
- Directory contents: 1 file, about 4.0 KiB total. The directory contains only `SKILL.md`. No scripts or executables are present.
- Declared tools or permissions: none in front matter.
- Neutral summary: This skill tells an agent to inspect a repository and produce a structured documentation map, onboarding path, and wiki-style information architecture. It is aimed at high-level codebase understanding rather than implementation work. It appears to focus on organizing repository knowledge into navigable sections for humans.
- Network and data handling: no embedded scripts, package installs, or remote-service instructions were found in the directory. The main data handling is repository reading.
- Prompt-injection surface: low to moderate. It reads untrusted repository text while forming documentation, but it does not declare preapproved shell or write tools in front matter.
- Overlap with planned skills: partial overlap with `documentation-auditor` and `tool-doc-author`. It does not match the repository's thin-wrapper approach and could drift into policy or information-architecture choices that belong in repo-owned docs.
- Fit: possible support for Phase 0 documentation seeding or Phase 4.7 documentation refreshes, especially README and docs-index structure.
- Recommendation: `defer` — if revisited, install by reference at user scope, pinned to the reviewed commit, and use only for exploratory documentation mapping. Do not vendor it into the repository, and rewrite any output to match the repository's house style and allowed-link rules.

### 2. Find Untested Sources

- Source: [Find Untested Sources](https://github.com/dotnet/skills/blob/main/plugins/dotnet-test/skills/find-untested-sources/SKILL.md)
- Publisher and repository facts: .NET, repository `dotnet/skills`, not archived, repository license MIT, front matter license MIT (checked 2026-10-04).
- Pinned commit: `1eb71366fe1bceac2bf1587924a3003b3cf6f619` from 2026-09-08.
- Directory contents: 3 files, about 55.7 KiB total. It contains `SKILL.md`, a Roslyn-based C# script, and a Python tree-sitter analyzer. Executable content exists through `Find-UntestedSources.cs` run with `dotnet run` and `find_untested_sources.py`.
- Declared tools or permissions: none in front matter.
- Neutral summary: This skill tells an agent to statically pair source files with likely test files before generating tests. It prefers a Roslyn analyzer for .NET-only repositories and offers a polyglot tree-sitter analyzer for mixed-language repositories. The output is meant to produce a deterministic worklist of apparently unpaired files.
- Network and data handling: the Roslyn path depends on first-run NuGet restore for `Microsoft.CodeAnalysis.CSharp`, and the Python path explicitly depends on `tree-sitter-language-pack` installed with `pip`. The analyzers parse repository files as data and emit JSON to stdout. No external service exfiltration instructions were found.
- Prompt-injection surface: low to moderate. The analyzers read repository source and tests as data instead of executing them, but using the skill still requires shell execution of external scripts.
- Overlap with planned skills: complementary to `test-plan-evaluator` rather than a direct duplicate. It does not encode repository policy, but it does add external execution paths outside the repository's deterministic tool wrappers.
- Fit: good fit for Phase 1 test planning, especially WP1.8, WP1.10, and WP1.12 when deciding where new tests are missing in the .NET API.
- Recommendation: `trial-in-isolation` — install by reference at user scope, pinned to the reviewed commit, and use only the Roslyn path for this .NET-focused repository. Do not vendor it, do not rely on the Python fallback, and do not allow it to define repository coverage policy.

### 3. Code Testing Agent

- Source: [Code Testing Agent](https://github.com/dotnet/skills/blob/main/plugins/dotnet-test/skills/code-testing-agent/SKILL.md)
- Publisher and repository facts: .NET, repository `dotnet/skills`, not archived, repository license MIT, front matter license MIT (checked 2026-10-04).
- Pinned commit: `a824bbb8a24ec295e1e581a8c23cd3bcdab128c3` from 2026-09-25.
- Directory contents: 2 files, about 30.4 KiB total. It contains `SKILL.md` and a unit-test-generation prompt file. No standalone scripts or executables are present.
- Declared tools or permissions: none in front matter.
- Neutral summary: This skill tells an agent to discover existing testing conventions and then generate concise, behavior-focused unit tests. Its bundled prompt emphasizes parameterization, mutation-aware assertions, and running tests after writing them. It is broad by design and can scale from a single helper to large test-generation tasks.
- Network and data handling: no explicit package-install or download steps were found in the directory. The instructions do assume code reads, file writes, and test execution inside the repository.
- Prompt-injection surface: moderate. It reads source code and neighboring tests and then writes new tests, but it does not preapprove shell or write tools in front matter.
- Overlap with planned skills: partial overlap with `test-plan-evaluator`, but it is broader and more generative than the repository's planned thin-wrapper model. It would influence how tests are authored, not just how they are evaluated.
- Fit: possible support for Phase 1 unit, application, and infrastructure tests after conventions are stable.
- Recommendation: `defer` — if revisited, install by reference at user scope, pinned to the reviewed commit, after the repository's testing conventions and quality gates are already fixed. Do not vendor it, and do not let it become the source of test policy.

### 4. Coverage Analysis

- Source: [Coverage Analysis](https://github.com/dotnet/skills/blob/main/plugins/dotnet-test/skills/coverage-analysis/SKILL.md)
- Publisher and repository facts: .NET, repository `dotnet/skills`, not archived, repository license MIT, front matter license MIT (checked 2026-10-04).
- Pinned commit: `e8ed8473d94ac2f8d15f7663430852faa9c11219` from 2026-09-08.
- Directory contents: 7 files, about 44.2 KiB total. It includes `SKILL.md`, four markdown references, and two PowerShell scripts for method-level coverage extraction and CRAP scoring. Executable content exists through the PowerShell scripts.
- Declared tools or permissions: none in front matter.
- Neutral summary: This skill interprets Cobertura coverage evidence, reconciles coverage arithmetic, and optionally ranks CRAP hotspots. It distinguishes between directly answering from an existing report and collecting new coverage through another skill. The bundled scripts parse Cobertura XML to compute method coverage and complexity-weighted risk.
- Network and data handling: the normal interpretation path is local and data-only, but the optional HTML-report path tells the agent to install `dotnet-reportgenerator-globaltool` if `reportgenerator` is not already present. It also relies on PowerShell execution and may delegate test collection to another skill.
- Prompt-injection surface: low to moderate. It reads XML coverage artifacts and repository files as data, but it does carry executable scripts and an optional install path.
- Overlap with planned skills: complementary to `test-plan-evaluator`. It analyzes evidence rather than defining repository policy, but it should not become the owner of required thresholds or gate logic.
- Fit: strong fit for Phase 1 quality gates and Phase 2 CI reporting, especially around Microsoft Code Coverage, Cobertura exports, and CRAP-style hotspot review.
- Recommendation: `trial-in-isolation` — install by reference at user scope, pinned to the reviewed commit, and limit use to existing Cobertura interpretation plus the bundled parse scripts. Do not vendor it, and do not use the optional ReportGenerator install path inside repository automation.

### 5. Test Tagging

- Source: [Test Tagging](https://github.com/dotnet/skills/blob/main/plugins/dotnet-test/skills/test-tagging/SKILL.md)
- Publisher and repository facts: .NET, repository `dotnet/skills`, not archived, repository license MIT, front matter license MIT (checked 2026-10-04).
- Pinned commit: `a824bbb8a24ec295e1e581a8c23cd3bcdab128c3` from 2026-09-25.
- Directory contents: 1 file, about 21.5 KiB total. The directory contains only `SKILL.md`. No scripts or executables are present.
- Declared tools or permissions: none in front matter.
- Neutral summary: This skill classifies existing tests into a fixed trait taxonomy and can either report the distribution or edit tests to add standardized tags. It covers multiple frameworks and defaults to report-only behavior when the request is ambiguous. The taxonomy includes functional, operational, and security-oriented test traits.
- Network and data handling: no bundled scripts, downloads, or package installs were found. The main side effect is source editing when the caller explicitly asks it to apply tags.
- Prompt-injection surface: moderate. It reads and may rewrite test files, but it does not preapprove shell or write tools in front matter.
- Overlap with planned skills: partial overlap with `test-plan-evaluator`, especially around test taxonomy and evidence reporting. It does not fit the repository's current thin-wrapper policy model because it can directly rewrite test sources.
- Fit: possible later fit for requirements traceability and test-inventory work after the repository's test taxonomy is stable.
- Recommendation: `defer` — if revisited, install by reference at user scope, pinned to the reviewed commit, and start in report-only mode. Do not vendor it, and do not allow auto-edit use until the repository's own tagging scheme is finalized.

### 6. Create Architectural Decision Record

- Source: [Create Architectural Decision Record](https://github.com/github/awesome-copilot/blob/main/skills/create-architectural-decision-record/SKILL.md)
- Publisher and repository facts: GitHub, repository `github/awesome-copilot`, not archived, repository license MIT, no front matter license field in the skill (checked 2026-10-04).
- Pinned commit: `caab1f623bb68a330f294a11279597d7ae7be737` from 2026-02-24.
- Directory contents: 1 file, about 3.0 KiB total. The directory contains only `SKILL.md`. No scripts or executables are present.
- Declared tools or permissions: none in front matter.
- Neutral summary: This skill prompts an agent to gather decision context and emit an ADR document from structured inputs. It is a generic ADR generator aimed at formatting and completeness. It does not appear to model repository-specific ADR state, evidence rules, or acceptance workflow.
- Network and data handling: no scripts, downloads, or credential handling were found. The main side effect is writing an ADR file.
- Prompt-injection surface: low to moderate. It mainly uses conversation inputs, but it writes repository documentation without any repository-specific guardrails.
- Overlap with planned skills: direct overlap with `adr-assistant`. It also conflicts with this repository's ADR rules, status transitions, naming, and evidence requirements.
- Fit: only a superficial fit for Phase 0 ADR authoring.
- Recommendation: `reject` — do not install or vendor it. If any ideas are reused later, rewrite them locally inside the planned `adr-assistant` instead of importing this skill.

### 7. GitHub Actions Hardening

- Source: [GitHub Actions Hardening](https://github.com/github/awesome-copilot/blob/main/skills/github-actions-hardening/SKILL.md)
- Publisher and repository facts: GitHub, repository `github/awesome-copilot`, not archived, repository license MIT, no front matter license field in the skill (checked 2026-10-04).
- Pinned commit: `df566343b3a685e821f8ebe8ea70a2a8eb8b857b` from 2026-06-16.
- Directory contents: 6 files, about 23.3 KiB total. It contains `SKILL.md` plus five markdown reference files. No scripts or executables are present.
- Declared tools or permissions: none in front matter.
- Neutral summary: This skill reviews GitHub Actions workflows against common workflow-specific trust-boundary failures such as untrusted interpolation, privileged triggers, mutable action references, over-scoped tokens, and unsafe secret handling. It organizes the review around a stepwise workflow and bundles focused reference notes. It is advisory rather than executable and does not ship helper code.
- Network and data handling: no bundled scripts, downloads, installs, or credential reads were found. The skill focuses on reading workflow YAML and related documentation already present in a repository.
- Prompt-injection surface: moderate. It reads untrusted workflow text and examples, but it does not preapprove shell or write tools in front matter and does not instruct the agent to execute fetched content.
- Overlap with planned skills: partial overlap with `advanced-security-auditor`, but this skill is narrower and can act as reviewer guidance rather than policy ownership. It should not replace deterministic CI checks or repository-specific enforcement logic.
- Fit: strong fit for WP2.2, WP2.3, and WP3.9, where the plan already depends on hardened GitHub Actions, least-privilege tokens, SHA pinning, and secret-safe workflow design.
- Recommendation: `adopt-candidate` — install by reference, pinned to the reviewed commit, and use it only as human review guidance. Do not vendor it unless a later governance decision requires local edits, and do not let it own normative repository policy that should stay in deterministic tools and repo-owned documentation.

### 8. Gha Security Review

- Source: [Gha Security Review](https://github.com/getsentry/skills/blob/main/skills/gha-security-review/SKILL.md)
- Publisher and repository facts: Sentry, repository `getsentry/skills`, not archived, repository license Apache-2.0, no front matter license field in the skill (checked 2026-10-04).
- Pinned commit: `b11ee5163295996fa6449c4842749811c82ac77c` from 2026-09-29.
- Directory contents: 10 files, about 74.5 KiB total. It contains `SKILL.md` plus nine markdown references. No scripts or executables are present.
- Declared tools or permissions: `allowed-tools: Read Grep Glob Bash Task`.
- Neutral summary: This skill performs exploit-focused GitHub Actions review and asks the agent to report only externally exploitable workflow findings with concrete attack paths. It classifies triggers, maps known workflow abuse classes, and requires structured attack narratives for high-confidence findings. The supporting reference set is broad and tightly focused on Actions exploitation patterns.
- Network and data handling: no bundled executable files were found. The skill expects repository reads and allows Bash and Task use, but I did not see explicit package-install instructions in the reviewed directory.
- Prompt-injection surface: high. It asks the agent to inspect untrusted workflow YAML and related repository files while holding preapproved Bash and Task capability, even though the skill itself frames the work as read-and-analyze.
- Overlap with planned skills: substantial overlap with `advanced-security-auditor`. It is more exploit-narrative-oriented than policy-oriented, which makes it useful for manual review but a poor owner of repo policy logic.
- Fit: strong fit for secure-CI review in Phase 2 and for manual spot checks of privileged workflows in later phases.
- Recommendation: `trial-in-isolation` — install by reference at user scope, pinned to the reviewed commit, and use only for manual workflow reviews. Do not vendor it, and require a fresh preview before updates because its preapproved Bash and Task surface raises the trust threshold.

### 9. Security Review

- Source: [Security Review](https://github.com/getsentry/skills/blob/main/skills/security-review/SKILL.md)
- Publisher and repository facts: Sentry, repository `getsentry/skills`, not archived, repository license Apache-2.0, front matter license points to a skill-local `LICENSE` file that applies CC-BY-SA attribution rules to derived OWASP material (checked 2026-10-04).
- Pinned commit: `3482d8dd35315adf34607f1c12bf793e14890bfe` from 2026-09-28.
- Directory contents: 22 files, about 213.2 KiB total. It contains `SKILL.md`, a skill-local license notice, language guides, infrastructure guides, and many markdown references. No scripts or executables are present.
- Declared tools or permissions: `allowed-tools: Read Grep Glob Bash Task`.
- Neutral summary: This skill performs general security code review and asks the agent to research the whole codebase before reporting only high-confidence vulnerabilities. It is organized around common vulnerability classes, language-specific guidance, and infrastructure review paths. It is meant to be a broad security-review framework rather than a narrow workflow skill.
- Network and data handling: no bundled scripts were found, but the skill expects broad repository exploration and allows Bash and Task use. The reviewed directory did not include several files that the skill says to load, including some language and infrastructure guides, so some of its intended flows are incomplete.
- Prompt-injection surface: high. It directs codebase-wide research while holding preapproved Bash and Task capability, and it is broad enough to ingest untrusted diffs, templates, and configuration files.
- Overlap with planned skills: direct overlap with `advanced-security-auditor` and some overlap with `blast-radius-evaluator`. It is too broad to preserve the repository's planned separation between deterministic policy checks and thin skill wrappers.
- Fit: limited fit for this .NET repository because the reviewed directory lacks C# guidance and some referenced infrastructure guidance, even though the project depends heavily on .NET, Kubernetes, and Terraform.
- Recommendation: `reject` — do not install or vendor it. The combination of share-alike license baggage, missing referenced materials, and broad overlap makes it a poor fit for this repository.

### 10. Agentic Actions Auditor

- Source: [Agentic Actions Auditor](https://github.com/trailofbits/skills/blob/main/plugins/agentic-actions-auditor/skills/agentic-actions-auditor/SKILL.md)
- Publisher and repository facts: Trail of Bits, repository `trailofbits/skills`, not archived, repository license CC-BY-SA-4.0, no front matter license field in the skill (checked 2026-10-04).
- Pinned commit: `123037ec8aed26f0d86327cc39137ee5043e5deb` from 2026-09-16.
- Directory contents: 15 files, about 97.4 KiB total. It contains `SKILL.md`, an agent metadata YAML file, an SVG asset, and ten markdown references. No scripts or binaries are present.
- Declared tools or permissions: `allowed-tools: Read Grep Glob Bash`.
- Neutral summary: This skill audits GitHub Actions workflows that invoke AI coding agents and traces whether attacker-controlled inputs can reach those agents. It includes workflow-discovery guidance, cross-file resolution rules for composite actions and reusable workflows, and a catalog of AI-specific attack vectors. The references are specialized around agent prompts, sandbox settings, and trigger-driven exposure.
- Network and data handling: the skill includes explicit remote-analysis instructions that use `gh api` to list and fetch workflow files from GitHub. It also includes a safety section that says fetched YAML must be treated as data, not executed.
- Prompt-injection surface: high. It deliberately reads attacker-controlled workflow text and agent configuration while holding preapproved Bash capability, although the skill does include good safety guidance against executing fetched content.
- Overlap with planned skills: partial overlap with `advanced-security-auditor`, but it is much narrower and only becomes relevant if this repository later adopts AI-agent steps inside GitHub Actions. It should not own repo policy.
- Fit: limited near-term fit. It is relevant only if later phases add agentic workflow steps for CI, review, or automation.
- Recommendation: `trial-in-isolation` — install by reference at user scope, pinned to the reviewed commit, only if the repository later adds AI-agent workflow steps. Do not vendor it because of the repository's CC-BY-SA license and the repository's current no-duplication policy.

### 11. Differential Review

- Source: [Differential Review](https://github.com/trailofbits/skills/blob/main/plugins/differential-review/skills/differential-review/SKILL.md)
- Publisher and repository facts: Trail of Bits, repository `trailofbits/skills`, not archived, repository license CC-BY-SA-4.0, no front matter license field in the skill (checked 2026-10-04).
- Pinned commit: `123037ec8aed26f0d86327cc39137ee5043e5deb` from 2026-09-16.
- Directory contents: 7 files, about 35.7 KiB total. It contains `SKILL.md`, three markdown references, an agent metadata YAML file, and an SVG asset. No scripts or binaries are present.
- Declared tools or permissions: `allowed-tools: Read Write Grep Glob Bash`.
- Neutral summary: This skill performs security-focused differential review of code changes, including git-history analysis, blast-radius estimation, test-gap checks, adversarial modeling, and a written report. It is process-heavy and assumes a multi-phase review workflow. The skill also expects a report artifact to be created for each review.
- Network and data handling: no bundled scripts were found, but the methodology tells the agent to use git-history commands extensively and to generate markdown report files. The reviewed directory also refers to an `adversarial-modeler` agent and to other supporting skills that were not present in the skill directory.
- Prompt-injection surface: high. It combines read, write, and Bash capability while ingesting diffs, commit history, and broader repository context.
- Overlap with planned skills: direct overlap with `blast-radius-evaluator` and significant overlap with `advanced-security-auditor`. Its report-writing behavior and wide operational scope cut against the repository's thin-wrapper goal.
- Fit: some conceptual fit for PR review work, but not enough to justify the overlap and license cost.
- Recommendation: `reject` — do not install or vendor it. If the repository later needs any of its ideas, implement them inside repo-owned skills instead of importing this broader skill.

### 12. Security Best Practices

- Source: [Security Best Practices](https://github.com/openai/skills/blob/main/skills/.curated/security-best-practices/SKILL.md)
- Publisher and repository facts: OpenAI, repository `openai/skills`, not archived, repository license endpoint not verified, no front matter license field, and the skill directory contains `LICENSE.txt` with Apache License 2.0 text (checked 2026-10-04).
- Pinned commit: `5c8f1e26803bcfaffeceef1e7accbcf7e388417a` from 2026-02-02.
- Directory contents: 13 files, about 402.1 KiB total. It contains `SKILL.md`, an agent metadata YAML file, a license file, and nine large markdown references. No scripts or binaries are present.
- Declared tools or permissions: none in front matter.
- Neutral summary: This skill is a broad best-practices guide for supported language and framework combinations and can either steer secure-by-default coding or produce a security report. It emphasizes loading reference files by language or framework and allows fallback to online searching when local guidance is not enough. The reference set is large but limited to Python, JavaScript or TypeScript, and Go combinations.
- Network and data handling: the skill explicitly allows online searching when local guidance is missing. It also directs the agent to write a markdown security report file by default when asked for a report.
- Prompt-injection surface: moderate to high. It may mix repository reads with web content and can produce or update report files, even though it does not preapprove shell access in front matter.
- Overlap with planned skills: broad overlap with `advanced-security-auditor`. It also does not support the repository's primary .NET stack, so it would add generic advice without matching the repository's strongest needs.
- Fit: weak fit for the planned .NET 10 API. It is only indirectly relevant if later phases add a substantial JavaScript or Go surface beyond the current plan.
- Recommendation: `reject` — do not install or vendor it. The language mismatch and online-search fallback make it a poor fit for this repository's main workload.

## Cross-cutting findings

- Relevance scores were useful for discovery, but they were not reliable trust signals. Several high-score skills still had license, scope, or safety problems.
- The cleanest license path came from MIT-licensed repositories and skills. Apache-2.0 repositories were acceptable in principle, but one candidate mixed Apache-2.0 at the repo level with CC-BY-SA material inside the skill. CC-BY-SA repositories are workable for reading, but they are poor candidates for vendoring into a public repository unless the maintainer explicitly accepts share-alike obligations.
- Scripted skills deserve extra caution even when the scripts are parse-only. Candidate 2 depends on NuGet or `pip` prerequisites, and candidate 4 contains PowerShell plus an optional tool-install path for report generation.
- Preapproved tool permissions materially change the trust bar. Candidates 8, 9, 10, and 11 preapprove Bash or Task, and candidate 11 also preapproves Write.
- Two reviewed skills had evidence of incomplete packaging. Candidate 9 references language and infrastructure guides that were not present in the reviewed directory, and candidate 11 references supporting agents or skills that were not present in its directory.
- The best near-term fit for this repository came from workflow-review guidance and coverage interpretation, not from generic code-generation or generic security-review frameworks.

## Installation policy

GitHub's current documentation says `gh skill install` can pin a skill to a specific tag or commit SHA. The CLI supports both `OWNER/REPOSITORY SKILL@TAG-or-SHA` and `gh skill install OWNER/REPOSITORY SKILL --pin TAG-or-SHA`, and the docs say pinned skills are skipped during updates.

- `gh skill install` can pin to a commit or tag. It is not limited to floating latest versions.
- `@VERSION` and `--pin` are mutually exclusive according to the documentation.
- `gh skill preview` should be the required first step before any install.
- For this repository, an approved external skill should be installed by reference, not vendored, unless a later separate decision accepts the license and maintenance cost.
- Because this task must not add anything under `.github/`, any future trial should prefer user scope or another non-repository location supported by the toolchain. If the maintainer later wants a repository-scoped skill, that should be a separate change with its own review.

## Decisions needed from the maintainer

- Decide whether to allow any external skills at all beyond user-scope trials.
- Decide whether CC-BY-SA upstream skills should be categorically excluded from future consideration.
- Decide whether to pilot only the strongest fits now: `Coverage Analysis`, `Find Untested Sources`, and `GitHub Actions Hardening`.
- Decide whether exploit-focused workflow review should remain manual guidance only, or whether a narrower repo-owned skill should absorb the useful parts of candidates 8 and 10 instead.
