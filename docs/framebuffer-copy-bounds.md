# Quantized GX display-copy bounds

The standard 480-line AA presets use a 242-line EFB. In the pinned libogc2
toolchain, `GX_GetYScaleFactor(242,480)` returns `1.98760330677` and
`GX_GetNumXfbLines` reports **483** copied rows. A normal
`SYS_AllocateFramebuffer` request for the VI mode reserves only 480 rows.
At a 640-pixel padded stride and two bytes per XFB pixel, that leaves a
**3,840-byte overrun** on each affected copy.

The shared allocation helper queries the actual library's quantized copy
height, then allocates `max(VI rows, copied rows)`. Only the temporary storage
mode changes. The VI mode, EFB dimensions, stride, sample pattern and presented
480-line output remain intact. Both initialization and display transitions use
the helper. Transitions retain the existing allocate-both-before-publication
behavior and preserve the current buffers if either replacement allocation fails.

## Verification

The complete native host suite passes in its strict debug and shipped O3/LTO
configurations. The display fixture covers older libogc's 483-row result, a
479-row result from another quantizer, padded strides, ordinary 240/480/576
modes, unchanged VI metadata and both transactional allocation failures.

The optional PowerPC fixture in `tests/framebuffer_canary_gamecube.c` includes
the production helper. Its wrapped allocator reserves a separate 20 KiB red
zone, allowing an unsafe control copy to be observed without corrupting the
heap. The source EFB is cleared to black, so every copied YUYV byte differs
from the red-zone sentinel.

Using the exact pinned toolchain image
`ghcr.io/extremscorner/libogc2@sha256:13bb658d3f18903816617223f5d7c6f776d3db3ea806aae67f8d35a7a69d4c85`,
the fixture ran in Dolphin 2503 / OpenGL with XFB-to-RAM copies, emulator AA
and postprocessing disabled. The measured legacy 480i-AA and 480p-AA controls
both wrote 618,240 bytes into a claimed 614,400-byte framebuffer, changing all
3,840 bytes of the adjacent red zone. The helper requested 618,240 bytes and
both copies left its red zone intact.

| Native copy | Storage rows | Claimed bytes | Written bytes | Red-zone changes |
| --- | ---: | ---: | ---: | ---: |
| 480i AA, legacy | 480 | 614400 | 618240 | 3840 |
| 480p AA, legacy | 480 | 614400 | 618240 | 3840 |
| 480i/p AA, helper | 483 | 618240 | 618240 | 0 |
| Return from 576p to 480p AA | 483 | 618240 | 618240 | 0 |

All twelve cases completed: 240p, non-AA 480i/p, unsafe and protected 480i/p AA,
576i/p, return to 480p AA, 240p AA and a safe 480i return. Every protected copy
stayed within its allocation. This establishes actual emulator GX-copy bounds;
it is not physical-console qualification or proof of visual AA quality.
The [complete result and provenance](framebuffer-copy-bounds/native-canary.json)
include all twelve cases and hashes of the fixture, production helper, binaries
and original row readback.

## Reproduction

Run the host regression suite, including debug and O3/LTO assertions:

```sh
python3 scripts/ci_test_suite.py native
```

Use devkitPPC and libogc2 with the SDK's generated EXI overlay. The overlay is
needed for this pinned environment's pre-main RTC read; it does not replace
the GX math or copy implementation. Set `DND_EXI_OVERLAY` to the generated
`build/exi-overlay` directory, then build a matching DOL/ELF pair:

```sh
powerpc-eabi-gcc -g -O2 -Wall -Wextra -Werror -DGEKKO -mogc -mcpu=750 \
  -meabi -mhard-float -Iinclude -I"$DEVKITPRO/libogc2/gamecube/include" \
  tests/framebuffer_canary_gamecube.c -L"$DND_EXI_OVERLAY" \
  -L"$DEVKITPRO/libogc2/gamecube/lib" -Wl,--wrap=SYS_AllocateFramebuffer \
  -logc -lm -o /tmp/framebuffer-canary.elf
elf2dol /tmp/framebuffer-canary.elf /tmp/framebuffer-canary.dol
```

With a working X11 display (or Xvfb), run:

```sh
python3 scripts/dolphin_framebuffer_canary.py \
  --dol /tmp/framebuffer-canary.dol --elf /tmp/framebuffer-canary.elf \
  --logs /tmp/fresh-framebuffer-canary
```

The script records each mode's predicted and allocated rows, last written byte
and changed red-zone bytes in `result.json`, plus the original PowerPC readback
in `rows.bin`. It requires overwritten unsafe controls and intact protected
red zones before reporting success. No firmware or sample assets are required.
