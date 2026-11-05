# Firmware resource implementation status

This branch is **incomplete** against `system-firmware-resources-plan.md`.
Phase 1 implementation is complete with synthetic bus, managed, AOT and native
cross-compile validation. Physical-console and real regional-dump verification
remain unperformed and are tracked under Phase 7. No Nintendo data is included.

| Phase | Implemented | Remaining acceptance work |
| --- | --- | --- |
| 1 | Bounded EXI ROM reader, explicit file fallback and source metadata, owned loading, cipher/encoding evidence, cancellation, AOT console/file tests | Real regional dump and physical-console validation under Phase 7 |
| 2 | Bounded Yay0/FST/WSYS inventories; J3D1 section inspection; strict TEX1/BTI profiles for named intro resources; metadata-linked stereo extraction | Real-ROM regional compatibility and any non-FST archive layout remain unverified |
| 3 | Named texture publication via existing Texture2D; metadata-only caching; native allocation/disposal fixture; SystemFont/IplFont compatibility checks | Real regional texture captures and physical rendering comparisons |
| 4 | Lossless metadata-selected stereo AFC packaging; native memory-backed compressed Sound/Music; cancellation, loop and ownership tests | Real regional archive mapping, physical stereo playback and capture remain unverified |
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

Phase 2 continuation:

- `InspectModel` validates J3D1 file/chunk extents and duplicate sections without
  claiming geometry semantics. `GetTextures` validates a TEX1 table and every
  header-relative image/palette/mip range within the section.
- `GetTexture(SystemTexture)` resolves all six intro texture names using profile
  size/relative-header hints **plus** valid J3D1 sections, TEX1 membership and
  complete BTI validation. Three logo faces are validated together. Duplicate
  candidates and unknown layouts fail. No absolute ROM asset offsets are used.
- Strict embedded-FST discovery validates bounded names, hierarchy, file ranges
  and independent structured files. WSYS inventories validate banks, archive
  names, wave counts, codecs, sample rates, durations and loop ranges.
- Stereo discovery requires a unique metadata-compatible pair, a uniquely named
  AW file in the validated table and disjoint in-bounds channel extents. Original
  compressed channels are losslessly interleaved into the existing AFC container.
  It does not use the right-channel sample signature or a fixed AW base.
- FST presence in actual IPL revisions is **unverified**. This is a strict
  supported layout, not a claim that filename strings prove a filesystem.
  Non-FST archive locations remain explicitly unsupported; standalone WSYS/AW
  inspection and metadata-selected stereo extraction are also available.
- Original synthetic fixtures cover 14/31-record families, all six named texture
  profiles, overlapping relative BTI headers, malformed model/texture/WSYS
  ranges, valid and ambiguous FST tables, stereo pairing and disposal.
- Expanded console/file AOT fixture passes named environment extraction,
  embedded-FST inventory, WSYS and stereo metadata under forced GC: 513 reads,
  260 collections, 2,102,048 bytes occupied at completion, no file handles.
- The catalog caches immutable texture metadata only; it never caches a ROM,
  decoded model blob or shared GX handle. Returned metadata arrays are detached.

The file-table research lead is documented at
https://www.hcs64.com/mboard/forum.php?showthread=46117; its author did not locate
or validate the table root. No downloadable firmware from that thread was used.
No original geometry, textures or sounds were obtained from external dumps.

Phase 3 continuation:

- `SystemResources.CreateTexture(catalog, SystemTexture)` resolves the validated
  profile and returns a fresh existing `Texture2D`, with unchanged native GX
  sampler, palette/mip conversion, alpha, cache flush and executor rules. The
  caller owns/disposes each texture; no shared graphics objects are cached.
- The forced-GC console/file AOT fixture now creates a native 8x8 I4 texture from
  the named environment profile and checks dimensions, mip count, native bytes
  and restoration of graphics allocation totals after disposal. It passes with
  zero live draw handles (513 reads; 275 collections; final heap 2,102,048 bytes).
- Existing firmware-font provider tests pass, including cache lifetime,
  proportional metrics, failure paths and I4/IA8 swizzles. The font smoke fixture
  also checks SystemFont measurements against the unchanged IplFont API.
- Host synthetic warm-cache measurement: 100 named environment lookups took
  0.029 ms for 14 records and 0.051 ms for 31 records, allocating 3,200 managed
  bytes in either case. These are one host run, not console performance claims.
- Non-looping WSYS sentinel coordinates are ignored rather than interpreted as
  active loop ranges. Named archive resolution accepts a unique basename match
  and still rejects ambiguous mappings.

The final Phase 3 AOT rerun passes after adding a preflight TEX1 decoding-work
budget (4,194,304 texels across all mip chains, before allocating decode output)
and removing redundant RGBA decoding just to obtain metadata. WSYS inspection
and stereo interleaving now observe cancellation within their bounded loops.
A malformed shared-image table is rejected by the work-budget regression.
Host warm-cache timings varied from 0.029 to 0.304 ms per 100 lookups across runs;
allocated bytes remained 3,200. The variation is not a console timing estimate.
The SystemFont/IplFont smoke compiles through AOT (257 reachable methods).

Phase 4 continuation:

- `Sound.FromEncoded` and `Music.FromEncoded` copy compressed input (and optional
  paired-bank data) into owned native memory. Parsing and complete validation use
  the existing codecs and bounded `AudioStep` slices. No files are opened and no
  managed pointers survive the copy. Cancellation disposes native state.
- `SystemResources.CreateStartupSound/Music` preserve two channels, original
  sample rate and sample count. The startup profile uses WSYS/FST metadata and
  original compressed AFC channels; there is no PCM8 or mono conversion.
- Existing audio container, known vector, custom-loop, independent voice and
  cleanup tests pass with memory sources as well as file sources. A separate
  predictor-zero vector verifies +1 left and -2 right at 32 kHz.
- The expanded AOT fixture passes memory-backed Sound/Music playback, loops,
  simultaneous-source lifetime isolation, post-image/catalog-disposal playback,
  malformed-source cleanup and pre-cancellation. Its deterministic backend checks
  actual decoded stereo samples. All voices and clip tokens are retired and no
  files or graphics handles remain. Final run: 1,025 console bridge calls,
  330 forced-GC collections, 2,102,048 heap bytes occupied at completion.
- AOT validation found missing native mappings for InvalidDataException and
  EndOfStreamException. Both now preserve their CLR identities/inheritance, and
  explicit invalid-console fallback is caught separately from IOException.
  Malformed Yay0 markers, typed malformed-audio catches, invalid console data,
  inaccessible console data and EOF inheritance are tested through AOT.
- Audio native tests pass AddressSanitizer/UBSan with leak detection disabled
  because LeakSanitizer cannot run under the environment's tracing. Explicit
  voice/clip/file lifetime assertions pass. Native runtime/core BCL tests pass.

Real IPL startup archive association is still only supported for the strict
validated FST/WSYS layout described above. No real dump has been supplied, so
that association and original J3D1 geometry cannot yet be confirmed. There is no
claim of physical audio or original-animation fidelity.
