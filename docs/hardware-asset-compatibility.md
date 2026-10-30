# GameCube real-asset compatibility DOL (Phase 1)

A separate **headless, real-hardware test fixture** for step 1 of the JSystem
compatibility roadmap: discover owned retail assets, exercise the existing
rendering/import pipelines on GameCube, capture actual presented XFB frames and
compare approved reference hashes.

The fixture does **not** ship or download commercial assets, change game
images, decrypt proprietary media or silently rewrite the GC Loader SD card.
It reads the files you already have in `gcloader:/games`.

## What runs where

- **GameCube:** `tests/HardwareAssetCompatibility` is compiled AOT into a
  standalone DOL. It mounts the GC Loader storage device and scans
  `gcloader:/games` recursively. For each plain `.iso`/`.gcm` image, it
  mounts the existing `Storage.MountDisc(image)` read-only FST as `dvd:/`.
  It also looks for supported loose files.
- **GameCube:** it inspects eligible `.bti`, `.jpc`/`.jpa`,
  `.bmd`/`.bdl` and bounded `.arc`/`.szs` RARC members, then selects
  them in stable case order. J3D assets get a real native model-import/draw attempt; BTI assets get a
  direct GX-tiled base-level preview; eligible JPA effects get deterministic
  fixed-step simulation and a diagnostic 2D projection (not BSP1/TEV parity).
  Each case gets a result
  published in a small exported native `dnd_asset_compat_record`.
- **Host:** `scripts/run_hardware_asset_compatibility.py` uses the existing
  PR #141 `HardwareSession`: injected A release/press samples, post-GX
  presentation breakpoints and raw XFB capture over GDB RSP via **USB Gecko
  or TCP**. It reads the current indexed asset record from the same stopped
  GameCube and writes indexed PNG files and `compatibility.json`.

Test states: `pass` means the supported runtime importer/draw call completed;
`unsupported` means the asset fell outside the documented profile;
`fail` means a recognized candidate failed to load or render.
These results **do not prove visual correctness** without reviewing the images.

## Build the fixture

In a DolphinDotNet source checkout with .NET 8, devkitPPC and libogc2:

```sh
dotnet build tests/HardwareAssetCompatibility/HardwareAssetCompatibility.csproj -c Debug
dotnet run --project tools/DolphinDotNet.Compiler -- \
  --bundle tests/HardwareAssetCompatibility/bin/Debug/net8.0/HardwareAssetCompatibility.dll \
  out/HardwareAssetCompatibility --debug-symbols --gdb-usb 0
cd out/HardwareAssetCompatibility && make
```

The output includes `HardwareAssetCompatibility.dol`, its matching ELF and
debug/source maps. The native build embeds `DND_DEBUG_AUTOMATION` and retains
`dnd_asset_compat_record`. Use `--gdb-usb 1` for USB Gecko in slot B.
TCP debugging instead uses the normal `--gdb-tcp`, network and port options.

Load the built DOL/ELF using **the normal Swiss/console debugger launch
process**. It must be running its own debug GDB stub and waiting at the startup
stop; do not attach Rider or Visual Studio at the same time as the script.

The fixture is a GameCube binary, **not a desktop Windows/macOS process**.
The host orchestrates tests; the GameCube reads game images directly from
its GC Loader SD. Supported plain ISO/GCM is required for image mounting;
RVZ, CISO, NKit and compressed ISO containers are not automatically decoded.

## Automated run

```sh
# pip install pyserial   (USB Gecko only)
python3 scripts/run_hardware_asset_compatibility.py \
  --elf out/HardwareAssetCompatibility/HardwareAssetCompatibility.elf \
  --serial COM5 --baud 115200 --timeout 180 \
  --output captures/gamecube-001
```

For TCP (Dolphin GDB or GameCube BBA), replace serial settings with
`--tcp 127.0.0.1:2166`. Change the host address for BBA.

