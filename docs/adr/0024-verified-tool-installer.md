---
title: "ADR-0024: Install developer tools with a verified PowerShell installer, and leave mise on hold"
description: "Pin each developer tool in tools/versions.yaml with its SHA-256 and install it with a small PowerShell module that ships with the repository, because the native prerequisites must stay Git, Docker, and the .NET SDK, and a download must match what a reviewer approved."
status: accepted
date: 2026-10-10
decision-makers: ["@RostomOhannessian"]
supersedes: []
superseded-by: []
tools: [dev-command, powershell, pester, psscriptanalyzer, mise]
related-adrs: ["0004", "0018", "0019", "0020", "0025"]
evidence: []
---

# ADR-0024: Install developer tools with a verified PowerShell installer, and leave mise on hold

## Context and problem statement

Plan section 12 says that developer tools install "from `tools/versions.yaml` with verification" and that the WP1.4 ADR chooses between `mise` with a
lockfile and a custom installer. The criteria it names are Windows support, checksum and signature verification, lockfile support, and how easily the
approach can be taught. `REQ-DX-002` adds a hard limit: the native setup requires only Git, Docker, and the .NET 10 SDK.

Two things need installing today. `scripts/Invoke-ScriptTests.ps1` runs Pester and PSScriptAnalyzer, and the CI runner image happens to ship both,
while a contributor's machine may not, or may have other versions. Phase 3 adds more tools (kind, kubectl, Terraform, Helm, Chainsaw, and the Kyverno
CLI, according to the Phase 3 plan), and Phase 2 adds more.

Facts that shape the choice (checked 2026-10-10):

