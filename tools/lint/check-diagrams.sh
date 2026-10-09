#!/bin/sh
# Renders every Mermaid diagram in the Markdown files under the current directory, so a diagram with a syntax error fails the check.
# It runs inside the mermaid-cli container, where tools/lint/compose.yaml mounts the repository read-only (service "diagrams"):
#
#   docker compose -f tools/lint/compose.yaml run --rm diagrams
#
# The rendered files go to a temporary directory in the container and are discarded. Accessible titles and descriptions are checked by the
# Documentation.Auditor, because mermaid-cli accepts a diagram that has none.
set -u

mmdc=/home/mermaidcli/node_modules/.bin/mmdc
output=/tmp/diagrams
list="$output/files.txt"
failed=0
checked=0

mkdir -p "$output"

# Templates hold placeholders, and dependency or build folders hold no documentation. The image's grep is BusyBox, which has no
# --include or --exclude-dir options, so find selects the files and grep tests each one.
find . \( -name .git -o -name node_modules -o -name templates \) -prune -o -type f -name '*.md' -print | sort | while IFS= read -r candidate; do
  if grep -q '^[[:space:]]*```mermaid' "$candidate"; then
    echo "$candidate"
  fi
done > "$list"

while IFS= read -r file; do
  checked=$((checked + 1))
  name=$(printf '%s' "$file" | tr '/.' '__')
  echo "== $file"

  if ! "$mmdc" --puppeteerConfigFile /puppeteer-config.json --quiet --input "$file" --output "$output/$name.md"; then
    echo "error: a diagram in $file does not render." >&2
    failed=$((failed + 1))
  fi
done < "$list"

if [ "$checked" -eq 0 ]; then
  echo "error: no Markdown file with a Mermaid diagram was found, so the check proved nothing." >&2
  exit 1
fi

echo "Mermaid: $checked files checked, $failed failed."
[ "$failed" -eq 0 ]
