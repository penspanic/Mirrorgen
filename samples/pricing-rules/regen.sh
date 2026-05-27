#!/usr/bin/env bash
# samples/pricing-rules regeneration: build emits TS + fixtures via MSBuild,
# vitest cross-validates the two sides.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SAMPLE_DIR="$ROOT/samples/pricing-rules"

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
