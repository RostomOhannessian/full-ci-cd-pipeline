---
applyTo: "docs/**/*.md,*.md,.github/*.md,.github/instructions/**/*.md,.github/skills/**/SKILL.md"
---

# Documentation rules

Applies from Phase 0 (WP0.4 and WP0.5).

- Put each page in the right Diataxis section (tutorials, how-to guides, reference, explanation) before you write it. Plan section 11.1 defines the sections.
- Write for a developer who knows the syntax but not the tool or pattern. Use US English and plain, direct, present-tense prose. Avoid marketing language, emojis, and exclamation marks.
- Use sentence-case headings, one H1 that matches the front matter `title`, and never skip a heading level.
- Use `-` for bullets and `1.`, `2.`, `3.` for steps. Leave a blank line before and after every heading, list, table, and code fence.
- Give every code fence a language (`text` when nothing fits). Say that a hand-written command block is illustrative unless it comes from a tested script (plan section 11.3).
- Write links as `[text](url)` and never as bare URLs. Use relative links for repository files, and link external URLs only after you have opened them.
- Do not link to a tool page that does not exist yet. Name the tool and list its inventory ID in front matter `tools:`.
- Give every page under `docs/` front matter with `title`, `description`, `audience`, `last-verified`, and `owner`, plus the fields its template adds (`tools`, `introduced`, `prerequisites`, `estimated-time`, `verified-against`). ADRs and research records use the keys in their templates.
- Add `(checked YYYY-MM-DD)` to any fact that can go stale, such as versions, licenses, prices, and vendor claims.
- Describe planned work as planned and name the work package that delivers it. Never claim that an unbuilt file, workflow, or service exists.
- Keep Mermaid diagrams small, valid, and tied to real repository paths.
- Tool pages need Overview, Decision rationale, Setup tutorial, and Further research. Tier A pages also need How this project uses it, Validation and troubleshooting, Security and operations, and Lab (see `docs/reference/tools/index.md`).
- Start ADRs from `docs/templates/adr-template.md` and back every claim that can go stale with dated evidence.
- Use tables for comparisons and inventories, and keep paragraphs short.
- Never put secrets, personal email addresses, or absolute local paths in documentation.
- In instruction files, use only the front matter keys GitHub documents (`applyTo`, and `excludeAgent` when needed). In skill files, use only the documented fields (`name`, `description`, and the optional ones GitHub lists).
- Before you push, run `docker compose -f tools/lint/compose.yaml run --rm markdownlint`, then the `links` and `secrets-worktree` services. CI runs the same commands in `.github/workflows/docs-quality.yml`.
- Treat markdownlint, link, and secret findings as blocking. Fix the content instead of loosening the configuration.
