---
title: "Research record: GitHub and supply-chain verification (2026-10-04)"
description: "Source-checked findings on GitHub plan limits, Actions security, attestations, Sigstore, CodeQL licensing, and runner pricing."
type: research-record
date: 2026-10-04
status: raw
---

# Verification Report — CI/CD Pipeline Engineering Plan
**Verification date:** 2026-10-04 | **Project:** `RostomOhannessian/full-ci-cd-pipeline` (private, GitHub Free, personal account)

---

## 1. Private-repo feature availability: GitHub Free vs. Pro (contrasted with Public)

**(a) Repository rulesets & classic branch protection**
- **Fact:** Classic branch protection rules and repository rulesets follow the same pattern: full availability on **public** repos on any plan; on **private** repos they require **Pro** (personal) or **Team/Enterprise** (org) — "Protected branches" is explicitly listed as a feature GitHub Pro unlocks for private repos. Org-wide (multi-repo) rulesets additionally require Team/Enterprise regardless of visibility.
- **Source:** https://docs.github.com/en/get-started/learning-about-github/githubs-plans ; https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/creating-rulesets-for-a-repository ; corroborating community thread https://github.com/orgs/community/discussions/190190
- **Confidence:** High for the Pro-unlocks-private-repo pattern (direct plans-page citation); **Medium** specifically for rulesets (the two rulesets docs pages I fetched did not contain an explicit "Free = public-repo-only" callout box in the content I could retrieve — I inferred parity with classic branch protection, which is explicit).
- **Implication:** On Free, you cannot require PR reviews/status checks on your private repo at all today. Plan to add branch protection the moment the repo flips to public (free there), or pay for Pro now if protection is needed pre-launch.

**(b) Required PR reviews & CODEOWNERS enforcement**
- **Fact:** "Required pull request reviewers," "Multiple pull request reviewers," and "Code owners" are listed explicitly as GitHub **Pro**-tier features for **private** repositories; all are free on public repos on any plan.
- **Source:** https://docs.github.com/en/get-started/learning-about-github/githubs-plans ; https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/about-code-owners
- **Confidence:** High.
- **Implication:** CODEOWNERS file can exist in the repo today, but it will not block/request reviews until the repo is public or you upgrade to Pro.

**(c) Actions environments, environment secrets, deployment branch policies, required reviewers, wait timer**
- **Fact (precise, from the authoritative reference doc):**
  - Environments + environment secrets: **Free = public repos only**. Converting public→private causes existing environment config/secrets to be *ignored* (not deleted) until reverted to public. **Pro/Team unlock environments for private repos.**
  - Deployment branch/tag policies: available for all public repos; **Pro or Team** also unlocks for private repos.
  - **Required reviewers** and **wait timer**: *"If you are on a GitHub Free, GitHub Pro, or GitHub Team plan, required reviewers [wait timers] are only available for public repositories."* — i.e., these two specific protection rules need **Enterprise Cloud** for private repos; Pro/Team do **not** unlock them even though Pro/Team unlock environments generally.
- **Source:** https://docs.github.com/en/actions/how-tos/deploy/configure-and-manage-deployments/manage-environments ; https://docs.github.com/en/actions/reference/workflows-and-actions/deployments-and-environments
- **Confidence:** High (verbatim doc text).
- **Implication:** **This is a critical, easy-to-miss gap.** Your dev→staging→prod gating via "required reviewers"/"wait timer" on a private repo is impossible on Free *or even Pro* — only GHEC unlocks it. Simulate manual gates with a `workflow_dispatch` approval step or external check until the repo is public (where it becomes free).

**(d) GitHub Pages publishing**
- **Fact:** Free = Pages on **public** repos only. Pro adds Pages for private repos, but *"to publish a GitHub Pages site privately, you need an organization account on GitHub Enterprise Cloud"* — i.e., even on Pro, a private repo's Pages site is still served **publicly** once enabled.
- **Source:** https://docs.github.com/en/get-started/learning-about-github/githubs-plans
- **Confidence:** High.
- **Implication:** Not core to this pipeline, but if you add a docs/reports site, it won't work privately under Free.

**(e) Code scanning / CodeQL SARIF upload (GitHub Code Security)**
- **Fact:** Code scanning/SARIF-upload is a **GitHub Code Security** (Advanced Security) feature. It's enabled by default for **public** repos at no cost. For **private** repos it requires purchasing GitHub Code Security, and **that purchase requires a GitHub Team or Enterprise plan** — personal-account Free *and Pro* cannot buy it at all.
- **Source:** https://docs.github.com/en/get-started/learning-about-github/about-github-advanced-security (verbatim: *"You must be on a GitHub Team or GitHub Enterprise plan in order to purchase GitHub Code Security or GitHub Secret Protection."*)
- **Confidence:** High.
- **Implication:** No SARIF upload to the Security tab while private, regardless of personal-plan tier. CodeQL can still run and emit a SARIF artifact (see item 12), just not upload it.

