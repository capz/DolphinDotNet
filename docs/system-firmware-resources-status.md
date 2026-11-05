# System firmware resource status

The seven-phase contract is **not complete**. Phase 1 software access is
implemented; Phases 2–4 have strict supported-layout implementations and tests.
Original J3D1 geometry/material interpretation (Phase 5) and the startup renderer
(Phase 6) remain unimplemented. Integration work for Phase 7 is present, but
original regional compatibility and physical-console validation are unperformed.
No Nintendo firmware or extracted proprietary assets are included.

| Phase | Implemented | Remaining acceptance work |
| --- | --- | --- |
| 1 | Bounded EXI access; explicit file fallback/source metadata; ownership, cancellation, descrambling and encoding evidence; console/file AOT tests | Physical console and real revision coverage |
| 2 | Yay0 inventory/extraction; J3D1 section inspection; TEX1/BTI profiles; strict FST/WSYS inventories; metadata-linked stereo extraction | Verify actual regional layouts, especially the currently unconfirmed FST/AW association |
| 3 | Named caller-owned Texture2D resources; metadata-only cache; SystemFont/IplFont compatibility; native allocation/disposal tests | Original NTSC/PAL render captures and comparisons |
| 4 | Lossless stereo AFC packaging; native memory-backed Sound/Music; decoder, loop, cancellation and lifetime tests | Confirm actual startup descriptors/archive mapping and physical stereo output |
| 5 | Structural metadata explicitly marks models unsupported | Original geometry/topology, attributes, hierarchy/matrices, materials/TEV and embedded texture interpretation |
| 6 | No renderer/controller implementation | Original startup sequence using the Phase 5 models and deterministic captures |
| 7 | AOT/native bindings, heap selection, metadata CLI, regression integration, emulator completion proof and API guide | Phase 5/6 integration, actual regional dumps, physical validation and final remote CI confirmation |

## Current validation — 2026-11-05

Passed on the cloud host:

- `dotnet run --project tests/SystemFirmwareTests`: synthetic cipher/reference
  vector, exact-size/header/source checks, cancellation, malformed Yay0/J3D1/
  TEX1/WSYS/FST inputs, 14/31-record families, all six named texture profiles,
  overlapping header-relative BTIs, metadata cache ownership and work budgets.
- `dotnet run --project tests/GameCubeFormatsTests`: 88 assertions.
- `python3 tests/gcft_cli_smoke.py`: desktop archive roundtrip.
- `python3 scripts/ci_test_suite.py native`: standard and O3/LTO modes, including
  new synthetic EXI range/contention/timeout/cleanup tests.
- `python3 scripts/ci_test_suite.py compiler`: compiler integration and native
  AOT numeric/string/generic/runtime fixtures.
- `python3 scripts/ci_test_suite.py graphics`: native model/GX recording tests,
  ModelSmoke with forced GC, texture, effect and font smoke builds.
- `python3 scripts/ci_test_suite.py audio`: asset conversion, WAV/PCM native
  tests, container/vector/loop/independent-voice tests for file and memory sources.
- `python3 tests/system_firmware_aot.py`: synthetic console acquisition,
  malformed-console and unavailable-source fallback, mounted-file loading,
  catalog/WSYS inventory, named texture publication, stereo Sound/Music playback,
  custom loops, cancellation, typed malformed-source/EOF exceptions, and lifetime
  independence after image/catalog disposal. No live file/draw/voice/clip handles
  remain. Final host run: 1,025 bridge calls, 330 forced-GC collections and
  2,102,048 heap bytes occupied at completion in an 8 MiB heap. This is final
  occupancy, not peak memory.
- `python3 tests/system_firmware_inventory.py`: metadata-only CLI and truncated
  source checks. The CLI exports no firmware, pixel, vertex or sample payload.
- Native audio tests under AddressSanitizer/UBSan. LeakSanitizer could not run
  under tracing; leak detection was disabled for that run. Explicit lifetime
  assertions passed.
- Native runtime/core BCL regression tests and 18 Dolphin probe unit tests.

The pinned devkitPro/libogc2 Docker build passed with `system_firmware.c` and
produced a 32-byte-aligned DOL/ELF. The firmware-reachable bundle selected an
8 MiB heap; the ELF confirmed its actual BSS allocation.

**Successful emulated synthetic execution:** Dolphin/JIT, using a deterministic
completion breakpoint at `dnd_platform_wait_forever`, reached the expected PC
and verified both guest-written console-buffer strings:
`SYSTEM FIRMWARE CIPHER PASS` and `MANAGED MAIN: OK`. No DOL-specific loader/PPC
errors were reported. This proves the synthetic program, not original firmware
acquisition, original assets, original animation fidelity or physical hardware.
Artifacts/logs are local under `out/system-firmware-completion-proof`.

