#!/usr/bin/env bash
set -euo pipefail
# Rebuild the complete EXI subsystem from our vendored, minimally corrected
# libogc2 source. Do not modify the SDK archive or post-process the ELF.
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SDK="${DEVKITPRO:?}/libogc2/gamecube"
PPC="${DEVKITPPC:?}/bin"
UPSTREAM_REV=094b250
mkdir -p "$ROOT/build/exi-overlay" "$ROOT/build/exi-upstream"
if [ ! -d "$ROOT/build/exi-upstream/.git" ]; then
  git clone --quiet --filter=blob:none https://github.com/extremscorner/libogc2.git "$ROOT/build/exi-upstream"
fi
git -C "$ROOT/build/exi-upstream" fetch --quiet --depth=100 origin master || git -C "$ROOT/build/exi-upstream" fetch --quiet origin
 git -C "$ROOT/build/exi-upstream" checkout --quiet --detach "$UPSTREAM_REV"
UP="$ROOT/build/exi-upstream"
# Verify our source is exactly the pinned upstream EXI implementation,
# except for the documented register correction and attribution.
python3 "$ROOT/scripts/verify_exi_source.py" "$UP/libogc/exi.c" "$ROOT/vendor/libogc2/exi.c"
"$PPC/powerpc-eabi-gcc" -c "$ROOT/vendor/libogc2/exi.c" \
  -o "$ROOT/build/exi-overlay/exi.o" \
  -DLIBOGC_INTERNAL -DGEKKO -DHW_DOL -O2 -g \
  -mcpu=750 -meabi -msdata=sysv -mhard-float \
  -ffunction-sections -fdata-sections -fno-strict-aliasing \
  -Wall -Wno-address-of-packed-member -Wno-prio-ctor-dtor \
  -I"$UP" -I"$UP/libogc" -I"$UP/include" -I"$UP/include/ogc" \
  -I"$UP/include/ogc/machine" -I"$UP/include/sdcard" \
  -I"$SDK/include" -I"$SDK/include/ogc" \
  -I"$ROOT/build/exi-upstream/lwip/include"
cp "$SDK/lib/libogc.a" "$ROOT/build/exi-overlay/libogc.a"
"$PPC/powerpc-eabi-ar" d "$ROOT/build/exi-overlay/libogc.a" exi.o
"$PPC/powerpc-eabi-ar" r "$ROOT/build/exi-overlay/libogc.a" "$ROOT/build/exi-overlay/exi.o"
"$PPC/powerpc-eabi-ranlib" "$ROOT/build/exi-overlay/libogc.a"
echo "DND_EXI_OVERLAY_READY"
