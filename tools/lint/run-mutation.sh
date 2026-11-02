#!/bin/bash
# Measures how well the tests of the domain model and of the application layer catch changes to the code, with Stryker.NET (WP1.6, WP1.7, ADR-0029). It runs inside the .NET SDK
# container, where tools/lint/compose.yaml mounts the repository read-only and a folder for the reports (service "mutation"). The argument names the layer, and the default is the domain:
#
#   docker compose -f tools/lint/compose.yaml run --rm mutation
#   docker compose -f tools/lint/compose.yaml run --rm mutation application
#
# Stryker changes the source files of the project that it tests, so it never touches the mounted repository. The script copies the repository to
# a scratch directory first, restores it in locked mode as CI does, runs Stryker there, and leaves the reports in the mounted folder, which is
# TestResults/mutation/<layer>. The score is reported and is not a gate yet, because the Microsoft.Testing.Platform runner of Stryker is a preview (ADR-0018).
# Run it in Linux: Stryker cannot start on a Windows host that enforces Smart App Control, because many of its files are not signed (spike 1.c).
set -euo pipefail

work=/work
reports=/reports
target="${1:-domain}"

case "$target" in
  domain)
    project_dir=tests/Catalog.Domain.Tests
    layer=Catalog.Domain
    ;;
  application)
    project_dir=tests/Catalog.Application.Tests
    layer=Catalog.Application
    ;;
  *)
    echo "error: the layer must be 'domain' or 'application', not '$target'." >&2
    exit 1
    ;;
esac

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

cd "$project_dir"
echo "== Running Stryker.NET on $layer"
dotnet dotnet-stryker --config-file stryker-config.json --output "$reports/$target"

echo "== Reports"
find "$reports" -type f \( -name 'mutation-report.json' -o -name 'mutation-report.md' \) | sort
