---
title: "ADR-0006: Use the GitHub noreply commit identity and limit privacy exposure"
description: "Use the repository-local GitHub noreply identity for commits and rewrite the empty initial commit once so the public history does not keep a personal address in normal view."
status: accepted
date: 2026-10-04
decision-makers: ["@RostomOhannessian"]
supersedes: []
superseded-by: []
tools: ["gitleaks", "github-secret-scanning"]
related-adrs: ["0002", "0007"]
evidence:
  - docs/research/2026-10-04-decision-critical-addendum.md
---

# ADR-0006: Use the GitHub noreply commit identity and limit privacy exposure

## Context and problem statement

Phase 0 turns this repository into a public teaching and portfolio project. Plan finding F13 and the decision-critical
addendum record that the original empty initial commit used a personal address. Plan section 9 also states that logs
carry no personal data, test data is synthetic, and commits use a noreply identity.

Once the repository becomes public, commit metadata becomes part of the portfolio surface. Leaving a personal address in
the visible history would conflict with the privacy rule for this project and with the wider goal of making the
repository safe to share broadly.

At the same time, history rewriting is a real operational event. Even with an empty initial commit, a force-push must
be justified, time-bounded, and honest about what it cannot guarantee after publication.

## Decision drivers

- Keep public commit metadata aligned with the repository's privacy posture.
- Limit history rewriting to the smallest safe change that removes the immediate exposure.
- Keep the repository's commit identity portable and repository-specific, not a global workstation policy.
- Be honest about residual risk after a force-push.
- Pair identity cleanup with evidence-driven secret and privacy scanning.

## Considered options

1. Recreate the empty initial commit once under the noreply identity and force-push before publication
2. Keep the original empty commit
3. Delete and recreate the repository
4. Ask GitHub Support to purge history without rewriting locally

## Decision outcome

Chosen option: **Recreate the empty initial commit once under the noreply identity and force-push before publication**,
because it removes the visible personal address from normal repository history while staying proportionate to the scope
of the problem.

All commits in this repository use the GitHub noreply identity
`260491873+RostomOhannessian@users.noreply.github.com`. That identity is set as repository-local Git configuration, not
as a global workstation default, so other repositories do not inherit the policy accidentally.

The original empty initial commit is recreated once under the noreply identity, and the default branch is force-pushed
before public publication. The plan justifies this as safe because the history was empty and the remote had no other
branches, issues, or pull requests at review time.

This decision also depends on two account settings. GitHub creates merge, squash, and web-edit commits itself, and it
uses the account's primary email for them unless "Keep my email addresses private" is on, so that setting is a
prerequisite for every merge. "Block command line pushes that expose my email" rejects a push whose commits carry a
personal address, which protects against a mis-set local identity. WP0.7 verifies the first setting through the account
email API before the first merge.

The residual risk stays explicit. A replaced commit object can remain retrievable by its identifier on GitHub until
garbage collection, and a force-push can appear in repository activity. The stronger alternative of deleting and
recreating the repository was considered and rejected as disproportionate for an otherwise empty commit history.

### Consequences

- Good: the public commit history uses the intended noreply identity from the start.
- Good: the policy is local to this repository and therefore portable, reviewable, and less risky for other work.
- Bad: the rewrite cannot guarantee immediate disappearance of the replaced commit object; the mitigation is to do the
  rewrite before publication and to record the residual risk honestly.
- Bad: force-pushes are normally undesirable; the mitigation is to perform exactly one narrowly scoped rewrite for the
  empty initial commit and then rely on rulesets that disallow force-pushes.
- Neutral: privacy protection focuses on normal public surfaces and does not promise impossible retroactive erasure.

### Confirmation

WP0.1 confirms the repository-local identity setup and the one-time empty-commit rewrite before the repository becomes
public.

WP0.6 confirms the scanning posture through a full-history gitleaks run and the surrounding governance checks.

WP0.7 confirms the publication checklist, including the review of commit metadata, logs, and other privacy-sensitive
surfaces before final publication settings are turned on. The metadata review covers the author and committer email of
every commit on every remote ref, because GitHub creates merge and squash commits itself.

Execution record (WP0.1, 2026-10-04): the owner confirmed the rewrite. The empty initial commit was recreated under the
noreply identity and `master` was force-pushed once, using a lease on the exact commit being replaced. Afterward the
remote `master` matched the local branch and its commit author was the noreply identity. A check made immediately
afterward showed that the GitHub API still served the replaced commit by its identifier, which confirms the residual
risk described above. While the repository is private, only people with access to it can use that identifier.

WP0.7 asks the owner to confirm the visibility change with that check result in front of them. The alternatives in this
ADR (accept the residual risk, delete and recreate the repository, or ask GitHub Support for a purge) stay available
until the repository is made public.

Execution record (WP0.7, 2026-10-04): the commit-metadata check ran against the remote refs after the Phase 0 pull
request was merged through GitHub. It found a merge commit whose author email was the account's primary email, because
"Keep my email addresses private" was off (verified through the account email API), and the workflow run for that push
recorded the same author. Force-pushing a cleaned master could not have removed it, since GitHub keeps the pull
request's merge commit and the run metadata. The repository was still private, so nothing had been exposed.

The owner chose the third option above: delete and recreate the repository before publication. That also removed the
replaced initial commit from GitHub, and it replaces the earlier judgment that recreation was disproportionate, because
with one pull request and no Issues the cost was small. The work stayed in Git, so the same commits were pushed to the
new repository and the Phase 0 pull request was reopened there, after the account setting was turned on and verified.

## Pros and cons of the options

### Recreate the empty initial commit once under the noreply identity and force-push before publication

- Good: solves the visible-history problem with the smallest practical change.
- Good: consistent with the repository's privacy and publication goals.
- Bad: still leaves residual risk around cached objects and activity records.

### Keep the original empty commit

- Good: avoids history rewriting entirely.
- Bad: knowingly leaves public personal metadata in the most visible part of repository history.
- Bad: conflicts with the stated privacy posture in the plan.

### Delete and recreate the repository

- Good: strongest practical reset if exposure later proves unacceptable.
- Bad: disproportionate for an empty initial commit and disruptive to repository continuity.
- Bad: destroys continuity of existing metadata that Phase 0 is trying to establish.

### Ask GitHub Support to purge history without rewriting locally

- Good: could help if residual exposure remains after the rewrite.
- Bad: not a primary plan because it depends on an external escalation and may still require local cleanup work first.
- Bad: does not replace the need for the repository-local identity policy.

## Revisit when

- A later review shows that the replaced commit object remains meaningfully exposed after publication.
- The repository needs to import older history or another identity source that changes the privacy risk.
- GitHub changes its commit-privacy features or offers a stronger documented purge path that is proportionate here.

## Further reading

- Implementation plan v2: [../plans/implementation-plan.md](../plans/implementation-plan.md) (checked 2026-10-04)
- Decision-critical addendum: [../research/2026-10-04-decision-critical-addendum.md](../research/2026-10-04-decision-critical-addendum.md) (checked 2026-10-04)
- Repository visibility ADR: [./0002-repository-visibility-and-capability-strategy.md](./0002-repository-visibility-and-capability-strategy.md) (checked 2026-10-04)
