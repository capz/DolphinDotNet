#!/usr/bin/env bash
set -euo pipefail
: "${DEVKITPRO:?}" "${DEVKITPPC:?}"
output=${1:?output directory}
mkdir -p "$output"
"$DEVKITPPC/bin/powerpc-eabi-gcc" -O2 -g -Wall -Wextra -Werror -DGEKKO -mogc \
  -I"$DEVKITPRO/libogc2/gamecube/include" tests/dol_replacement_gamecube.c \
  -L"${DND_EXI_OVERLAY:-build/exi-overlay}" -L"$DEVKITPRO/libogc2/gamecube/lib" \
  -Wl,-Ttext,0x80200000 -logc -lm -o "$output/replacement.elf"
elf2dol "$output/replacement.elf" "$output/replacement.dol"
python3 scripts/dol_sections.py --pad "$output/replacement.dol"
python3 - "$output" <<'PY'
from pathlib import Path
import sys
root=Path(sys.argv[1]);data=(root/'replacement.dol').read_bytes()
(root/'replacement_bytes.c').write_text('const unsigned replacement_size='+str(len(data))+';\nconst unsigned char replacement[]={'+','.join(map(str,data))+'};\n')
PY
"$DEVKITPPC/bin/powerpc-eabi-gcc" -O2 -g -Wall -Wextra -Werror -DGEKKO -DDND_GAMECUBE_STORAGE -mogc \
  -Iinclude -I"$DEVKITPRO/libogc2/gamecube/include" tests/dol_chain_gamecube.c \
  "$output/replacement_bytes.c" source/dol.c source/dol_gamecube.c source/managed_runtime.c \
  -L"${DND_EXI_OVERLAY:-build/exi-overlay}" -L"$DEVKITPRO/libogc2/gamecube/lib" \
  -logc -lm -o "$output/chain.elf"
elf2dol "$output/chain.elf" "$output/chain.dol"
python3 scripts/dol_sections.py --pad "$output/chain.dol"
