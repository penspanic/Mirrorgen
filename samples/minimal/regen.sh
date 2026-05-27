#!/usr/bin/env bash
# samples/minimal regeneration:
#   - `dotnet build` runs the Mirrorgen MSBuild target, emitting TS files
#     under client/src/_generated/ (mirrors the C# directory layout).
#   - The CLI captures C# fixtures from the just-built assembly.
#   - vitest cross-validates the two sides.
#
# Usage:
#   ./regen.sh
#   CONFIG=Release ./regen.sh

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SAMPLE_DIR="$ROOT/samples/minimal"

CONFIG="${CONFIG:-Debug}"
TFM="${TFM:-net8.0}"

CLI_PROJECT="$ROOT/src/Mirrorgen.Cli/Mirrorgen.Cli.csproj"
RULES_PROJECT="$SAMPLE_DIR/Rules/Rules.csproj"
RULES_DLL="$SAMPLE_DIR/Rules/bin/${CONFIG}/${TFM}/Mirrorgen.Samples.Minimal.Rules.dll"

OUT_DIR="$SAMPLE_DIR/client/src/_generated"
OUT_JSON="$OUT_DIR/Pricing.fixtures.json"

echo "[1/3] Building Rules.csproj (MSBuild target emits TS) ($CONFIG)..."
dotnet build "$CLI_PROJECT"   -c "$CONFIG" --nologo -v minimal
dotnet build "$RULES_PROJECT" -c "$CONFIG" --nologo -v minimal

echo "[2/3] Capturing C# fixtures -> $OUT_JSON"
mkdir -p "$OUT_DIR"
dotnet run --project "$CLI_PROJECT" -c "$CONFIG" --no-build -- \
    fixtures "$RULES_DLL" -o "$OUT_JSON"

echo "[3/3] Running vitest..."
cd "$SAMPLE_DIR/client"
if [ ! -d node_modules ]; then
    npm install --silent
fi
npm test

echo "Done."
