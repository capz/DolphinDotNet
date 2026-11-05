# Firmware resource implementation status

This branch is **incomplete** against `system-firmware-resources-plan.md`.
Phase 1 implementation is complete with synthetic bus, managed, AOT and native
cross-compile validation. Physical-console and real regional-dump verification
remain unperformed and are tracked under Phase 7. No Nintendo data is included.

| Phase | Implemented | Remaining acceptance work |
| --- | --- | --- |
| 1 | Bounded EXI ROM reader, explicit file fallback and source metadata, owned loading, cipher/encoding evidence, cancellation, AOT console/file tests | Real regional dump and physical-console validation under Phase 7 |
| 2 | Yay0 inventory, separate bounded streams, output/work/count budgets, overlap rejection, detached extraction, unsupported BMD1 metadata | Named intro resources, original audio descriptors, regional layout validation, BMD1 structural inspection |
| 3 | Validated BTI extraction relative to each header, existing Texture2D adapter, SystemFont facade preserving IplFont | Validated named NTSC/PAL layouts, console rendering, measured cache/performance budgets |
| 4 | Existing audio pipeline researched; unchanged | Validated WSYS/AW discovery, stereo memory-backed compressed playback and tests |
| 5 | J3D1bmd1 resources explicitly marked unsupported | Original geometry/material importer and fidelity comparisons |
| 6 | No implementation | Native startup sequence, deterministic captures and physical comparison |
| 7 | Synthetic tests wired into the assets CI domain; cipher AOT smoke fixture | All-phase integration and physical/emulated regional validation |

## Using the implemented subset

`FirmwareImage.Load(path)` accepts an explicit 2 MiB dump. `Read(bytes)` copies
caller storage. `CreateCatalog()` creates an independent catalog retaining only
compressed records; dispose the image immediately afterward. `Extract(index)`
returns a fresh, bounded decoded record. Dispose the catalog after extracting
resources. The APIs are synchronous; call loading and extraction away from the
graphics executor, and publish graphics between frames on the application executor.
Do not concurrently dispose an image/catalog while reading it.

Automatic encoding detection requires at least two independent, non-overlapping,
fully decodable Yay0 records in exactly one interpretation. Unsupported or ambiguous
sources fail rather than guessing from magic. Explicit `FirmwareEncoding` bypasses
encoding inference for controlled research; it does not certify a supported revision.
The scan considers the encrypted interval only, preserving the firmware font region.
Malformed candidate signatures are ignored; valid overlapping records are rejected.

`FirmwareTextures.Extract(resource, headerOffset)` requires a header already
identified by the caller. It validates native GX block sizes, all declared mip
ranges, palette counts, sampler fields and per-header relative offsets, then
copies exactly the palette and image bytes into a standalone BTI. It does not
search arbitrary bytes for plausible textures. `SystemResources.CreateTexture`
uses the existing `Texture2D.FromBti` path, including palette conversion, sampler
configuration, GPU cache/lifetime and graphics executor checks. Caller disposes
the returned texture. `SystemFont` wraps the existing `IplFont`; its existing API
and native shared backing remain intact.

## Source research and comparison

Pinned libogc2 source revision:
`094b250f1e978d37789f18ffc2d3c9c2325062f8`, `libogc/system.c`.
The symbol is `void __SYS_ReadROM(void *buf, u32 len, u32 offset)`.
It divides reads at 1024-byte boundaries and retries `__read_rom` indefinitely.
The new reader does not call that wrapper. It uses the confirmed EXI channel
0/device 1, 32 MHz and `offset << 6` command through immediate transfers, holding
normal libogc bus arbitration. Each read is capped at 4096 bytes, 20 ms and 4096
polls per transfer, with deselect/unlock cleanup. No DMA pointers or firmware
writes are involved. Cancellation is observed between 4 KiB transactions.
`SystemFirmware.Open(explicitFallbackPath)` selects console ROM first and only
then the supplied fallback; `FirmwareImage.Source` distinguishes memory, file
and EXI acquisition without claiming that an emulated bus is physical hardware.
The fallback path must be on a volume mounted by the caller.

GC-anim-Wii's `source/main.c` identifies these decompressed slices:

| Resource | Output-size hint | Header position in decoded record | This implementation |
| --- | --- | --- | --- |
| Environment | 0x6140 | 8448 | Generic BTI extraction only; no size-based identification |
| Tiles | 0x1020 | 0 | Generic BTI extraction only |
| Three logo faces | 0x16F60 | 5056, 5088, 5120 | Independent relative headers supported and synthetically tested |
| GAMECUBE wordmark | 0x64A0 | 11616 | Generic BTI extraction only |
| Startup sound | Raw AFC channel hints | Reference heuristics insufficient | Unsupported |
| Cube/logo meshes | Ripped models.c geometry | Outside ROM parser | Not copied; unsupported |

