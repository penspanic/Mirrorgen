#!/usr/bin/env bash
# samples/minimal regeneration:
#   - `dotnet build` runs the Mirrorgen MSBuild target, emitting TS files
#     and the cross-test fixtures JSON under client/src/_generated/.
#   - vitest cross-validates the two sides.
#
# Usage:
#   ./regen.sh
#   CONFIG=Release ./regen.sh

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SAMPLE_DIR="$ROOT/samples/minimal"

CONFIG="${CONFIG:-Debug}"

RULES_PROJECT="$SAMPLE_DIR/Rules/Rules.csproj"

echo "[1/2] Building Rules.csproj (MSBuild target emits TS + fixtures) ($CONFIG)..."
dotnet build "$RULES_PROJECT" -c "$CONFIG" --nologo -v minimal

echo "[2/2] Running vitest..."
cd "$SAMPLE_DIR/client"
if [ ! -d node_modules ]; then
    npm install --silent
fi
npm test

echo "Done."
