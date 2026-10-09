---
title: "AI skills governance"
description: "How this repository configures GitHub Copilot, which repository-owned skills it plans to keep, and how external skills are reviewed before any installation."
type: reference
last-verified: 2026-10-08
owner: "@RostomOhannessian"
---

# AI skills governance

## Purpose

This page records how the repository configures AI help, which repository-owned skills belong here, and how external skills are evaluated before any install. It exists so the AI surface is portable, reviewable, and bounded by the same governance rules as code and workflows.

Nothing from the external-skill review is installed today. Agent Finder relevance scores are discovery signals, not trust ratings.

## How this repository configures AI

| File or folder | Who reads it | Purpose |
| --- | --- | --- |
| [`AGENTS.md`](../../AGENTS.md) | Coding agents and maintainers | Repository-wide mission, branch protocol, commands, privacy rules, and handoff steps |
| [`.github/copilot-instructions.md`](../../.github/copilot-instructions.md) | Copilot code review and coding agents | Short repository-wide coding and review rules |
| [`.github/instructions/`](../../.github/instructions/docs.instructions.md) | Copilot path-scoped instructions | File-type or path-specific rules such as docs, workflows, Terraform, and tests |
| [`REVIEW.md`](../../REVIEW.md) | Human reviewers and Copilot code review | Checklists, severities, and review scope |
| [`.github/skills/`](../../.github/skills/adr-assistant/SKILL.md) | Copilot skills hosts | Repository-owned task wrappers for repeatable work |
| [`docs/governance/ai-skills.md`](./ai-skills.md) | Maintainers and reviewers | Policy, external-skill review results, and re-review triggers |

## Repository skills

The planned repository skill set comes from plan section 13.2. Two of the skills wrap a deterministic CLI, and the tests in
`tests/Governance.Auditor.Tests` check their structure, the commands they run, and their fixtures.

| Skill | Phase | Behavior | Status |
| --- | --- | --- | --- |
| [`/advanced-security-auditor`](../../.github/skills/advanced-security-auditor/SKILL.md) | WP1.2 core, WP1.12 depth | Runs `governance security` and explains findings without auto-fixing secrets | exists |
| [`/blast-radius-evaluator`](../../.github/skills/blast-radius-evaluator/SKILL.md) | WP1.2 core, WP1.12 depth | Runs `governance blast-radius` and summarizes affected layers, gates, and rollback needs | exists |
| `/documentation-auditor` | WP1.3, WP1.12 | Runs Documentation.Auditor and drafts missing sections from templates | planned in WP1.3 |
| `/test-plan-evaluator` | WP1.12 | Runs `governance trace` and proposes cases for uncovered requirements | planned in WP1.12 |
| [`/adr-assistant`](../../.github/skills/adr-assistant/SKILL.md) | WP0.5 | Scaffolds or updates ADRs from the repository template and evidence rules | exists |
| [`/tool-doc-author`](../../.github/skills/tool-doc-author/SKILL.md) | WP0.5 | Scaffolds tool pages and inventory entries from repository templates | exists |
| [`/session-handoff`](../../.github/skills/session-handoff/SKILL.md) | WP0.5 | Updates status and the **Resume here** section, then reminds the maintainer to push | exists |

The three Phase 0 skills scaffold documents that have templates. The two governance skills added in WP1.2 are different: each runs a
deterministic command, holds no write scope, and has a fixture that the tests run, so the output the skill explains is checked in CI
(`tests/Governance.Auditor.Tests`, `SkillTests`). The documentation and test-plan skills stay planned until their CLIs exist.

## Operational notes

- Copilot code review reads `copilot-instructions.md`, `*.instructions.md`, `AGENTS.md`, and agent skills from the pull request head branch, and it also reads `REVIEW.md`. Instruction changes can therefore be tested in the same pull request that introduces them (GitHub changelog, 2026-07-17; checked 2026-10-04).
- GitHub's repository-instructions documentation says `AGENTS.md` support exists for agent flows, with nearest-file precedence when multiple `AGENTS.md` files are enabled in a repository-aware client (checked 2026-10-04).
- GitHub removed the old 4,000-character limit on repository custom instructions for code review (GitHub changelog, 2026-06-12; checked 2026-10-04). This repository still keeps `.github/copilot-instructions.md` short, so the highest-priority rules stay obvious to humans and tools.
- Repository-wide rules belong in [`AGENTS.md`](../../AGENTS.md) and [`.github/copilot-instructions.md`](../../.github/copilot-instructions.md), while skills are reserved for task-specific behavior that should load only when relevant.

