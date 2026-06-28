#!/usr/bin/env bash
# Assembles the community template catalog from the repo's templates/ folder. Each templates/*.json is
# one entry { name, description, category, doc }; we just collect them into the catalog the app fetches.
# Usage: scripts/package-templates.sh
set -euo pipefail

OUT="out/plugins"
mkdir -p "$OUT"

jq -s '{generated: (now | todateiso8601), templates: .}' templates/*.json > "$OUT/templates-catalog.json"

count="$(jq '.templates | length' "$OUT/templates-catalog.json")"
echo "Wrote $OUT/templates-catalog.json ($count template(s))"
