#!/bin/bash
# Runs the whole test suite with coverage on Linux, in the pinned .NET SDK container (WP1.8, ADR-0034). It runs inside the container that tools/lint/compose.yaml defines (service "linux-tests"),
# where the repository is mounted read-only and the Docker socket is mounted so that Testcontainers can start the SQL Server container (REQ-QUA-001). Run it from the repository root:
#
#   docker compose -f tools/lint/compose.yaml run --rm linux-tests
#
# The script copies the repository to a scratch directory first, because a build writes next to the sources, restores in locked mode as CI does, builds in Release with warnings as errors, and runs
# every test project with the code coverage extension on. The coverage extension changes the assemblies that it instruments, and it adds a tracker type to each one on Linux, so a run without coverage
# hides a defect that CI would find (tests/Catalog.Architecture.Tests/coverage.settings.xml explains it). The reports go to the mounted folder TestResults/linux.
#
# The Docker socket gives the container control of the Docker daemon of the host, which is the trust boundary that the threat model and ADR-0034 name. The service is for a developer machine and is never
# a service of a CI job or of the Compose stack. The hosted CI job does not need it, because the runner has Docker and runs the suite directly.
set -euo pipefail

work=/work
reports=/reports

if [ ! -f /repo/ProductCatalog.slnx ]; then
  echo "error: the repository is not mounted at /repo, so there is nothing to test." >&2
  exit 1
fi

if [ ! -S /var/run/docker.sock ]; then
  echo "error: the Docker socket is not mounted at /var/run/docker.sock, so Testcontainers cannot start SQL Server." >&2
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

echo "== Building in Release"
dotnet build ProductCatalog.slnx --configuration Release --no-restore

echo "== Running the tests with coverage"
dotnet test --solution ProductCatalog.slnx --configuration Release --no-build --coverage --coverage-output-format cobertura --report-trx --results-directory TestResults

echo "== Checking the coverage thresholds"
dotnet pwsh -NoProfile -File tools/ci/Test-CoverageThresholds.ps1 -CoverageFile 'TestResults/*.cobertura.xml'

echo "== Reports"
rm -rf "${reports:?}"/*
cp -r TestResults/. "$reports"/
find "$reports" -type f \( -name '*.trx' -o -name '*.cobertura.xml' \) | sort
