# Native DOL argument handoff

Build `argv_target.c` with `build_target.sh <sdk-root> <output>` inside the pinned
libogc2 toolchain. `-Wl,-n` avoids an ELF program-header segment below cached MEM1.
Then run `python build_caller.py --target <target.dol> --output <caller-dir>`.
The generated project references this SDK source and embeds only the 448-byte
original native test payload. It keeps the default 256 KiB managed heap.

In a cloud-local X11 display with Dolphin NoGUI and `nm`, run:

```sh
python verify_native.py --caller <caller-dir>/bin/Release/net8.0/DolArgumentsSmoke.dol \
  --target-elf <target.elf> --logs <fresh-output-directory>
```

`native-review/result.json` records an actual managed-caller → PPC-target handoff
in Dolphin 2503. It checked five arguments, spaces, UTF-8 and all NUL separators.
It is not a Swiss compatibility test or physical-console result. No user desktop
or host clock is involved. The standalone fixture uses no copyrighted game data.
