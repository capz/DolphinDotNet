#!/usr/bin/env bash
set -euo pipefail
# Run from the repository root after AOT generation; uses the verified EXI archive.
: "${DEVKITPRO:?}" "${DEVKITPPC:?}"
generated=${1:?generated AOT C file}
output=${2:?output directory}
mkdir -p "$output"
"$DEVKITPPC/bin/powerpc-eabi-gcc" -O2 -g -Wall -Wextra -Werror -DGEKKO -mogc \
  -Iinclude -I"$DEVKITPRO/libogc2/gamecube/include" \
  tests/controller_dolphin_entry.c "$generated" source/input.c source/input_gamecube.c \
  source/managed_runtime.c source/async_runtime.c -Wl,--wrap=dnd_input_poll \
  -L"${DND_EXI_OVERLAY:-build/exi-overlay}" -L"$DEVKITPRO/libogc2/gamecube/lib" \
  -logc -lm -o "$output/controller.elf"
elf2dol "$output/controller.elf" "$output/controller.dol"
python3 scripts/dol_sections.py --pad "$output/controller.dol"
