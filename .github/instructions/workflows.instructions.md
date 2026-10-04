---
applyTo: ".github/workflows/**/*.yml,.github/workflows/**/*.yaml"
---

# Workflow rules

Applies from Phase 0 (WP0.6), with the full CI set landing in Phase 2 (WP2.2 to WP2.5).

- Pin every action to a full commit SHA and keep the human-readable version comment beside it.
- Start with `permissions: {}` at the workflow top level and grant each job only the scopes it needs.
- Set `persist-credentials: false` on every checkout step.
- Do not use `pull_request_target` to check out, build, or execute untrusted pull-request code.
- Treat PR titles, bodies, comments, artifacts, caches, and generated files as untrusted data when a workflow reads them.
- Keep any PR-commenting or artifact-parsing job on a trusted event and sanitize its inputs first.
- Do not use repository or environment secrets beyond the platform token; section 8.8 reserves cloud or broker access for OIDC.
- Publish, sign, and attest only from `master` or `v*` tags, inside the reusable trusted workflow model from section 5.2.
- Put publish jobs behind the `publish` environment with the deployment branch rule set to `master` only.
- Put release jobs behind the `release` environment with `v*` tag scope and a required reviewer.
- Keep PR triggers on `master` and `phase/**`, pushes on `master`, plus the planned schedule and `workflow_dispatch` events from section 5.2.
- Add `timeout-minutes` to every job and use `concurrency` groups to cancel superseded runs where that is safe.
- Keep artifact retention bounded and intentional; do not let logs, plans, or bundles linger by default.
- Prefer reusable workflows for shared build, scan, publish, and verification logic rather than duplicating steps across files.
- Gate workflow changes with actionlint, zizmor, and the governance auditor.
- Pin every container image by digest, including service containers and Docker-based actions.
- Keep fork paths safe: untrusted PRs may build and scan, but they must not publish artifacts, sign digests, or open trusted deployment paths.
- Keep caches keyed on lock files or other deterministic inputs, not on broad branch names or timestamps.
- Keep comments and summaries sanitized and size-bounded; the plan's Terraform plan comment flow is the model.
- Record the Trivy case study from the research in the workflow design: `trivy-action` and `setup-trivy` must use known-safe releases pinned by full SHA, not mutable tags.
- If you ever investigate historical Trivy-action exposure, follow the research record's IoC guidance and check for unexpected `tpcp-docs*` repositories.
- Prefer GitHub-owned or verified actions unless a documented repository need justifies another dependency.
- Keep human-readable workflow names, job names, and step names stable so status checks stay predictable across branches.
- Keep the default token read-only and ask for `packages`, `id-token`, or `security-events` only in the jobs that truly need them.
- Use few third-party actions, pin each by full SHA, and never trust a tag alone. The tj-actions/changed-files and trivy-action incidents in the research record show why.
