#!/usr/bin/env bash
set -euo pipefail
# Run from the repository root after scripts/build_exi_overlay.sh.
: "${DEVKITPRO:?}" "${DEVKITPPC:?}"
output=${1:?output directory}
mkdir -p "$output"
"$DEVKITPPC/bin/powerpc-eabi-gcc" -O2 -g -Wall -Wextra -Werror -DGEKKO -mogc \
  -Iinclude -I"$DEVKITPRO/libogc2/gamecube/include" tests/display_switch_gamecube.c \
  -L"${DND_EXI_OVERLAY:-build/exi-overlay}" -L"$DEVKITPRO/libogc2/gamecube/lib" \
  -logc -lm -o "$output/display-switch.elf"
elf2dol "$output/display-switch.elf" "$output/display-switch.dol"
python3 scripts/dol_sections.py --pad "$output/display-switch.dol"