- `mise` runs on Windows, Linux, and macOS. Its installation page recommends winget on Windows and an install script elsewhere, and it offers a GPG
  signature for the script ([installing mise](https://mise.jdx.dev/installing-mise.html)).
- `mise.lock` records the exact versions, download URLs, and checksums "where the backend provides them", and `mise install --locked` installs what the
  file records ([the lockfile page](https://mise.jdx.dev/dev-tools/mise-lock.html)).
- The PowerShell Gallery serves each module version as a `.nupkg` at a stable address, and publishes the SHA-512 of each package. The packages of Pester 5.9.0 and
  PSScriptAnalyzer 1.25.0 matched the published SHA-512 when they were downloaded on 2026-10-10.
- PowerShell 7.6.6 is available as a .NET tool on nuget.org under the MIT license, and the repository already restores local tools with a required repository
  signature ([ADR-0020](0020-dependency-intake-controls.md)).
- PowerShell 7 has no YAML parser, and a parser module would be one more thing to pin before the installer that pins it could run.

## Decision drivers

- The native prerequisites stay Git, Docker, and the .NET SDK (`REQ-DX-002`).
- A download must match what a reviewer approved, so a changed or tampered file is refused and a new version is a reviewed change.
- The same installer runs on a contributor's machine, in the Dev Container, and in CI.
- The mechanism is small enough to read in one sitting, because the plan teaches it.
- It can grow to the binary tools that Phases 2 and 3 need.

## Considered options

1. A custom installer in PowerShell, driven by `tools/versions.yaml` with a pinned SHA-256 for each download (chosen).
2. `mise` with a committed `mise.lock`.
3. Install the tools in the Dev Container image only, with no installer for native setups.
4. Rely on the CI runner image and on whatever a contributor has installed.
5. Use `Install-PSResource` or `Install-Module` with a pinned version and no checksum.

## Decision outcome

Chosen option: **a custom installer in PowerShell**, because it adds no fourth native prerequisite, it checks every download against a checksum that lives in the
repository, and it is small. `mise` is put on hold and not rejected: it is a good tool, and the first revisit trigger below is likely to arrive.

### The manifest

`tools/versions.yaml` lists one tool per entry with an `id`, a `kind`, a module `name`, an exact `version`, the download `url`, and the `sha256` of the file.
It is a strict subset of YAML that the installer reads itself (`scripts/modules/Dev.Yaml.psm1`) and that the documentation auditor reads with its full YAML
library, so the two readers agree. The installer refuses tabs, flow collections, anchors, aliases, tags, block scalars, merge keys, and duplicate keys,
and it names the file and the line.

### The installer

- **One kind today.** `powershell-module` installs a module from the PowerShell Gallery. The manifest reader accepts a download URL only if it is the Gallery's own
  address for the name and the version in the same entry, and a `kind` that the installer does not know stops the read. The documentation auditor treats an
  unknown kind the same way, so a new kind of tool needs its installer and its scanner in one change.
- **Verification before use.** The installer downloads to a temporary folder, compares the SHA-256 with the pin, extracts with a check that no entry leaves the folder,
  and confirms that the module manifest declares the pinned version. Any failure removes the temporary folder and leaves the install folder as it was.
- **A marker.** An installed tool carries a marker with the SHA-256 that it was installed from. A folder without it, or with a different pin, counts as missing, so a
  repin installs again and a copied-in folder is not trusted.
- **Where it installs.** Into `.dev/modules`, inside the repository and ignored by Git. `dev` puts that folder first on `PSModulePath`, so the pinned module wins over any
  other copy on the machine.
- **Updating a pin.** A maintainer downloads the new package, compares its SHA-512 with the Gallery's published hash, computes the SHA-256, and edits the version, the URL, and
  the SHA-256 together with the inventory entry. Dependabot cannot do this, so the weekly review of upstream releases stays a manual task until a scheduled check exists.

### Versions

Pester stays on **5.9.0** and PSScriptAnalyzer on **1.25.0**. The tests are written against Pester 5 and 5.9.0 is what the runner image ships. Pester 6.2.0 is the current release
(published 2026-09-09), and moving to a new major version is a reviewed change of its own. PowerShell is pinned at 7.6.6 as a local .NET tool in `.config/dotnet-tools.json`.

### Consequences

- Good: the prerequisites are Git, Docker, and the .NET SDK, and `dotnet tool restore` supplies PowerShell.
- Good: a tampered download fails with both checksums in the message, and the tests show it (`scripts/tests/Dev.Tools.Tests.ps1`).
- Good: CI and a contributor run the same pinned versions, and `dev doctor` says when they do not.
- Bad: the repository maintains an installer and a YAML reader. Mitigation: both are small, they have 48 tests, and the reader's limits are enforced and tested.
- Bad: updating a pin is manual, and a stale pin is a quiet risk. Mitigation: the inventory records each version with its date, and the freshness check flags an entry that is older
  than 180 days.
- Bad: a Gallery checksum protects against corruption and tampering in transit, and it does not prove who built the package. Mitigation: the pin is reviewed in a pull request, the
  SHA-512 is cross-checked with the Gallery at pin time, and the PowerShell Gallery requires an authenticated publisher. Authenticode signatures are not checked, because they are not
  portable to Linux and macOS.
- Neutral: Phases 2 and 3 add a second kind, a downloaded archive for a binary tool, with one URL and one SHA-256 for each platform.

### Confirmation

- **WP1.4** delivers `tools/versions.yaml`, `scripts/modules/Dev.Tools.psm1`, `scripts/modules/Dev.Yaml.psm1`, their tests, and the documentation auditor scanner for the manifest.
- The Pester tests run an install from a fake package, a checksum mismatch, a package that tries to leave the folder, a package with the wrong version, and a repin.
- `dev doctor` reports a tool that is missing or not installed as pinned.
- **Phase 3** (WP3.0) adds the archive kind and the first binary tools, and revisits `mise` with real numbers.

## Pros and cons of the options

### A custom installer in PowerShell

- Good: no new prerequisite, and a checksum in the repository for every download.
- Good: small, readable, and tested like the rest of the scripts.
- Bad: it is code to maintain, and it reads only the kinds that it implements.

### mise with a committed lockfile

- Good: many tools through one mechanism, with a lockfile, and on every platform.
- Bad: a fourth native prerequisite, installed through a different mechanism on each platform, which `REQ-DX-002` forbids.
- Bad: the checksum depends on what each backend provides, and the lock file is a second source of versions beside the .NET and Compose pins.

### Tools in the Dev Container image only

- Good: nothing to install in the container.
- Bad: a native setup would have no way to get the tools, and the plan documents a native path.

### Rely on the runner image and the contributor's machine

- Good: nothing to build.
- Bad: a different version on each machine gives a different answer to the same test, which breaks `REQ-QUA-004`.

### Install-PSResource or Install-Module with a pinned version

- Good: no code of our own.
- Bad: no checksum comparison, so nothing in the repository proves that the package is the one that was reviewed. A version pin selects a version, and it does not check the file.

## Revisit when

- Phase 3 needs more than three or four binary tools on every platform, which would argue for `mise` or for a general archive installer.
- The PowerShell Gallery stops publishing a package hash, or Authenticode verification becomes portable.
- Pester 6 or a later PSScriptAnalyzer release gives a reason to move the pins.
- Contributors report that a manual pin update is too slow, which would argue for a scheduled check that opens a pull request.

## Further reading

- [Installing mise](https://mise.jdx.dev/installing-mise.html) (checked 2026-10-10)
- [The mise lockfile](https://mise.jdx.dev/dev-tools/mise-lock.html) (checked 2026-10-10)
- [PowerShell Gallery package hashes](https://learn.microsoft.com/en-us/powershell/gallery/how-to/working-with-packages/manual-download) (checked 2026-10-10)
- [ADR-0020: Control dependency intake](0020-dependency-intake-controls.md) for the rules that the installer extends to developer tools
- [ADR-0025: The dev command and the Dev Container](0025-dev-command-and-dev-container.md)