**(f) Secret scanning & push protection (GitHub Secret Protection)**
- **Fact:** Same gating as (e): free/default-on for **public** repos (push protection became default-on for new public personal-account repos starting **March 2024**); for **private** repos requires purchasing GitHub Secret Protection, which again requires **Team/Enterprise** — not purchasable on personal Free/Pro.
- **Source:** https://docs.github.com/en/get-started/learning-about-github/about-github-advanced-security ; https://github.blog/changelog/2024-03-11-secret-scanning-and-push-protection-are-enabled-by-default-on-new-public-repositories/
- **Confidence:** High.
- **Implication:** Zero secret-scanning coverage on the private repo today — reinforces "no long-lived secrets in Actions" as a hard requirement, not just best practice.

**(g) dependency-review-action**
- **Fact:** *"The action is available for all public repositories, as well as private repositories that have GitHub Code Security or GitHub Advanced Security enabled."*
- **Source:** https://docs.github.com/en/code-security/concepts/supply-chain-security/dependency-review
- **Confidence:** High.
- **Implication:** Combined with (e)/(f), this action is **non-functional on your private repo** until public or until Code Security is purchased (which your personal Free plan cannot do anyway).

**(h) Artifact attestations (actions/attest-build-provenance, actions/attest, actions/attest-sbom, `gh attestation verify`)**
- **Fact (the single most important finding in this report):** *"Artifact attestations are available in public repositories for all current GitHub plans... To use artifact attestations in private or internal repositories, you must be on a GitHub Enterprise Cloud plan."* This is **stricter than (e)/(f)** — it's not purchasable as an add-on at all; it requires the GHEC plan itself.
- **Source:** `actions/attest:README.md` (fetched today) and `actions/attest-build-provenance:README.md` (fetched today) — identical NOTE blocks in both.
- **Confidence:** High (verbatim, from the GitHub-maintained action's current README).
- **Implication:** **Your entire SLSA-provenance/attestation story (items 8–9) is blocked while the repo is private**, on Free, Pro, *or Team*. `actions/attest` will simply not work. You must either (i) defer attestation generation to post-publicization, or (ii) use **raw `cosign sign --keyless`** directly against the public-good Sigstore instance (bypassing GitHub's gated attestations API) — but see item 10 for the public-disclosure consequence of doing that from a private repo.

**(i) Auto-merge**
- **Fact:** Public repos — Free and up. Private repos — **Pro** or higher.
- **Source:** https://docs.github.com/en/pull-requests/how-tos/merge-and-close-pull-requests/automatically-merging-a-pull-request (confirmed path via search; 404 on alternate guessed path)
- **Confidence:** Medium-High (consistent with every other Free/Pro pattern found, but I did not independently re-fetch this exact page's full text after the 404 redirect).
- **Implication:** Minor; not core to the pipeline's required capabilities.

**(j) Merge queue**
- **Fact:** GA'd July 2023: *"Merge queue is available on private and public repos on the GitHub Enterprise Cloud plan **and all public repos owned by organizations**."* Note precisely: it does **not** say "public repos owned by personal accounts." Your repo is personal-account-owned.
- **Source:** https://github.blog/changelog/2023-07-12-pull-request-merge-queue-is-now-generally-available/
- **Confidence:** High (verbatim GA announcement; I did not find a more recent change widening this to personal-account public repos).
- **Implication:** Even after you flip the repo to public, merge queue may **still not be available** because it's a personal-account repo, not an org repo. Verify directly in repo Settings before depending on it; don't design the promotion pipeline around merge queue.

---

## 2. GitHub-hosted runner specs & included minutes

- **Fact:** Current (live, fetched today) standard runner specs:

| Repo visibility | Linux x64/arm64 | Windows x64/arm64 | macOS Intel | macOS arm64 (M1) |
|---|---|---|---|---|
| **Public** | 4 vCPU / 16 GB | 4 vCPU / 16 GB | 4 vCPU / 14 GB | 3 vCPU / 7 GB |
| **Private** | 2 vCPU / 8 GB | 2 vCPU / 8 GB | 4 vCPU / 14 GB | 3 vCPU / 7 GB |

  Included minutes/month: **Free = 2,000**, **Pro = 3,000** (Team = 3,000, GHEC = 50,000). Included artifact storage: Free 500 MB, Pro 1 GB (shared with GitHub Packages).
  Current per-minute rates (post-Jan-2026 cut): Linux 2-core $0.006, Linux 1-core-slim $0.002, Linux arm64 $0.005, Windows (x64/arm64) $0.010, macOS $0.062.
  **2026 pricing change:** GitHub cut GitHub-hosted runner prices **up to 39% effective January 1, 2026**.
- **Source:** https://docs.github.com/en/actions/reference/runners/github-hosted-runners ; https://docs.github.com/en/billing/concepts/product-billing/github-actions ; https://docs.github.com/en/billing/reference/actions-runner-pricing ; https://github.blog/changelog/2025-12-16-coming-soon-simpler-pricing-and-a-better-experience-for-github-actions/
- **Confidence:** High for specs/minutes/Jan-2026 cut (live docs + changelog). **Medium** for the classic "Windows=2×/macOS=10×" *included-minutes multiplier* framing — current docs present cost as direct per-minute SKU rates rather than a multiplier-against-quota model; I could not re-confirm the old multiplier terminology is still used verbatim post-restructure.
- **Implication:** Public CI (CodeQL, Trivy, Syft, builds) gets **2× the cores/RAM** of what the same workflow would get while private — budget CI time accordingly pre- vs. post-publicization, and don't be surprised if jobs run faster once the repo goes public.

---

## 3. Self-hosted runner "platform fee" (Dec 2025 announcement)

- **Fact:** On **2025-12-16**, GitHub announced that from **2026-03-01** it would add a **$0.002/minute "Actions cloud platform charge"** applying to **self-hosted runner usage on private repos** (public repos explicitly exempt; GHES unaffected). The *same changelog post* was later updated with: *"We're postponing the announced billing change for self-hosted GitHub Actions to take time to re-evaluate our approach."* Independent trackers as of Aug–Oct 2026 confirm the charge **was never implemented** and self-hosted runner usage remains free regardless of repo visibility; GitHub called it "postponed," explicitly **not** "canceled," and has not announced a new date.
- **Source:** https://github.blog/changelog/2025-12-16-coming-soon-simpler-pricing-and-a-better-experience-for-github-actions/ (primary, contains both the original announcement and the postponement update) ; community discussion https://github.com/orgs/community/discussions/182186
- **Confidence:** High that it was announced and postponed; **Medium** on "still postponed as of today" since this relies partly on secondary trackers rather than a GitHub-dated October 2026 confirmation — I found no newer official GitHub changelog entry reinstating or formally canceling it.
- **Implication:** Your self-hosted-runner-to-local-kind-cluster deployment path is currently free, but **treat this as a live risk, not settled policy** — GitHub explicitly reserved the right to reintroduce pricing. Re-check before/at go-live.

---

## 4. `GITHUB_TOKEN` and workflow-triggering

- **Fact:** Confirmed verbatim: events from the default `GITHUB_TOKEN` do **not** trigger new workflow runs, **except** `workflow_dispatch` and `repository_dispatch`. One added nuance: a `pull_request` event (opened/synchronize/reopened) caused by a `GITHUB_TOKEN`-authored push *will* create a run, but it lands in an **approval-required** state needing a maintainer to click "Approve workflows to run." Documented alternative: use a **GitHub App installation access token** (e.g., via `actions/create-github-app-token`) or a classic PAT instead of `GITHUB_TOKEN` — both trigger events normally, including removing the approval-required PR behavior.
- **Source:** https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/trigger-a-workflow (section "Triggering a workflow from a workflow")
- **Confidence:** High.
- **Implication:** For your GitOps promotion flow (dev→staging→prod via automated PRs/commits), if any stage needs to auto-trigger a downstream workflow, you need a GitHub App token (no long-lived secret — satisfies your "no long-lived secrets" constraint, since App tokens are short-lived and minted via OIDC-free but ephemeral private-key exchange... note the **private key itself** is a standing secret unless you rotate/manage it carefully; a PAT would violate your "no long-lived secrets" rule).

---

## 5. tfsec status

- **Fact:** `aquasecurity/tfsec` is **not archived** but its own description reads *"Tfsec is now part of Trivy"*; last tagged release is **v1.28.14 (2025-05-02)** — effectively feature-frozen. `aquasecurity/tfsec-action` is likewise not archived but has had **no release since v1.0.3 (2023-01-26)** and no commits since **2023-02-24** — abandoned in practice. Aqua's official, repo-hosted migration guide recommends replacing `tfsec .` with **`trivy config .`** (or `trivy fs --scanners misconfig`), and states **AVD-\* rule IDs carry over unchanged**.
- **Source:** live `gh api repos/aquasecurity/tfsec` and `repos/aquasecurity/tfsec-action` (checked today) ; https://github.com/aquasecurity/tfsec/blob/master/tfsec-to-trivy-migration-guide.md
- **Confidence:** High.
- **Implication:** **Do not add `aquasecurity/tfsec-action` to a new pipeline in 2026.** Use `aquasecurity/trivy-action` (or `setup-trivy` + `trivy config`) for the Terraform misconfiguration-scanning stage instead — this also consolidates your scanning tooling (you already need Trivy for image scanning), reducing the number of third-party actions in your supply chain.

---

## 6. 2025–2026 Actions supply-chain incidents & hardening

- **Fact — tj-actions/changed-files:** **GHSA-mrrh-fwg8-r2c3 / CVE-2025-30066**, active **2025-03-14 to 03-15**. Attacker force-pushed version tags to a malicious commit that dumped runner memory and printed secrets (base64-encoded) to logs — publicly visible in public-repo logs. ~23,000 repos affected. Patched in **v46.0.1**; current latest is **v47.0.6**.
- **Fact — aquasecurity/trivy-action & aquasecurity/setup-trivy (major, previously unknown to me — directly relevant to your pipeline):**
  - **GHSA-9p44-j4g5-cfx5 / CVE-2026-26189** (2026-02-18, medium, CVSS 5.9): command injection in trivy-action via unescaped env-var export from untrusted inputs (e.g., PR title) sourced in `entrypoint.sh`. Affected `>=0.31.0, <0.34.0`; fixed in **0.34.0**.
  - **GHSA-69fq-xp46-6x23 / CVE-2026-33634** (2026-03-24, **critical**, CVSS4 9.4): a **full ecosystem supply-chain compromise**, 2026-03-19/20. Attacker used compromised credentials to (1) publish a malicious Trivy **v0.69.4** binary/image release containing an infostealer (process-memory dumping, credential sweeping of SSH/cloud/K8s/Docker/`.env`/DB creds, AES-RSA hybrid-encrypted exfiltration, with a GitHub-repo-upload fallback channel), (2) **force-push 76 of 77 version tags** of `trivy-action` to malicious commits, and (3) **replace all 7 tags** of `setup-trivy` with malicious commits. Exposure windows ran 3–12 hours. **Known-safe versions:** Trivy binary v0.69.2/v0.69.3; **trivy-action v0.35.0+** (pre-0.35.0 historical tags were recreated with a `v`-prefix, e.g. `@v0.34.0`, since the originals are now immutable/unusable); **setup-trivy v0.2.6+**. Current latest tags today: `trivy-action@v0.36.0`, `setup-trivy@v0.3.1` — both safe.
  - **reviewdog/reviewdog:** no GHSA/CVE advisories found in GitHub's advisory database as of today.
- **Fact — GitHub's hardening guidance / 2025–2026 shipped features:**
  - **SHA-pinning enforcement**: shipped **2025-08-15** — admins can now check a box under the "allowed actions" policy (repo/org/enterprise level) to require every action reference be a full commit SHA; non-conforming workflows fail outright.
  - **Immutable releases**: a real, shipping GitHub feature (not just roadmap) — confirmed **active in production by March 2026** per the Trivy GHSA itself, which cites specific Trivy releases as "protected by GitHub's immutable releases feature (enabled March 3[–4], before v0.69.3/trivy-action v0.35.0 was published)." Once enabled per-repo, a release's Git tag and assets become permanently locked, with an auto-generated cryptographic "release attestation."
  - GitHub's **March 2026 "2026 Actions security roadmap"** post explicitly names **tj-actions/changed-files, Nx (npm, Aug 2025 "s1ngularity" attack), and trivy-action** as the incidents motivating a forthcoming `dependencies:` workflow-lock-file mechanism (SHA-pinned direct+transitive action dependencies, "go.sum for workflows"), targeted for public preview 3–6 months and GA ~6 months from that post (i.e., roughly mid-to-late 2026) — **I could not confirm whether this has shipped/GA'd as of today** (search tooling failures prevented a final check); treat as **not yet confirmed GA**.
  - Other standard guidance referenced across these advisories: `persist-credentials: false` on checkout, Harden-Runner (StepSecurity, third-party) for egress monitoring — both are standard community practice but I did not re-verify a 2025/2026-specific GitHub endorsement beyond their general secure-use docs.
- **Source:** `gh api /advisories/GHSA-mrrh-fwg8-r2c3`, `/advisories/GHSA-9p44-j4g5-cfx5`, `/advisories/GHSA-69fq-xp46-6x23` (official GitHub Advisory Database, queried today) ; https://github.blog/changelog/2025-08-15-github-actions-policy-now-supports-blocking-and-sha-pinning-actions/ ; https://docs.github.com/en/code-security/concepts/supply-chain-security/immutable-releases ; https://github.blog/news-insights/product-news/whats-coming-to-our-github-actions-2026-security-roadmap/ (published 2026-03-26)
- **Confidence:** High for all GHSA facts and shipped features (primary sources); Medium/Low for the `dependencies:` lock-file GA status (unconfirmed timing).
- **Implication:** **This is directly load-bearing for your plan.** You named `aquasecurity/trivy-action` explicitly as a pipeline component — it was at the center of a critical, credential-stealing supply-chain compromise seven months ago. You must (1) pin to `trivy-action@v0.36.0`/`setup-trivy@v0.3.1` **by full commit SHA**, not a mutable tag, (2) enable SHA-pinning enforcement at the repo/org level now that it's available, and (3) check for any `tpcp-docs`-prefixed repos under your account as an IoC check if you ever ran trivy-action between 2026-03-19 and 03-20.

---

## 7. Cosign v3

- **Fact:** Current latest is **v3.1.3 (2026-08-06)**. Per the official Sigstore blog announcing v3: the new unified **Sigstore bundle format** (`--new-bundle-format`), **`--trusted-root`** (TUF-rotatable trust material in one file), and **`--use-signing-config`** (rotatable transparency-log shards) all flip from **opt-in (2.6.x) to on-by-default in v3** — this aligns with real deployments like Homebrew, PyPI, Maven Central, and the tile-based **Rekor v2** log. New helper subcommands: `cosign bundle create`, `cosign trusted-root create`, `cosign signing-config create`. Old flags/formats still work as an explicit opt-out in v3.x but Sigstore states **v4 will remove them** ("there probably won't be many releases of v3").
- **Fact — Kyverno compatibility:** A real, now-**closed** bug, `kyverno/kyverno#15327` ("[Bug] Unable to validate Cosign v3 signatures or attestations with Kyverno 1.17"), opened 2026-02-20, **closed 2026-03-16** — i.e., it **was fixed**. A separate, still-**open** issue `kyverno/kyverno#17833` (opened 2026-10-02, two days before this report) notes a **performance quirk**: Kyverno's `ImageValidatingPolicy` fetches every Cosign bundle **twice** per check (not a correctness bug). Current Kyverno is **v1.19.1 (2026-09-10)**.
- **Source:** https://blog.sigstore.dev/cosign-3-0-available/ ; `gh api repos/sigstore/cosign` (releases, checked today) ; `gh api repos/kyverno/kyverno/issues/15327` and `/17833` (checked today)
- **Confidence:** High.
- **Implication:** Current Kyverno (v1.19.1) **should correctly verify Cosign v3-produced signatures/attestations** (the correctness bug is resolved), but expect a documented double-fetch performance overhead on `ImageValidatingPolicy`-based checks — budget extra admission-latency, and pin Kyverno to ≥ the release that merged the #15327 fix (released after 2026-03-16), not an older 1.17.0.

---

## 8. GitHub artifact attestations — format, storage, SLSA level, and admission-control integration

- **Format:** JSON-serialized **Sigstore bundle** (`sigstore/protobuf-specs` `sigstore_bundle.proto`), in-toto attestation statement, SLSA **v1.0** provenance predicate by default.
- **Storage:** Always uploaded to GitHub's **attestations API** (viewable under the repo's Actions tab / Attestations); optionally **also** pushed as an **OCI referrer** alongside the image when `push-to-registry: true` is set on `actions/attest` (requires `packages: write`); organization-owned repos additionally get an **"Artifact Metadata Storage Record"** (new REST endpoint, `/rest/orgs/artifact-metadata`) when `push-to-registry` is used.
- **SLSA Build Level:** Plain workflow → **SLSA v1.0 Build Level 2**. Using a (shared/vetted) **reusable workflow** to perform the actual build → **SLSA v1.0 Build Level 3** (isolation between caller and builder).
- **Plan gating (repeating/critical from item 1h):** Private/internal repos need **GitHub Enterprise Cloud** — not available on Free/Pro/Team at all.
- **Admission-control integration:** When pushed as an OCI referrer, the attestation is a standard Sigstore-bundle-wrapped in-toto statement — in principle verifiable by any Sigstore-aware verifier (Cosign, Kyverno, policy-controller) that trusts the correct Fulcio/Rekor root (GitHub's own private Sigstore instance for private-repo attestations — which federates **only** with GitHub Actions and isn't the public Sigstore root; vs. the public-good instance for public repos).
- **Official GitHub Kubernetes admission product:** **I found no GitHub-branded Kubernetes admission controller.** `sigstore/policy-controller` is a community Sigstore project (not GitHub-authored) that supports keyless verification against configurable Fulcio/Rekor roots; Kyverno likewise supports generic keyless/Sigstore-bundle verification. Neither is "GitHub-provided."
- **Source:** https://docs.github.com/en/actions/concepts/security/artifact-attestations ; https://docs.github.com/en/actions/how-tos/secure-your-work/use-artifact-attestations/use-artifact-attestations ; `actions/attest:README.md` (fetched today) ; `gh api repos/sigstore/policy-controller` (checked today, description confirms community Sigstore ownership, latest v0.15.1)
- **Confidence:** High on format/storage/SLSA level/plan-gating; **Medium** on "Kyverno can verify GitHub attestations as OCI referrers" — this is architecturally sound (same bundle format) but I did not find a worked, GitHub- or Kyverno-documented end-to-end example confirming it in practice, and it's moot for you anyway since item 1h/8 blocks it on your private Free repo regardless.
- **Implication:** Given the Enterprise-Cloud gate, **you cannot use GitHub's native attestation feature while private.** Your realistic path is raw Cosign (keyless, public-good instance) + Kyverno verifying against the public Fulcio/Rekor root — which is exactly the combination covered by item 10's public-disclosure caveat below.

---

## 9. slsa-framework/slsa-github-generator

- **Fact:** Repo is **not archived**; still receives minor commits (last push 2026-08-07) but its **latest tagged release is v2.1.0 from 2025-02-24** — over 19 months stale as of today. Critically, the **container-image provenance builder specifically** (`internal/builders/container`, i.e., `generator_container_slsa3.yml` — the exact workflow relevant to your pipeline) carries this explicit banner in its current README: *"This project is no longer actively maintained. For new integrations we suggest GitHub artifact attestations."*
- **Source:** `slsa-framework/slsa-github-generator:internal/builders/container/README.md` (fetched today) ; `gh api repos/slsa-framework/slsa-github-generator` (checked today)
- **Confidence:** High on the maintenance-status banner (verbatim, current). **Could not verify** the specific `private-repository: true` flag / "public Rekor disclosure" caveat language in the portions of the docs I was able to retrieve — I searched the top-level README and the container-builder README for "private-repository"/"Rekor"/"transparency" and found no matches in either. **This sub-point is unverified; do not assume the flag still exists or behaves as historically documented without checking the live workflow source directly.**
- **Implication:** This creates a real gap in your plan: the tool GitHub itself now tells you to use *instead* of slsa-github-generator for containers (native artifact attestations) is the very thing **blocked on your private Free repo** (items 1h/8). Don't build your pipeline around slsa-github-generator's container builder for anything beyond short-term/experimental use; plan to switch to `actions/attest` once public, and use plain Cosign signing (not SLSA provenance) as the private-repo interim.

---

## 10. Keyless Cosign signing from a private repo — public identity disclosure

- **Fact:** When signing keylessly against the **public-good Sigstore instance** (Fulcio + Rekor), the short-lived code-signing certificate embeds — as **publicly readable, DER-encoded X.509v3 extensions** under OID arc `1.3.6.1.4.1.57264.1.*` — at minimum:

| OID | Field | Contents |
|---|---|---|
| .1.9 | Build Signer URI | the exact workflow file (incl. ref) that performed signing — may be a reusable workflow |
| .1.10 | Build Signer Digest | commit SHA of that workflow file |
| .1.11 | Runner Environment | `github-hosted` vs `self-hosted` |
| .1.12 | Source Repository URI | `https://github.com/{owner}/{repo}` |
| .1.13 | Source Repository Digest | commit SHA being built |
| .1.14 | Source Repository Ref | the branch/tag ref |
| .1.16/.17 | Source Repository Owner URI/Identifier | the account/org |
| .1.18/.19 | Build Config URI/Digest | the top-level/initiating workflow + its SHA |
| .1.20 | Build Trigger | triggering event, e.g. `push` |
| .1.21 | Run Invocation URI | direct link to the specific Actions run/attempt |
| .1.22 | **Source Repository Visibility At Signing** | literally `"private"` or `"public"` |
| .1.23 | Deployment Environment | e.g. `production`, if an environment was used |
| SAN | — | typically the Build-Signer-URI as a URL |

  All of this — plus the signature itself — is written to the **publicly-readable, immutable Rekor transparency log** the moment you sign, regardless of whether the *source repository* is private. The one-time-sensitive fact (field .1.22) that the repo *was private at signing time* is itself permanently, publicly disclosed.
- **Source:** https://github.com/sigstore/fulcio/blob/main/docs/oid-info.md (canonical OID registry, fetched today)
- **Confidence:** High.
- **Implication:** **This is a significant, concrete privacy consideration you must decide on deliberately.** If you `cosign sign --keyless` (raw, not via `actions/attest`) from your private repo against the public-good instance to work around the item-8/9 gating, you will permanently and publicly leak: your exact repo name, the workflow path/ref/commit that built each image, every triggering event, and direct links to the specific Actions run — for as long as Rekor exists — even while the repo itself stays private. This is arguably acceptable since you plan to go public eventually, but confirm you're comfortable with it being disclosed *before* that date, not after.

---

## 11. GHCR package visibility independent of repository visibility

- **Fact:** The **Container registry** (ghcr.io) is one of the registries supporting **granular permissions** — a package's **visibility** can be set independently of its linked repository's visibility (unlike repo-scoped-only registries like Maven/Gradle). By default, a package **inherits access *permissions*** from a linked repo (only if linked *before* first publish) but does **not** inherit *visibility* — visibility must be set explicitly, public or private, in the package's own settings.
- **Caveats:**
  - **Reversion:** Multiple independent community sources (Stack Overflow, GitHub Community discussions) consistently report that **once a GHCR package is switched to public, it cannot be switched back to private** — the only workaround is deleting and republishing under a private name. **I could not locate this exact sentence in the official docs pages I retrieved today**, despite reading the full "Configuring a package's access control and visibility" and "About permissions for GitHub Packages" pages — so this is **community-corroborated, not independently doc-verified** by me.
  - **Anonymous pulls:** Confirmed by docs — *"in the Container registry, public packages allow anonymous access and can be pulled without authentication."* No official, numeric rate-limit figure for anonymous ghcr.io pulls was found in GitHub's docs; third-party sources claim no enforced/publicized limit exists (unlike Docker Hub), but this is **not an official GitHub commitment** and undocumented throttling (HTTP 429) has been anecdotally reported.
- **Source:** https://docs.github.com/en/packages/learn-github-packages/configuring-a-packages-access-control-and-visibility ; https://docs.github.com/en/packages/learn-github-packages/about-permissions-for-github-packages
- **Confidence:** High on independence-from-repo-visibility and inheritance mechanics; **Medium** on irreversibility (community-sourced); **Low-Medium** on anonymous rate limits (no official number found either way).
- **Implication:** Your "package public, repo private" design is directly supported — make the GHCR package public explicitly in its own settings (don't rely on inheritance). Treat the public flip as **one-way**: don't flip it on accidentally before you're ready, and don't assume a specific anonymous-pull SLA for Kyverno/kind-cluster image pulls at scale.

---

## 12. CodeQL CLI on a private Free-plan repo — licensing

- **Fact (verbatim from the current, official license text):** The CodeQL CLI's Terms and Conditions permit use *"in connection with an Open Source Codebase"* only (or academic research/demos/testing OSI-licensed queries) — they explicitly **do not authorize** use *"in connection with any codebase that is not an Open Source Codebase (e.g., code in a private repo in GitHub)"* — **unless** *"your use of the Software is under a paid customer license for GitHub Advanced Security."* As established in item 1(e)/(f), **GitHub Advanced Security products can only be purchased on Team/Enterprise plans** — a personal Free **or Pro** account cannot obtain that license at all.
- **Fact:** Technically, nothing stops the CodeQL CLI/Action binary from *executing* against your private repo and writing a SARIF file as a workflow artifact (upload-to-code-scanning being unavailable is a separate, plan-based *mechanical* restriction — see item 1e) — but doing so, and even just **keeping the SARIF as an artifact without uploading it**, **is a Terms-of-Service violation** for a private, non-open-source codebase without a GHAS license. The restriction is about analysis occurring at all, not about where the output goes.
- **Source:** https://raw.githubusercontent.com/github/codeql-cli-binaries/main/LICENSE.md (fetched today, quoted verbatim above)
- **Confidence:** High.
- **Implication:** **You should not run CodeQL against this repo's private code at all until it is public**, even "just to keep a SARIF artifact for later." This is a genuine license-compliance risk, not merely a feature gap — plan CodeQL as a post-publicization addition, or confirm with GitHub/legal if you believe an exception applies.

---

## 13. Current action/tool versions (confirmed live via GitHub API, 2026-10-04)

| Action | Latest version | Published |
|---|---|---|
| `actions/checkout` | **v7.0.1** | 2026-07-20 |
| `actions/setup-dotnet` | **v6.0.0** | 2026-07-16 |
| `github/codeql-action` | major tag **`v4`** (currently `v4.38.2`); separately, the CodeQL **CLI/query bundle** it pulls is versioned independently (latest `codeql-bundle-v2.27.1`, 2026-09-22) | v4.38.2: recent; confirm exact date via `gh api repos/github/codeql-action/tags` |
| `docker/build-push-action` | **v7.4.0** | 2026-09-15 |
| `docker/setup-buildx-action` | **v4.4.1** | 2026-09-16 |
| `anchore/sbom-action` | **v0.24.3** | 2026-10-02 |
| `aquasecurity/trivy-action` | **v0.36.0** (post-incident safe; see item 6) | 2026-04-22 |
| `aquasecurity/setup-trivy` | **v0.3.1** (post-incident safe; see item 6) | 2026-06-03 |
| `sigstore/cosign-installer` | **v4.1.2** | 2026-05-07 |
| `actions/attest-build-provenance` | **v4.2.2** — *as of v4, this is purely a thin wrapper over `actions/attest`; GitHub recommends using `actions/attest` directly for new workflows* | 2026-08-06 |
| `actions/attest` | **v4.2.2** (recommended going forward) | 2026-08-04 |
| `actions/create-github-app-token` | **v3.2.0** | 2026-05-12 |
| `hashicorp/setup-terraform` | **v4.0.1** | 2026-05-12 |
| `devcontainers/ci` | **v0.3.1900000450** (this is devcontainers/ci's actual, unusual versioning scheme — not a typo) | 2026-06-01 |

*Bonus, not explicitly requested but relevant:* `actions/attest-sbom` v4.1.0 (2026-03-18); core Trivy v0.75.0 (2026-10-01); Cosign v3.1.3 (2026-08-06); Kyverno v1.19.1 (2026-09-10).

- **Source:** All rows via `gh api repos/{owner}/{repo}` and `repos/{owner}/{repo}/releases/latest` / `/tags`, queried live today against GitHub's REST API.
- **Confidence:** High — this is live, current data, not training-set knowledge.
- **Implication:** All of these should be pinned by **full commit SHA** (not the `vN` tag) per item 6's hardening guidance, with a comment noting the SHA-pinning enforcement policy you should also enable at the repo level.

---

## Top risks for this project (ranked by severity)

1. **CodeQL license violation (item 12).** Running CodeQL CLI/Action against this private repo's code without a GHAS license — which a personal Free/Pro account structurally cannot obtain — is a Terms-of-Service violation today, independent of SARIF upload. **Action: disable the CodeQL job (or scope it to a public mirror/OSS-licensed subset) until the repo goes public.**
2. **Native GitHub artifact attestations are unusable while private, on any personal plan (items 1h, 8, 9).** Your SLSA/provenance/attestation design point (`actions/attest`, and slsa-github-generator's now-explicitly-unmaintained container builder that GitHub itself tells you to replace with the thing that's gated) has **no fully-compliant, native path while private.** Decide now: raw Cosign keyless signing (accepting public Rekor disclosure of repo identity, item 10) vs. deferring provenance entirely until public.
3. **`trivy-action`/`setup-trivy` were at the center of a critical, credential-stealing supply-chain compromise seven months ago (item 6).** Must SHA-pin to the confirmed-safe commits behind `v0.36.0`/`v0.3.1`, enable the repo's SHA-pinning-enforcement policy, and add a post-incident IoC check (no `tpcp-docs*` repos on the account).
4. **Deployment "required reviewers"/"wait timer" protection rules are unavailable for private repos on Free, Pro, *and* Team (item 1c)** — your dev→staging→prod gate design needs a manual workaround (e.g., `workflow_dispatch` + branch-based approval) until public or on GHEC.
5. **Self-hosted-runner pricing is an unresolved, explicitly-reversible policy decision, not settled fact (item 3).** Don't hard-code "self-hosted is free" into cost assumptions for the life of the project.
6. **Keyless Cosign/SLSA signing from the private repo permanently and publicly discloses repo name, workflow path/ref/commit, and run links via Rekor the moment you sign (item 10)** — a one-way disclosure decision that should be made consciously, not accidentally via a copy-pasted public-good Cosign workflow.
7. **Merge queue probably won't work even once public, because this is a personal-account (not org) repo (item 1j).** Don't architect the promotion pipeline's "safe merge" step around it without first verifying availability in the live repo settings.
8. **tfsec is dead; `tfsec-action` hasn't been touched since Feb 2023 (item 5).** Low severity only because the fix is trivial — swap to `trivy config`/`trivy fs --scanners misconfig`, same AVD-* rule IDs.
