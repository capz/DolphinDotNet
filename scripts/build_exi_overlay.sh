#!/usr/bin/env bash
set -euo pipefail
# Rebuild EXI and VIDEO from our verified libogc2 corrections. Keep the
# historical script/archive paths for existing consumers. Never modify the SDK.
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SDK="${DEVKITPRO:?}/libogc2/gamecube"
PPC="${DEVKITPPC:?}/bin"
UPSTREAM_REV=094b250f1e978d37789f18ffc2d3c9c2325062f8
UP="${DND_EXI_SOURCE_DIR:-$ROOT/build/exi-upstream}"
mkdir -p "$ROOT/build/exi-overlay" "$UP"
if [ ! -d "$UP/.git" ]; then
  git clone --quiet --filter=blob:none https://github.com/extremscorner/libogc2.git "$UP"
fi
if ! git -C "$UP" cat-file -e "$UPSTREAM_REV^{commit}" 2>/dev/null; then
  git -C "$UP" fetch --quiet --depth=100 origin master || git -C "$UP" fetch --quiet origin
fi
git -C "$UP" checkout --quiet --detach "$UPSTREAM_REV"
# Verify both sources differ only by the documented fixes and attribution.
python3 "$ROOT/scripts/verify_exi_source.py" "$UP/libogc/exi.c" "$ROOT/vendor/libogc2/exi.c"
python3 "$ROOT/scripts/verify_video_source.py" "$UP/libogc/video.c" "$ROOT/vendor/libogc2/video.c"
for module in exi video; do
  "$PPC/powerpc-eabi-gcc" -c "$ROOT/vendor/libogc2/$module.c" \
    -o "$ROOT/build/exi-overlay/$module.o" \
    -DLIBOGC_INTERNAL -DGEKKO -DHW_DOL -O2 -g \
    -mcpu=750 -meabi -msdata=sysv -mhard-float \
    -ffunction-sections -fdata-sections -fno-strict-aliasing \
    -Wall -Wno-address-of-packed-member -Wno-prio-ctor-dtor \
    -I"$UP" -I"$UP/libogc" -I"$UP/include" -I"$UP/include/ogc" \
    -I"$UP/include/ogc/machine" -I"$UP/include/sdcard" \
    -I"$SDK/include" -I"$SDK/include/ogc" \
    -I"$UP/lwip/include"
done
# Publish only after both replacements compile and the archive is complete.
archive="$ROOT/build/exi-overlay/libogc.a.tmp"
trap 'rm -f "$archive"' EXIT
cp "$SDK/lib/libogc.a" "$archive"
"$PPC/powerpc-eabi-ar" d "$archive" exi.o video.o
"$PPC/powerpc-eabi-ar" r "$archive" "$ROOT/build/exi-overlay/exi.o" "$ROOT/build/exi-overlay/video.o"
"$PPC/powerpc-eabi-ranlib" "$archive"
mv "$archive" "$ROOT/build/exi-overlay/libogc.a"
echo "DND_EXI_OVERLAY_READY"
echo "DND_VIDEO_OVERLAY_READY"
