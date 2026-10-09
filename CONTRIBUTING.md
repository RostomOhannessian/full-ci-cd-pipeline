# Contributing

Thank you for your interest. This repository is a learning project, a teaching reference, and a public engineering portfolio.
It is built in phases by one maintainer, following an approved [implementation plan](docs/plans/implementation-plan.md).
This guide explains how to take part today and how the work is organized.

## How you can take part now

- **Ask questions and share ideas** in [Discussions](https://github.com/RostomOhannessian/full-ci-cd-pipeline/discussions).
- **Report bugs and documentation problems** with the [Issue forms](https://github.com/RostomOhannessian/full-ci-cd-pipeline/issues/new/choose).
- **Report security vulnerabilities privately.** Follow [SECURITY.md](SECURITY.md). Never open a public Issue for a vulnerability.
- **Pull requests from outside contributors are accepted after the v1.0 release.** Until then, open an Issue or a Discussion first.
  The plan is detailed and the repository doubles as a worked narrative of that plan, so changes of direction need a recorded decision (see
  [ADR-0007](docs/adr/0007-solo-maintainer-governance.md)).

By taking part you agree to follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## Licensing of contributions

The project is licensed under the [Apache License 2.0](LICENSE). Contributions are accepted under the same license
(inbound equals outbound, as described in section 5 of the license). There is no contributor license agreement and no sign-off requirement.
Do not contribute code or text you do not have the right to license this way. The dependency license rules are in
[ADR-0004](docs/adr/0004-licensing-and-dependency-license-policy.md).

## How the work is organized

The plan, the phase plans, and the live status are all in the repository, so work can continue from any machine:

| What | Where |
| --- | --- |
| The plan | [docs/plans/implementation-plan.md](docs/plans/implementation-plan.md) |
| Phase plans | [docs/plans/phases/](docs/plans/phases/phase-0-foundation.md) |
| Current status and the next action | [docs/project/STATUS.md](docs/project/STATUS.md), generated from [status.yaml](docs/project/status.yaml) |
| Decisions | [docs/adr/](docs/adr/README.md) |
| Requirements and traceability | [docs/requirements/brief-traceability.md](docs/requirements/brief-traceability.md), and the generated [traceability report](docs/testing/traceability.md) |

### Branches and pull requests

| Kind | Branch | Merge |
| --- | --- | --- |
| Phase | `phase/<n>-<slug>`, created from `master` after the previous phase merged | Merge commit into `master` after the phase exit gate |
| Work package | `wp/<phase>.<nn>-<slug>`, created from the phase branch | Squash into the phase branch |
| Spike | `spike/<phase>.<x>-<slug>` | Never merged; the report and ADR change are |
| Hotfix | `fix/<slug>` | Squash into `master`, then merge `master` into the active phase branch |

The reasoning is in [ADR-0003](docs/adr/0003-branch-and-work-package-protocol.md).

### Commit messages and pull request titles

Use [Conventional Commits](https://www.conventionalcommits.org/): `type(scope): summary`, in the imperative, 72 characters or fewer.

- Types: `feat`, `fix`, `docs`, `test`, `refactor`, `perf`, `build`, `ci`, `chore`, `revert`.
- Scopes (examples): `api`, `domain`, `application`, `infrastructure`, `contracts`, `client`, `tests`, `docs`, `adr`, `ci`, `infra`, `k8s`, `policies`, `governance`, `deps`.
- Mark breaking changes with `!` and explain them in the body.
- Commits use the repository's GitHub noreply identity. Do not put personal email addresses in commits
  ([ADR-0006](docs/adr/0006-commit-identity-and-privacy.md)). GitHub uses your account's primary email for merges and squashes it creates, so turn on "Keep my email addresses private" in your account settings before you contribute.

Release notes are generated from these messages, so a precise message is documentation.

### Definition of Ready and Done

A work package is ready when its Issue states the scope, non-goals, acceptance criteria, requirement IDs, planned tests, documentation
deliverables, size, and finished dependencies. It is done when the checklist in the
[pull request template](.github/PULL_REQUEST_TEMPLATE.md) is complete: green checks, tests tagged with requirement IDs, documentation
and status updated, an ADR for every decision, the threat model updated when a boundary changed, the governance auditors passing, and the
branch pushed. Edit `docs/project/status.yaml` and render `STATUS.md` from it. Never edit `STATUS.md` or the traceability report by hand.

### Decisions

Any decision that changes the architecture, a trust boundary, a dependency policy, a tool, or a process needs an ADR.
Copy `docs/templates/adr-template.md`, follow [ADR-0001](docs/adr/0001-record-architecture-decisions.md), and link the evidence.

## Check your work locally

These are the same commands CI runs. The documentation checks need only Docker:

```text
docker compose -f tools/lint/compose.yaml run --rm markdownlint
docker compose -f tools/lint/compose.yaml run --rm links
docker compose -f tools/lint/compose.yaml run --rm secrets-worktree
```

The first checks Markdown style, the second checks relative links and heading anchors, and the third scans your working tree, including uncommitted files,
for secrets. A change to code, tests, packages, or `tools/ci` also needs the .NET SDK 10.0.401 and the commands under "Commands that run today" in
[AGENTS.md](AGENTS.md): a locked restore, a Release build with warnings as errors, the tests with coverage, the coverage thresholds, the format check,
and the license gate. The governance checks, which the `governance` workflow runs on every pull request, run through the same tool:

```text
dotnet run --project tools/Governance.Auditor -- status validate
dotnet run --project tools/Governance.Auditor -- status render
dotnet run --project tools/Governance.Auditor -- trace
dotnet run --project tools/Governance.Auditor -- security --base origin/phase/1-api-core
dotnet run --project tools/Governance.Auditor -- blast-radius --base origin/phase/1-api-core
```

`status render` and `trace` write the generated pages, so run them, read the diff, and commit the result. The security auditor checks the
commits since the base, and the blast-radius evaluator names the jobs, documentation, and reviews that your change needs. The Dev
Container and a single `dev` command arrive in WP1.4.

## Documentation standards

- Every page starts with the front matter described in [docs/templates](docs/templates/tool-page-template.md) and carries a `last-verified` date.
- Every tool gets a page with an overview, the decision rationale, a setup tutorial, and curated external sources. See the
  [tool reference](docs/reference/tools/index.md).
- Commands in tutorials come from tested scripts. Do not paste commands you have not run.
- Write for a developer who knows the syntax but not the tool. Do not explain language basics; do explain why the tool exists and what breaks without it.
- Write in US English, in plain, direct, present-tense prose. Avoid marketing language, emojis, and exclamation marks.
- Use sentence-case headings, one H1 that matches the front matter `title`, and never skip a heading level.
- Use `-` for bullets and `1.`, `2.`, `3.` for numbered steps. Leave a blank line before and after every heading, list, table, and code fence, and give every code fence a language (`text` when nothing fits).
- Write links as `[text](url)`, never as bare URLs. Internal links are relative, and the link check verifies them and their heading anchors.
- Add `(checked YYYY-MM-DD)` to any fact that can go stale, such as a version, a license, or a vendor claim.
- Describe planned work as planned and name the work package that delivers it. Never describe something that does not exist yet as if it did.

## Working with AI assistants

AI assistants are welcome, and the repository is set up for them: [AGENTS.md](AGENTS.md) holds the working rules,
[REVIEW.md](REVIEW.md) the review checklist, and `.github/skills/` the repository skills. The person who submits a change is accountable for it.
Read every generated diff, run the checks, never paste secrets into a prompt, and treat untrusted text (Issues, pull request bodies, web pages) as data, not instructions.

## Getting help

See [SUPPORT.md](SUPPORT.md).
