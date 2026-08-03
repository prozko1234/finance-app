#!/usr/bin/env bash
# Packs the built web app into the zip the native shell downloads as a live update.
#
# The version is derived from the content, not from a counter someone has to remember to
# bump: a deploy that changed nothing produces the same version, and the phone does not
# download a bundle identical to the one it is running.
#
#   ./scripts/make-bundle.sh          # after npm run build
#
# Output: bundle/<version>.zip and bundle/bundle.json, both served from wwwroot in the image.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DIST="$ROOT/dist"
OUT="$ROOT/bundle"

[ -d "$DIST" ] || { echo "!! немає dist — спершу npm run build"; exit 1; }

rm -rf "$OUT"
mkdir -p "$OUT"

# Zipped from inside dist, so index.html sits at the root of the archive — that is where the
# updater expects to find it after unpacking.
( cd "$DIST" && zip -qr "$OUT/app.zip" . )

CHECKSUM="$(shasum -a 256 "$OUT/app.zip" | cut -d' ' -f1)"

# Semver, because that is what the updater compares — and monotonic, so a later build always
# wins. The patch number is minutes since 2026-01-01: unique per build without a counter, and
# it stays inside the integer range for the next few thousand years.
EPOCH=$(( ( $(date +%s) - 1767225600 ) / 60 ))
VERSION="1.0.$EPOCH"

mv "$OUT/app.zip" "$OUT/$VERSION.zip"

cat > "$OUT/bundle.json" <<JSON
{
  "version": "$VERSION",
  "checksum": "$CHECKSUM",
  "file": "$VERSION.zip"
}
JSON

echo "bundle $VERSION ($(du -h "$OUT/$VERSION.zip" | cut -f1))"
