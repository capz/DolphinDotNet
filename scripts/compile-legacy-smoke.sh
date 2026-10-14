#!/usr/bin/env sh
set -eu
ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
dotnet build "$ROOT/tests/LegacyHelloSmoke/LegacyHelloSmoke.csproj" -c Release
dotnet run --project "$ROOT/tools/DolphinDotNet.Compiler/DolphinDotNet.Compiler.csproj" -- \
  "$ROOT/tests/LegacyHelloSmoke/bin/Release/net8.0/LegacyHelloSmoke.dll" \
  "$ROOT/generated/generated_program.h"