The reference's copy sizes and these offsets are research observations, not
validated layout declarations. They are deliberately not used to assign names
or silently extract a plausible but incorrect asset. Cipher attribution and the
upstream MIT license are retained in source and `licenses/GC-anim-Wii-MIT.txt`.
No GPL Dolphin Emulator source was used.

## Limits and evidence

Configured limits (not performance measurements): 2 MiB image, 2 MiB decoded
record, 128 records, 2 MiB total retained compressed records, 32 MiB cumulative
declared decode work per scan. Encoding detection performs two bounded scans.
No real-ROM inventory, render/audio fidelity, native peak allocation benchmark,
regional compatibility or physical console result is claimed. Synthetic tests
cover the cipher against an independently executed upstream C vector, round trip,
header/font preservation, malformed/truncated streams, backreferences, file size,
encoding ambiguity, cancellation, ownership, and overlapping BTI header offsets.

Local validation on 2026-11-05:

- `dotnet run --project tests/SystemFirmwareTests`: passed.
- `dotnet run --project tests/GameCubeFormatsTests`: passed, 88 assertions.
- `python3 tests/gcft_cli_smoke.py`: passed.
- `python3 scripts/ci_test_suite.py audio`: passed.
- `python3 scripts/ci_test_suite.py native`: passed both standard and O3/LTO modes.
- Firmware synthetic smoke: AOT compilation passed (104 reachable methods,
  27 types), generated C compilation passed, native host execution under forced
  GC passed (42 collections; 4,195,408 bytes occupied at completion in an 8 MiB
  heap). This is a final occupancy reading, not peak memory or a hardware result.

Final local results: `python3 scripts/ci_test_suite.py compiler` passed;
the compiler integration runner passed again after the heap-budget changes;
`python3 scripts/ci_test_suite.py graphics` passed on retry after the GC-root fix,
including model native tests, production GX recordings and ModelSmoke under GC
stress. The pinned devkitPro cross-build passed. Remote CI was dispatched and
remains pending/queued; it is not reported green. NuGet audit requests produced
NU1900 connectivity warnings in some local builds.

The historical native managed heap is 256 KiB. Firmware workloads need an
explicit larger heap; the native Makefile and generated GameCube Makefiles now
accept `make DND_MANAGED_HEAP_BYTES=8388608` (8 MiB), with native compile-time
bounds of 256 KiB to 16 MiB. The default is unchanged. Budget this alongside
native graphics/audio allocations in MEM1. Host firmware AOT execution is tested
with an 8 MiB heap; the initial cross-build alone does not establish successful
execution with the default heap. No emulated execution result is claimed.

The pinned devkitPro/libogc2 Docker cross-build completed successfully and
produced 32-byte-aligned `SystemFirmwareSmoke.dol` and its ELF. This establishes
compilation/linking, not runtime memory sufficiency or physical verification.

The first complete graphics run reached native model compilation and failed on
GCC's `-Werror=dangling-pointer` diagnostic in existing `dnd_array_clone` root
registration. Clone now uses a scoped active GC frame, matching other native
array helpers. GCC O1/Werror compilation, managed runtime/core BCL tests in
standard and O3/LTO modes, and the firmware forced-GC host fixture pass after
this change. The full graphics suite passed on retry; the initial
run remains a recorded failure.

The synthetic fixture was also cross-built with an explicit 8 MiB native heap.
An emulated DOL boot was observed, but the GDB console-buffer read returned an
empty response and Dolphin exited with status 141. This attempt failed to prove
managed completion; it is not an emulated firmware/resource validation pass.
The harness first needed the LTO-renamed console symbol; correcting that name
did not produce successful memory proof. Physical hardware was not available.
Revision text validation accepts CR/LF/tab inside the bounded ASCII header;
the synthetic multiline-header regression passes.

Phase 1 continuation verification:

- Synthetic EXI bus test passes for range limits, unaligned/tail reads, busy and
  select-failure paths, finite timeout, guard bytes and cleanup.
- `tests/system_firmware_aot.py` passes console acquisition and explicit mounted
  file fallback under forced GC: 513 bridge calls, 93 collections, 2,102,048
  bytes occupied at completion, zero file handles. No ROM bytes are retained by
  the bridge after a transaction. Source metadata and pre-cancel behavior pass.
- `source/system_firmware.c` compiles with pinned devkitPro/libogc2, O2/Werror.
- Managed synthetic firmware/source/fallback tests pass.
