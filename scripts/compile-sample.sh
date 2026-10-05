#!/usr/bin/env sh
set -eu
ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
dotnet build "$ROOT/samples/HelloGameCube/HelloGameCube.csproj" -c Release
dotnet run --project "$ROOT/tools/DolphinDotNet.Compiler/DolphinDotNet.Compiler.csproj" -- \
  "$ROOT/samples/HelloGameCube/bin/Release/net8.0/HelloGameCube.dll" \
  "$ROOT/generated/generated_program.h"