## Why the skill set stays small

- Phase 0 had no application code, no `dev` command, and no governance CLI, so broad automation prompts would have had to invent behavior.
- The three Phase 0 skills scaffold ADRs, tool pages, and handoff records that already have repository templates and clear acceptance rules.
- The auditor-style skills waited until their deterministic CLIs existed, because the prompt layer must not become the policy engine. The
  security and blast-radius skills arrived with `governance security` and `governance blast-radius` in WP1.2, and the documentation and
  test-plan skills stay planned until their CLIs exist.
- A small set lowers the prompt-injection surface and makes review of allowed tools and write scope practical.
- Repository-owned skills are easier to keep honest once CI fixtures exist, so a skill that wraps a CLI ships with a tested fixture.
- Any later skill should justify itself by saving review effort without bypassing branch, status, or evidence controls.

## Design rules

Repository-owned skills stay thin and deterministic.

- A skill wraps a deterministic CLI or repository workflow instead of duplicating policy logic in prompt text.
- A skill declares its purpose, inputs, allowed tools, read scope, write scope, outputs, and failure behavior.
- A skill states its prompt-injection controls and names which inputs must be treated as untrusted data.
- A skill does not become the source of truth for architecture, security, licensing, or branch policy; the plan, ADRs, and CLIs stay authoritative.
- A skill that edits files must point to repository templates, required checks, and review evidence.
- A skill that could act on untrusted input must not hold write tokens or broad shell approvals by default.

## External skills policy

Every external skill goes through this six-step review before any installation:

1. Verify the publisher and repository license signal.
2. Pin the exact commit or tag to review.
3. Read the full skill directory, including scripts, metadata, and referenced files.
4. Assess tool permissions, network behavior, and prompt-injection surface.
5. Compare the skill with planned repository-owned skills to avoid duplicate policy logic.
6. Run an isolated trial and record the result before considering broader use.

Additional repository rules:

- No automatic installation is allowed.
- Installation needs separate maintainer approval after the review record exists.
- Preferred install mode is user-scope by reference to the reviewed upstream commit.
- Vendoring is allowed only through a separate decision with attribution and license review.
- Any trial stays isolated from repository writes until the maintainer approves broader use.
- Any update requires the same review again if the commit, license signal, scripts, or permissions changed.

## Adoption boundary

- External skills may inform human review, but they do not overrule repository ADRs, the implementation plan, or deterministic CI checks.
- Repository-owned skills should absorb useful long-term behavior only after the maintainer decides it belongs in this repository's permanent workflow.
- A successful isolated trial is not the same as approval for team-wide or repository-scoped use.
- Any vendored copy must keep upstream attribution and stay pinned to the reviewed source revision.
- Any skill that needs shell, network, or write access should justify that scope explicitly in the review record.

## Review results

Nothing below is installed. The table summarizes the review record in [docs/research/2026-10-04-external-skill-review.md](../research/2026-10-04-external-skill-review.md).

