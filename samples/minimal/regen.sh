#!/usr/bin/env bash
# samples/minimal regeneration: emits TS + fixtures, then runs vitest.
#
# Usage:
#   ./regen.sh           # full build + emit + vitest run
#   CONFIG=Release ./regen.sh
#
# Outputs (committed alongside the sample):
#   client/src/_generated/rules.ts
#   client/src/_generated/rules.fixtures.json

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SAMPLE_DIR="$ROOT/samples/minimal"

CONFIG="${CONFIG:-Debug}"
TFM="${TFM:-net8.0}"

CLI_PROJECT="$ROOT/src/Mirrorgen.Cli/Mirrorgen.Cli.csproj"
RULES_PROJECT="$SAMPLE_DIR/Rules/Rules.csproj"
RULES_SOURCE="$SAMPLE_DIR/Rules/Pricing.cs"
RULES_DLL="$SAMPLE_DIR/Rules/bin/${CONFIG}/${TFM}/Mirrorgen.Samples.Minimal.Rules.dll"

OUT_DIR="$SAMPLE_DIR/client/src/_generated"
OUT_TS="$OUT_DIR/rules.ts"
OUT_JSON="$OUT_DIR/rules.fixtures.json"

echo "[1/4] Building CLI + sample Rules.csproj ($CONFIG)..."
dotnet build "$CLI_PROJECT"   -c "$CONFIG" --nologo -v minimal
dotnet build "$RULES_PROJECT" -c "$CONFIG" --nologo -v minimal

mkdir -p "$OUT_DIR"

echo "[2/4] Emitting TS -> $OUT_TS"
dotnet run --project "$CLI_PROJECT" -c "$CONFIG" --no-build -- \
    transpile "$RULES_SOURCE" -o "$OUT_TS"

echo "[3/4] Capturing C# fixtures -> $OUT_JSON"
dotnet run --project "$CLI_PROJECT" -c "$CONFIG" --no-build -- \
    fixtures "$RULES_DLL" -o "$OUT_JSON"

echo "[4/4] Running vitest..."
cd "$SAMPLE_DIR/client"
if [ ! -d node_modules ]; then
    npm install --silent
fi
npm test

echo "Done."
