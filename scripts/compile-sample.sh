#!/usr/bin/env sh
set -eu
ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
dotnet build "$ROOT/samples/HelloGameCube/HelloGameCube.csproj" -c Release -p:DndBuildNative=false
if [ -n "${DND_COMPILER_DLL:-}" ]; then
  set -- dotnet "$DND_COMPILER_DLL" --bundle "$ROOT/samples/HelloGameCube/bin/Release/net8.0/HelloGameCube.dll" "${1:-$ROOT/out/HelloGameCube}" --platform gamecube
  exec "$@"
fi
dotnet run --project "$ROOT/tools/DolphinDotNet.Compiler/DolphinDotNet.Compiler.csproj" -- \
  --bundle \
  "$ROOT/samples/HelloGameCube/bin/Release/net8.0/HelloGameCube.dll" \
  "${1:-$ROOT/out/HelloGameCube}" --platform gamecube
