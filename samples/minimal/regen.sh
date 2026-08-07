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

echo "[1/3] Building Rules.csproj (MSBuild target emits TS + fixtures) ($CONFIG)..."
dotnet build "$RULES_PROJECT" -c "$CONFIG" --nologo -v minimal

echo "[2/3] Typechecking the emit (noUncheckedIndexedAccess is on)..."
cd "$SAMPLE_DIR/client"
if [ ! -d node_modules ]; then
    npm install --silent
fi
npm run --silent typecheck

echo "[3/3] Running vitest..."
npm test

echo "Done."