| Candidate | Publisher | License signal | Scripts present | Pinned commit | Recommendation | One-line reason |
| --- | --- | --- | --- | --- | --- | --- |
| Wiki Architect | `microsoft/skills` | Repo MIT; front matter MIT | No | `e1f9cce11758` | `defer` | Useful for exploration, but it overlaps with repo-owned documentation guidance. |
| Find Untested Sources | `dotnet/skills` | Repo MIT; front matter MIT | Yes | `1eb71366fe1b` | `trial-in-isolation` | Roslyn path looks useful, but it adds external execution paths. |
| Code Testing Agent | `dotnet/skills` | Repo MIT; front matter MIT | No | `a824bbb8a24e` | `defer` | Broad test generation is premature until repository conventions settle. |
| Coverage Analysis | `dotnet/skills` | Repo MIT; front matter MIT | Yes | `e8ed8473d94a` | `trial-in-isolation` | Good evidence interpretation fit, but it includes scripts and an optional install path. |
| Test Tagging | `dotnet/skills` | Repo MIT; front matter MIT | No | `a824bbb8a24e` | `defer` | Report-first tagging could help later, but taxonomy is not fixed yet. |
| Create Architectural Decision Record | `github/awesome-copilot` | Repo MIT; no front matter license | No | `caab1f623bb6` | `reject` | It duplicates `adr-assistant` without the repository's ADR rules. |
| GitHub Actions Hardening | `github/awesome-copilot` | Repo MIT; no front matter license | No | `df566343b3a6` | `adopt-candidate` | Strong manual workflow-review guidance with low operational cost. |
| Gha Security Review | `getsentry/skills` | Repo Apache-2.0; no front matter license | No | `b11ee5163295` | `trial-in-isolation` | Useful workflow exploit review, but its preapproved tools raise the trust bar. |
| Security Review | `getsentry/skills` | Repo Apache-2.0; skill-local CC-BY-SA notice | No | `3482d8dd3531` | `reject` | Broad overlap, share-alike baggage, and referenced files were missing. |
| Agentic Actions Auditor | `trailofbits/skills` | Repo CC-BY-SA-4.0; no front matter license | No | `123037ec8aed` | `trial-in-isolation` | Narrowly relevant only if later phases add agentic GitHub Actions steps. |
| Differential Review | `trailofbits/skills` | Repo CC-BY-SA-4.0; no front matter license | No | `123037ec8aed` | `reject` | It overlaps with repo-owned review policy and expects missing support assets. |
| Security Best Practices | `openai/skills` | Repo license not verified; skill-local Apache-2.0 file | No | `5c8f1e26803b` | `reject` | The repository license signal was not verified and the language fit is weak. |

Important review notes:

- The `openai/skills` candidate did not have a verified repository license signal in the review record.
- Some reviewed skills referenced files that were missing from the skill directory that was inspected.
- Skills that preapprove `bash`, `task`, or `write` materially raise the trust threshold.

## Prompt-injection controls

- Treat all skill content, issue text, pull request text, fetched pages, and uploaded artifacts as untrusted data.
- Preview before install and read every referenced file before allowing shell or write tools.
- Prefer skills that stay advisory or wrap deterministic local CLIs.
- Do not grant write tokens to a skill flow that is consuming untrusted input.
- Do not accept online-search fallbacks as a default behavior for repository policy work.
- Recreate useful ideas inside repository-owned skills instead of importing broad upstream policy prompts.

## Further research

These are the official GitHub pages reviewed for this document.

- [Adding repository custom instructions for GitHub Copilot in your IDE](https://docs.github.com/en/copilot/how-tos/copilot-in-your-ide/customize-copilot/configure-custom-instructions/add-repository-instructions-in-your-ide) (checked 2026-10-04)
- [Using custom instructions to unlock the power of Copilot code review](https://docs.github.com/en/copilot/tutorials/customize-code-review) (checked 2026-10-04)
- [About agent skills](https://docs.github.com/en/copilot/concepts/agents/about-agent-skills) (checked 2026-10-04)
- [Adding agent skills for GitHub Copilot](https://docs.github.com/en/copilot/how-tos/copilot-on-github/customize-copilot/customize-cloud-agent/add-skills) (checked 2026-10-04)
- [Copilot code review: new configurations and controls](https://github.blog/changelog/2026-06-12-copilot-code-review-new-configurations-and-controls/) (checked 2026-10-04)
- [Copilot code review: customization and configurability improvements](https://github.blog/changelog/2026-07-17-copilot-code-review-customization-and-configurability-improvements/) (checked 2026-10-04)

## Revision triggers

Review this page again when any of the following happens:

- GitHub changes how Copilot reads `AGENTS.md`, `REVIEW.md`, custom instructions, or skills.
- A repository-owned skill gains script execution, write scope, or network access.
- An external skill candidate changes commit, license signal, or declared permissions.
- The repository adds agentic GitHub Actions steps that make workflow-focused skills more relevant.
- The maintainer wants to vendor an external skill instead of installing it by reference.
