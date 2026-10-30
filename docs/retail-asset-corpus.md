# Retail GameCube asset corpus: DOL + debugger capture

This is priority **1** of the [JSystem compatibility plan](jsystem-remaining-gaps.md):
collect owned retail file fixtures, decode/import them with DolphinDotNet on real
GameCube hardware, capture native GX frames through the [debug automation API](hardware-debug-automation.md),
and compare them with a recorded Dolphin/reference baseline. It is **not**
a complete JPA/MAT3/TEV implementation.

The fixture uses no menu, prompts or interactive UI. It does not copy, upload,
modify or redistribute copyrighted disc data.

## Storage discovery (read-only)

The managed DOL at `tests/RetailAssetCorpus` mounts `StorageDevice.GcLoader`
and scans `gcloader:/games`. If the GC Loader volume is unavailable or that
directory is missing, it falls back to `sd:/games` via SD2SP2.

It recognizes **plain** `.iso` and `.gcm` files (not RVZ/CISO) in
the games tree, bounded to 512 directory visits and **four** lexicographically
selected images. Each image is mounted as the same read-only `dvd:/` FST
filesystem used by the SDK. The scan examines up to 2,048 FST entries per image,
five levels deep, and selects at most ten assets/two per extension family. The
file importer enforces a 2 MiB limit on ordinary samples and 4 MiB on RARC
sources. Oversized/unknown assets are not loaded indiscriminately.

Supported sampling profiles:

| Format | Test mode | What it proves |
| --- | --- | --- |
| BTI | Decode + GX upload + full-frame texture | Source decode, sampler setup, resident texture render |
| J3D2 BMD/BDL | Native import + fixed-camera GX draw | Strict existing MAT3 profile and native renderer, without material approximation |
| JPAC1/JPAC2 | Parse + bounded billboard emitter GX draw | Only currently supported emitter/particle subset |
| BMG | Decode-only | Generic message decoder; no text layout/visual equivalence |
| BFN | Decode-only | Font metadata reader; no glyph atlas/render comparison |
| RARC/Yaz0 | Index + limited embedded BTI/J3D draws | Bounded archive traversal, at most two visual members per archive |

Results distinguish `ok`, `unsupported`, `malformed` and
`runtime-error`. Each result includes source length, native allocated GX
bytes and a stable key consisting of Game ID/disc number/revision plus
the in-disc path. Parsing and rendering are different evidence; capture files
for decode-only tests are plain diagnostic background frames.

**Limitations:** The scan checks direct FST entries and at most 64 members of each selected
RARC, attempting up to two embedded BTI/J3D render tests per archive. It
does **not** exhaustively inspect nested archives or every retail format. Models use a fixed camera and
may be off-screen if their native scale is unusual. Sprite capture is at the
native VI resolution without depth, alpha or HDMI/analog capture. Unsupported
assets are reported; no automatic material approximation is made.

## Build the DOL

From the SDK repository, with .NET 8 and a working devkitPPC/libogc2 toolchain:

```sh
dotnet build tests/RetailAssetCorpus -c Debug
dotnet run --project tools/DolphinDotNet.Compiler -- \
  --bundle tests/RetailAssetCorpus/bin/Debug/net8.0/RetailAssetCorpus.dll \
  out/retail-corpus --debug-symbols --gdb-usb 0
make -C out/retail-corpus
```

The resulting `out/retail-corpus/Game.dol` runs on the GameCube and
`Game.elf` holds the native symbols required by the debugger. Use the
appropriate EXI slot for your hardware: `--gdb-usb 0` is slot A, 1 is slot B.
Make sure the actual GDB transport and debug ELF match. The game needs its
real GC Loader volume accessible and must have permission to mount the disc images.
Physical firmware may not expose that block volume while using the ODE;
the `sd:/games` fallback is intentional.

Launch the game using your existing Swiss, USB Gecko or console-launcher
workflow **before attaching** the test runner. Do not simultaneously attach
Rider/Visual Studio to the same GDB transport.

## Capture a baseline in Dolphin or on hardware

```sh
# USB Gecko connection on a physical console:
python3 scripts/hardware_asset_corpus.py \
  --elf out/retail-corpus/Game.elf --serial COM5 --baud 115200 \
  --timeout 240 --max-frames 120 --output out/corpus-hardware

# Dolphin's GDB TCP listener or actual BBA GDB stub:
python3 scripts/hardware_asset_corpus.py \
  --elf out/retail-corpus/Game.elf --tcp 127.0.0.1:2166 \
  --timeout 240 --max-frames 120 --output out/corpus-reference
```

The host runner imports `HardwareSession`/`Rsp` from the PR #141
debugger API. It pauses at each completed GX presentation, injects a bounded
synthetic **A** button on port 4 to verify console-side PAD polling, and reads a
224-byte, versioned read-only `dnd_asset_corpus_report` symbol through
ordinary GDB RSP `m` packets.

New case records produce `asset-000.png`, `asset-001.png`, etc.
`report.json` includes case names, classification, frame serials,
native XFB SHA-256, image size, sample bytes and GX-memory measurements.

Save the reference `report.json` as an immutable baseline; do not
automatically bless differences. Then compare a new run:

```sh
python3 scripts/hardware_asset_corpus.py \
  --elf out/retail-corpus/Game.elf --serial COM5 \
  --baseline out/corpus-reference/report.json \
  --output out/corpus-hardware
```

The runner fails on missing/added cases, changed classification,
changed raw-XFB hashes for successful *visual* cases, or increased
per-case native GX allocation.
Even without a baseline, `malformed` and `runtime-error` cases fail, as does
an asset run without any successful visual BTI/J3D/JPA sample. `unsupported`
is a recorded skip, not a fabricated pass; the JSON report is written
before these assertions. Decode-only cases are not misleadingly
compared by blank framebuffer hashes. For emulator/hardware color-space
differences or intentional render changes, inspect the PNGs and produce a
new reviewed reference baseline. Avoid blindly comparing different VI modes
or display configurations.

### Failure and safety handling

- A run with no discovered assets is a failure by default; `--allow-empty`
  exists only to smoke-test mount failure/report completion.
- Missing debugger symbols, unexpected mailbox versions, duplicate/missing
  case IDs, skipped frames, rejected input injection, truncated XFB
  data and exceeded frame budgets fail without claiming compatibility.
- Max cases: 56 overall (four images × up to ten direct and four embedded cases);
  no raw game assets or game image bytes leave the console.
- An individual input override is bounded to 3,600 polls and expires;
  controller port 4 is isolated from normal ports 1–3.
- The host report is captured after VI presentation; GX buffers remain
  owned by the game. A one-frame capture costs 640×480×2=614,400
  serial bytes at standard NTSC resolution, so USB Gecko can be slow.

## CI versus actual hardware acceptance

The repository's normal compiler + GameCube AOT checks compile the DOL
fixture, native tests verify the 224-byte report ABI, and Python tests exercise
a simulated RSP/VI/input sequence and exact golden comparisons.

CI does **not** run owned retail discs or a physical GC Loader. Before
calling a format visually compatible, run the identical game image/fixture
on Dolphin and physical GameCube, compare the same test cases and captures,
and investigate any differences. Real hardware speed, boot access,
EXI slot conflicts and GPU/memory pressure must be recorded separately.
