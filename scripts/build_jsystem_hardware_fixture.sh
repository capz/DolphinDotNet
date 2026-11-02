#!/usr/bin/env bash
set -euo pipefail
# Build a fresh DOL using the repository's verified GameCube toolchain.
# Usage: ./scripts/build_jsystem_hardware_fixture.sh usb 0
#    or: ./scripts/build_jsystem_hardware_fixture.sh tcp IP MASK GATEWAY [PORT]
mode=${1:-usb}
out=${DND_JSYSTEM_FIXTURE_OUT:-out/jsystem-hardware-fixture}
dotnet build tests/JSystemHardwareFixture/JSystemHardwareFixture.csproj -c Debug
dotnet build tools/DolphinDotNet.Compiler/DolphinDotNet.Compiler.csproj -c Release
dll=tests/JSystemHardwareFixture/bin/Debug/net8.0/JSystemHardwareFixture.dll
compiler=tools/DolphinDotNet.Compiler/bin/Release/net8.0/DolphinDotNet.Compiler.dll
case "$mode" in
  usb)
    channel=${2:-0}
    [[ "$channel" == 0 || "$channel" == 1 ]] || { echo "USB channel must be 0 or 1" >&2; exit 2; }
    dotnet "$compiler" --bundle "$dll" "$out" --platform gamecube --debug-symbols --gdb-usb "$channel"
    ;;
  tcp)
    [[ $# -ge 4 ]] || { echo "Expected tcp IP MASK GATEWAY [PORT]" >&2; exit 2; }
    dotnet "$compiler" --bundle "$dll" "$out" --platform gamecube --debug-symbols \
      --gdb-tcp "$2" --gdb-netmask "$3" --gdb-gateway "$4" --gdb-port "${5:-2828}"
    ;;
  *) echo "Usage: $0 usb [channel] | tcp IP MASK GATEWAY [PORT]" >&2; exit 2;;
esac
make -C "$out"
echo "Fixture DOL and ELF: $out/Game.dol / $out/Game.elf"
