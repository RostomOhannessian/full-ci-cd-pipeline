---
title: "Runbook: Machine migration"
description: "Move the work to a new machine: clone, open the Dev Container, resume from the status page, and recreate the local environment with new secrets, without copying any local state."
audience: [maintainers, contributors]
tools: [dev-command, dev-containers, docker-compose, powershell]
introduced: "phase-1"
last-verified: 2026-10-10
owner: "@RostomOhannessian"
---

# Runbook: Machine migration

## When to use this

Use it when you start work on a new machine, when a disk is wiped, or when you hand the work to another contributor. The procedure is the resume protocol of [plan section 6](../plans/implementation-plan.md), written as steps
that you can rehearse. WP3.10 extends it for the local Kubernetes platform. Today it covers the Phase 1 environment: the Dev Container or the native setup, the `dev` command, and the API-only stack.

## Impact and risk

- **Blast radius:** the new machine only. The repository on GitHub is read and not changed, and the old machine keeps working until you retire it.
- **Reversible:** yes. Delete the new clone and its containers. Nothing here changes a shared system.
- **Needs confirmation from:** nobody, except for the last section, which revokes credentials on the old machine.

The rule that shapes everything below: **local state is disposable and is never copied.** The `.env` file, the `.dev` folder, Docker volumes, kubeconfig files, Vault keys, and Terraform state belong to one machine. A new machine
gets new secrets, and the procedure below makes them.

## Before you start

- A GitHub account with "Keep my email addresses private" turned on ([ADR-0006](../adr/0006-commit-identity-and-privacy.md)). Without it, GitHub uses your personal email for the commits that it creates.
- On the new machine: Git and Docker, plus either an editor that supports Dev Containers or the .NET SDK 10.0.401 ([native setup](../tutorials/native-setup.md)).
- Your GitHub noreply address, which is on the GitHub email settings page, in the form `<id>+<login>@users.noreply.github.com`.

## Procedure

1. Clone the repository, and fetch every branch.

   ```bash illustrative
   git clone https://github.com/RostomOhannessian/full-ci-cd-pipeline.git
   cd full-ci-cd-pipeline
   git fetch origin
   ```

   Expected output: the clone completes, and `git branch --remotes` lists `origin/master` and the phase branches.

2. Set the commit identity for this clone. Replace the placeholders.

   ```bash illustrative
   git config --local user.email "<id>+<login>@users.noreply.github.com"
   git config --local user.name "<your name>"
   ```

3. Open the Dev Container (VS Code: **Reopen in Container**), or use the native setup. The Dev Container runs `dev bootstrap` and `dev doctor` after it is created.

4. Read [the status page](../project/STATUS.md), at the section **Resume here**. It names the active branch, the last completed work, and the next action. Check out the branch.

   ```bash illustrative
   git switch <branch from the status page>
   ```

5. Install the pinned tools and create `.env` with new secrets. In a Dev Container this ran already, and running it again changes nothing.

   ```bash illustrative
   ./dev bootstrap
   ```

   Expected output: `.env created with 10 generated secret(s)`, or `.env is already complete` if the Dev Container made it.

6. Check the machine.

   ```bash illustrative
   ./dev doctor
   ```

   Expected output: `0 failure(s)`. Fix each failure with the command that doctor prints beside it.

7. Recreate the local environment. This starts the API-only stack and checks it.

   ```bash illustrative
   ./dev compose up
   ./dev compose check
   ```

   Expected output: five `[ok]` lines and `compose check: 5 ok, 0 warning(s), 0 failure(s)`. The first start pulls about 3 GB of images.

## Verify

1. The status files agree with each other.

   ```bash illustrative
   dotnet run --project tools/Governance.Auditor -- status validate
   ```

   Expected output: the check passes.

2. The commit identity is a noreply address. The command prints one address.

   ```bash illustrative
   git config --local user.email
   ```

3. The working tree is clean, and the branch is the one that the status page names.

   ```bash illustrative
   git status --short --branch
   ```

## Roll back

Delete the clone, and run `./dev compose reset` first if the stack is up, so that its containers and volumes go too. Nothing else on the new machine or on GitHub changed.

## After the procedure

- Retire the old machine when the new one works. Delete its clone, run `./dev compose reset` there to remove the stack's volumes, and delete its `.env`. Sign out of the GitHub CLI with `gh auth logout`, and remove any
  token that only that machine used. Do this only after you have pushed every branch that you want to keep.
- Record the move in an Issue if it exposed a gap in this runbook, and fix the runbook in the same pull request.

## What never moves

| Item | Why it stays behind | What replaces it |
| --- | --- | --- |
| `.env` | It holds this machine's random secrets | `./dev bootstrap` generates a new file |
| `.dev` | It holds downloaded tools | `./dev bootstrap` installs them again, after a checksum check |
| Docker volumes and images | They hold disposable data, and the images are pulled again | `./dev compose up` |
| Kubeconfig files, Vault unseal keys and root tokens, and Terraform state | They arrive with the Phase 3 platform, and each one grants access | Phase 3 regenerates them, and WP3.10 extends this runbook |

## Rehearsal log

| Date | Who | Environment | Result | Changes made to this runbook |
| --- | --- | --- | --- | --- |
| 2026-10-10 | The WP1.4 author | Windows x86-64 and a fresh Dev Container built with the Dev Containers CLI | Pass for steps 3 to 7: the container built, `dev bootstrap` and `dev doctor` ran in it, and Lab 1 started and checked the stack ([evidence](../testing/evidence/2026-10-10-dev-container-quick-start.md)). Steps 1, 2, and 4 were rehearsed on 2026-10-08 ([fresh clone resume](../testing/evidence/2026-10-08-fresh-clone-resume.md)). Nobody has run the whole procedure on a second physical machine yet | First version |