The runner reads the initial discovered-case count, advances each case with
a neutral input frame followed by an injected A press, records the stable
result metadata, and captures the presented frame. A final A press must
produce the `finished` state. Cases do not depend on host wall time or random
seeds. The DOL's own frame loop is what calls `Controller.Update()`.

Outputs:

- `compatibility.json`: source image and asset identity, format, result,
  complete XFB frame hash, dimensions, scanned-entry count, and failures
- `asset-000.png`, `asset-001.png`, ...: actual real-hardware XFB frames
- `baseline-candidate.json`: hashes of successfully imported cases **for
  review**, *not* auto-approved regression fixtures

After visually reviewing the captured frames against the source game/Dolphin
emulator reference, copy an approved baseline to your own persistent path:

```sh
python3 scripts/run_hardware_asset_compatibility.py \
  --elf out/HardwareAssetCompatibility/HardwareAssetCompatibility.elf \
  --serial COM5 --output captures/recheck \
  --baseline approved-gc-golden.json --require-all
```

`--require-all` treats unsupported profiles as regressions; otherwise they are
counted but not failed. A recognized asset's `fail` state always fails.
Missing or changed baseline hashes fail the run. Hashes are **exact packed-XFB
YUV422** hashes, sensitive to VI mode, GX configuration and game/SDK version;
they are not perceptual comparisons or an assertion of Nintendo parity.

## Deliberately bounded discovery

The console's normal managed heap is only 256 KiB. Therefore this initial
harness uses strict limits instead of loading entire retail image archives
into managed memory:

- At most **32 case previews**, **12 disc images**, **2,048 directory entries**
  and **16 RARC indexes** per run, up to directory depth 6
- Up to **96 KiB** for in-memory BTI/JPC and RARC indexing; J3D source models
  are recognized by a bounded header read and loaded through the **native
  importer**, with an 8 MiB source-size filter and 4 MiB native import budget
- BTI previews up to 128×128 power-of-two pixels in the eight native GX
  non-indexed formats, with the base mip only. Indexed CI/TLUT formats,
  larger images and authored mip/filter variants are not treated as fully
  compatible. This intentionally avoids the desktop-only BtiTexture decoder
  path not yet supported by the current GameCube closed-world AOT compiler
- RARC nested member inspection uses the existing bounded decoder; some large,
  compressed or deeply nested retail archives will be **skipped**. The sample
  is not an exhaustive census of every game on the card
- First supported JPA emitter among at most 12 inspected particles is chosen
  (up to 64 active particles, seed 17, 30 fixed 60Hz simulation updates).
  JPA previews show a fixed 2D projection with native source texture, not
  exact GX BSP1 color/alpha/TEV behavior; unsupported JPA profiles are reported

The scanner is **read-only**. It does not boot games inside game images or
support arbitrary studio-private file formats. Hardware USB Gecko throughput,
GC Loader simultaneous SD/image mounts and VI frame captures require physical
verification before claiming end-to-end console acceptance.

## Debugger result ABI

The existing debug-only `dnd_automation_mailbox` and
`dnd_automation_frame_boundary` APIs remain unchanged. The fixture adds a
**separate** `dnd_asset_compat_record` ELF symbol (256 bytes):
`DNAC` magic/version, sequence, state, zero-based index, total, kind, scanned
entry count and a NUL-terminated bounded ASCII asset name. It is read only
while the target is stopped. If an asset name requires non-ASCII text, its
code units are replaced with `?` in this diagnostic record; the original
on-media name is not modified. Correlate captures by index and original
asset paths when possible.

## CI verification

```sh
python3 scripts/ci_test_suite.py native
python3 scripts/ci_test_suite.py assets
python3 scripts/ci_test_suite.py gamecube
```

These compile the AOT fixture and check candidate policies, the native probe
memory layout and host fake-GDB scripted sequences. CI **does not access
personal retail discs or a physical GC Loader** and cannot establish game
rendering fidelity. Keep owned-disc golden references local rather than
committing copyrighted game images or unreviewed render outputs.