Earlier asynchronous-interrupt memory probes failed with empty/extra stop
responses. Bounded reads alone did not fix that path; the successful run uses
`--memory-after-symbol`. The probe now supports bounded memory packets and unique
LTO private-symbol aliases, with strict byte matching. An earlier graphics run
also hit GCC's existing Array.Clone root-registration warning; the scoped-root
fix passed subsequent standard/O3-LTO runtime and complete graphics runs.

Remote CI is triggered/dispatched on the existing draft PR. Its current result
must be checked separately; no green remote pipeline is asserted here. No merge
is authorized. Physical-console testing has not occurred.

## Supported layout and fidelity limits

Named texture resolution uses the reference's decompressed-size/relative-header
hints **plus** validated J3D1 sections, TEX1 membership and complete per-header
BTI/palette/mip ranges. All three logo faces must validate together. Ambiguous
candidates and unknown layouts fail. No absolute ROM texture offsets are used.
Only immutable metadata is cached; catalogs retain no complete ROM/decoded model
or shared GX object. Access is serialized through the existing executors.

Stereo discovery currently requires a valid embedded FST with a unique named AW
mapping, bounded WSYS descriptors, two compatible/disjoint channels and a unique
startup-duration/rate candidate. Channels are losslessly interleaved for the
existing native AFC decoder. It does not use a right-channel sample signature
or a fixed AW base. **FST presence/layout in actual IPL revisions is unverified.**
A filename string alone is not accepted as a filesystem. Non-FST mappings remain
unsupported. Standalone WSYS/AW inspection and metadata-selected stereo extraction
are available, but do not certify a specific real IPL layout.

J3D1 inspection validates only the file/chunk/TEX1 structures implemented here.
It does not claim support for original vertex, matrix, material, TEV or display-
list semantics. No ripped GC-anim-Wii geometry is used, no untrusted display
list is sent directly to GX, and no flat substitute is presented as authentic.

## Comparison with GC-anim-Wii

| Resource | Reference output-size/header-position hints | Current handling |
| --- | --- | --- |
| Environment | 0x6140 / 8448 | Strict named TEX1/BTI profile and native Texture2D publication |
| Tiles | 0x1020 / 0 | Validated standalone BTI profile |
| Three logo faces | 0x16F60 / 5056, 5088, 5120 | All three table entries and individual relative offsets validated |
| GAMECUBE wordmark | 0x64A0 / 11616 | Strict named TEX1/BTI profile |
| Startup audio | Reference raw-AFC heuristic | Metadata-linked WSYS/AW stereo path for the strict supported layout; real mapping unverified |
| Cube/logo/menu geometry | Reference contains ripped models.c meshes | Original geometry unsupported; meshes not copied |
| Startup timing/rendering | Reference manually reconstructed | Not implemented; requires original geometry work |

The reference's positions are hints, not arbitrary copy lengths or proof of
regional compatibility. Actual-resource comparisons/captures require supplied
dumps. Its MIT license and cipher attribution are retained in
`docs/licenses/GC-anim-Wii-MIT.txt` and source. No GPL Dolphin Emulator code was
copied or committed. GDB protocol behavior was inspected during diagnosis.

The FST research lead is
https://www.hcs64.com/mboard/forum.php?showthread=46117; that author did not locate
or validate the table root. No downloadable firmware from that thread was used.

## Budgets and measured cache behavior

Configured limits: 2 MiB exact ROM; 2 MiB decoded record; 128 Yay0 records;
2 MiB retained compressed catalog payload including stereo; 32 MiB declared Yay0
work per scan; separate bounded WSYS scan/entry/candidate limits; 4,194,304 TEX1
texels across mip chains before decode allocation. EXI requests are at most
4 KiB with a 20 ms deadline and finite transfer-poll cap, with cleanup on failure.
Cancellation is observed between reads and within decompression/metadata/stereo
loops. Native compressed audio uses caller-selected budgets up to 16 MiB.

Ordinary generated applications retain a 256 KiB managed heap; firmware-reachable
GameCube bundles select 8 MiB automatically. `DND_MANAGED_HEAP_BYTES` permits an
explicit 256 KiB–16 MiB override. Native graphics/audio allocations share MEM1
with that heap. No physical peak-allocation measurement is claimed.

Host warm-cache measurements for 100 named lookups ranged from 0.029 to 0.304 ms
across runs, allocating 3,200 bytes in both 14- and 31-record fixtures. These are
host observations, not console performance predictions.

## Required input for remaining work

A user-supplied original firmware dump (preferably NTSC and PAL) or an accessible
console is needed to research/verify original J3D1 geometry/material layouts and
actual resource association. The supplied Wii reference explicitly lacks that
parser and uses ripped geometry. Reinterpreting J3D2 or using those meshes would
violate the contract. The missing-input question remains pending; no dump or
physical connection has been provided in this session.

See [the API and diagnostics guide](system-firmware-resources.md). Use
`dnd-assets firmware inspect INPUT` for metadata-only reporting; original asset
payloads and captures must not be committed.
