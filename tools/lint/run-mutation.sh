#!/bin/bash
# Measures how well the tests of the domain model catch changes to the model, with Stryker.NET (WP1.6, ADR-0029). It runs inside the .NET SDK container,
# where tools/lint/compose.yaml mounts the repository read-only and a folder for the reports (service "mutation"):
#
#   docker compose -f tools/lint/compose.yaml run --rm mutation
#
# Stryker changes the source files of the project that it tests, so it never touches the mounted repository. The script copies the repository to
# a scratch directory first, restores it in locked mode as CI does, runs Stryker there, and leaves the reports in the mounted folder, which is
# TestResults/mutation. The score is reported and is not a gate yet, because the Microsoft.Testing.Platform runner of Stryker is a preview (ADR-0018).
# Run it in Linux: Stryker cannot start on a Windows host that enforces Smart App Control, because many of its files are not signed (spike 1.c).
set -euo pipefail

work=/work
reports=/reports

if [ ! -f /repo/ProductCatalog.slnx ]; then
  echo "error: the repository is not mounted at /repo, so there is nothing to test." >&2
  exit 1
fi

mkdir -p "$work" "$reports"
echo "== Copying the repository to a scratch directory"
tar -C /repo \
  --exclude=./.git --exclude=./.dev --exclude=./.env --exclude=./TestResults --exclude=./_site --exclude=./.docfx --exclude=./node_modules \
  --exclude='*/bin' --exclude='*/obj' \
  -cf - . | tar -C "$work" -xf -

cd "$work"
echo "== Restoring the tools and the solution"
dotnet tool restore
dotnet restore ProductCatalog.slnx

cd tests/Catalog.Domain.Tests
echo "== Running Stryker.NET on Catalog.Domain"
dotnet dotnet-stryker --config-file stryker-config.json --output "$reports"

echo "== Reports"
find "$reports" -type f \( -name 'mutation-report.json' -o -name 'mutation-report.md' \) | sort
