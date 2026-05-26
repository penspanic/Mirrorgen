#!/usr/bin/env bash
# Regenerate the cross-validation artifacts under runtime-ts/cross/:
#   - subject.ts            (TS emit from cross-fixtures/Subject.cs)
#   - subject.fixtures.json (C# invocation results for cross-test)
#
# Run from anywhere; paths are resolved relative to the repo root.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

CONFIG="${CONFIG:-Debug}"
TFM="${TFM:-net8.0}"

CLI_PROJECT="src/Mirrorgen.Cli/Mirrorgen.Cli.csproj"
SUBJECT_PROJECT="cross-fixtures/Mirrorgen.CrossFixtures.csproj"
SUBJECT_SOURCE="cross-fixtures/Subject.cs"
SUBJECT_DLL="cross-fixtures/bin/${CONFIG}/${TFM}/Mirrorgen.CrossFixtures.dll"

OUT_TS="runtime-ts/cross/subject.ts"
OUT_JSON="runtime-ts/cross/subject.fixtures.json"

echo "[1/3] Building CLI + subject assembly ($CONFIG)..."
dotnet build "$CLI_PROJECT"     -c "$CONFIG" --nologo -v minimal
dotnet build "$SUBJECT_PROJECT" -c "$CONFIG" --nologo -v minimal

mkdir -p "$(dirname "$OUT_TS")"

echo "[2/3] Emitting TS -> $OUT_TS"
dotnet run --project "$CLI_PROJECT" -c "$CONFIG" --no-build -- \
    transpile "$SUBJECT_SOURCE" -o "$OUT_TS"

echo "[3/3] Capturing C# fixtures -> $OUT_JSON"
dotnet run --project "$CLI_PROJECT" -c "$CONFIG" --no-build -- \
    fixtures "$SUBJECT_DLL" -o "$OUT_JSON"

echo "Done. Run 'npm test --prefix runtime-ts' to validate."
