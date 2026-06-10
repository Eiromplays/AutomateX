#!/usr/bin/env bash
# Packages every first-party plugin as a zip + generates catalog.json.
# Usage: scripts/package-plugins.sh v2.8.0
set -euo pipefail

TAG="${1:?usage: package-plugins.sh <tag>}"
VERSION="${TAG#v}"
REPO="${GITHUB_REPOSITORY:-Eiromplays/AutomateX}"
OUT="out/plugins"
rm -rf "$OUT"
mkdir -p "$OUT"

declare -A DESCRIPTIONS=(
  [AutomateX.Plugins.Ssh]="Run commands on remote hosts over SSH (password/key auth, host-key pinning)."
  [AutomateX.Plugins.Matrix]="Send Matrix room messages with retry-safe transaction ids."
  [AutomateX.Plugins.Llm]="Prompt any OpenAI-compatible LLM endpoint (OpenAI, OpenRouter, Ollama, local)."
  [AutomateX.Plugins.Feed]="Watch RSS/Atom feeds or any URL and fire workflows on new items (durable dedup)."
  [AutomateX.Plugins.Discord]="Post messages to a Discord channel webhook."
  [AutomateX.Plugins.Pushover]="Send mobile push notifications via Pushover."
)

ENTRIES=()
for project in src/Plugins/*/; do
  name="$(basename "$project")"
  echo "Packaging $name…"
  dotnet publish "$project" -c Release -o "$OUT/stage/$name" >/dev/null
  (cd "$OUT/stage/$name" && zip -qr "../../$name.zip" .)
  sha="$(sha256sum "$OUT/$name.zip" | cut -d' ' -f1)"
  ENTRIES+=("{\"name\":\"$name\",\"version\":\"$VERSION\",\"description\":\"${DESCRIPTIONS[$name]:-}\",\"url\":\"https://github.com/$REPO/releases/download/$TAG/$name.zip\",\"sha256\":\"$sha\"}")
done

printf '{"generated":"%s","plugins":[%s]}\n' \
  "$(date -u +%FT%TZ)" \
  "$(IFS=,; echo "${ENTRIES[*]}")" > "$OUT/catalog.json"

rm -rf "$OUT/stage"
echo "Wrote $OUT/catalog.json + $(ls "$OUT"/*.zip | wc -l) zips"
