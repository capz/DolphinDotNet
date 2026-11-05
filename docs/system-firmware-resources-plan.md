# System firmware resources — implementation contract

> Source of the assets: the Nintendo GameCube IPL (2 MiB boot ROM).
> This is an implementation task, **not** permission to embed copyrighted ROM contents, ripped meshes, audio samples, or font bytes in the repository.
> The public API namespace is `Dolphin.System`; **do not put “Ipl” or “IPL” in any newly introduced public type or member name**. Source provenance belongs in XML comments and documentation, not identifiers. Keep existing `Dolphin.Graphics.IplFont` working for backwards compatibility.

## Objective

Implement the entire GameCube firmware resource pipeline in **capz/DolphinDotNet**, on this single feature branch and PR. Complete every phase below with a **separate, meaningful Git commit per phase**. Continue through phases without asking the user to make API design decisions: use the smallest idiomatic API consistent with DolphinDotNet's current `Dolphin.Formats`, `Dolphin.Graphics`, `Dolphin.Audio`, AOT/compiler intrinsic, async storage, and native GX lifecycles. Avoid redundant codecs or entire duplicate rendering engines.

### Required sources and repo integrations

- [GC-anim-Wii](https://github.com/supertazon/GC-anim-Wii) and its `source/{main,yay0dec,descrambler,bti,afcdecoder,cubes,tiles,logos,models}.c`. This is a **Wii-only animation recreation**: its textures/audio are extracted from an explicit IPL file, but the geometry is ripped and its animation is manually reconstructed. The original geometry is **not** parsed from the ROM by this project.
- Existing DolphinDotNet `source/model_gc.inc` supports bounded Yay0; `source/model_j3d.inc` supports J3D2/BMD3, **not J3D1/BMD1**. Existing `BtiTexture`, `Texture2D.FromBti`, `source/audio_formats.inc`, `Sound/Music` cover BTI/AFC/WSYS/AW. Reuse/extend these.
- The existing firmware font adapter is `source/ipl_font.c` + `Dolphin.Graphics.IplFont`. Do not regress it.
- `tools/DolphinDotNet.Compiler/NativeStorageBindings.cs` maps native calls for AOT; `Makefile` automatically includes C sources in `source/`.
- `tests/GameCubeFormatsTests`, `tests/IplFontSmoke`, `tests/AudioAssetTests`, `tests/ModelAssetTests`, `tests/HardwareAssetCompatibility`, `tests/DebuggerInspectionSmoke`, `docs/hardware-debug-automation.md`, `docs/gcft-jsystem-integration.md` are relevant test/integration entry points.
- [libogc `__SYS_ReadROM`](https://github.com/devkitPro/libogc/blob/master/libogc/system.c), prototype `void __SYS_ReadROM(void*, u32 len, u32 offset)`. Confirm the symbol and calling constraints in the repo's pinned **libogc2** before using it; do not assume standard libogc equals pinned libogc2.
- The encrypted region of a raw 0x200000-byte ROM begins at `0x100` and is `0x1AFE00` bytes long. The final firmware-font region must not be descrambled. Preserve existing console font behavior.

## Phase 1 — Native firmware reading and correct descrambling

Implement read-only GameCube boot ROM access with bounded reads, support an explicit user-supplied `ipl.bin` file as fallback for Dolphin/emulator/testing, and expose a small `Dolphin.System` managed entry point. Avoid writing temporary firmware copies to storage and never include ROM data in tests or commits. Raw dumps are exactly 2 MiB; parse header/revision and distinguish scrambled vs already descrambled data by validated evidence, never by a weak single magic check. Ensure constrained memory use and fail clearly if firmware is not accessible. Preserve the original ROM in memory only as long as required. Include a host-side synthetic descrambler round-trip and malformed/truncated/unsupported-source tests. **Commit Phase 1.**

## Phase 2 — Bounded resource catalog and decompression

Scan the correctly descrambled ROM for all valid Yay0 records (not just the four signature+size pairs from GC-anim-Wii). Carefully bound masks/links/literals/decompressed lengths and reject overlaps/malformed backreferences/multiple ambiguous candidates. For Yay0 data containing J3D1/BMD1, inspect the structure and expose catalog metadata/resource extraction without prematurely claiming model support. Include named intro resources (environment, tiles, three logo faces, GAMECUBE wordmark) and the raw stereo startup audio source; maintain region/revision compatibility without hardcoding absolute offsets. The GC-anim-Wii four known output sizes are `0x6140`, `0x1020`, `0x16F60`, `0x64A0` but **are only hints**. The source reads three overlapping BTI headers from the logo decompression buffer; their data offsets are relative to their individual headers. Avoid arbitrary copying with unvalidated sizes. Provide resource enumeration and extraction under `Dolphin.System` with typed metadata and explicit unsupported cases. Add tests with synthetic Yay0/J3D1/textures and corrupted payloads. **Commit Phase 2.**

## Phase 3 — Textures and font unification

Connect the catalog to existing `BtiTexture` and `Texture2D` with native GX format, palette, sampler, alpha, mipmap, lifetime, and graphics-executor rules. Provide straightforward `Dolphin.System` APIs returning existing `Texture2D` objects, plus a new `SystemFont` facade if it can be done without breaking the current `IplFont` public API. Support named intro assets on NTSC and PAL. Do not silently approximate unknown layouts; surface extraction failures. Benchmark/cache sensibly, avoid keeping the full boot ROM resident after extraction. **Commit Phase 3.**

## Phase 4 — Original stereo audio, without disk round-trips

Find and validate the appropriate WSYS/AW description and two raw AFC channels in the ROM; do not rely on the right-channel magic and 97920-byte offset alone. Reuse the existing AFC decoder and `Sound`/`Music` playback resources, adding a safe **memory-backed compressed source** if required. Preserve two channels, sample rate, duration and clean playback lifetime; no lossy mono PCM8 workaround or temporary SD files. Add known-good synthetic AFC vectors and stereo separation/loop/resource-lifetime tests. Expose ergonomic `Dolphin.System` entry points without duplicating an audio API. **Commit Phase 4.**

## Phase 5 — Authentic J3D1/BMD1 geometry and materials

Research the original `J3D1bmd1` contents of IPL Yay0 files and extend DolphinDotNet's model pipeline to interpret bounded J3D1 chunks and original geometry/mesh topology, vertex attributes, matrices, hierarchy, material/TEV state, and embedded BTIs. Share proven code with the J3D2 importer only where layouts actually match. **Do not use or embed the ripped `GC-anim-Wii/source/models.c` geometry.** If geometry is encoded in GX display lists, parse/validate rather than sending untrusted bytes directly to GX. Add positive synthetic fixtures, deliberately malformed inputs, and console rendering comparisons. Include system-menu models if discovered. Clearly document any unverified native semantics; do not pass off a flat mesh approximation as complete fidelity. **Commit Phase 5.**

## Phase 6 — Startup animation renderer using native GameCube GX

Implement a reusable original-startup-sequence controller in `Dolphin.System`, with asset/model usage from prior phases, 50/60 Hz-tolerant elapsed-time progression, camera movement, cube motion, tiles, letter/logo stages, transparency, colors and texture projection. Use the existing graphics APIs/native GX (not Wii/GRRLIB, asndlib, Priiloader or Wii reset). GC-anim-Wii's hand-tuned stage positions are reference research, not the authoritative source of geometry. Keep the animation optional and side-effect-free (no automatic boot/reset). Document any deviations from the physical IPL and enable frame capture using existing debugger/automation facilities. A rendition of the existing original IPL animation is permitted; **before creating any new custom UI, first produce a design mockup and obtain user confirmation**. **Commit Phase 6.**

## Phase 7 — Integration, hardware validation, and documentation

Wire all relevant AOT intrinsics/native bindings, GC cross-build integration and public API docs; ensure cancellation, budgets, resource disposal, GPU cache flushes, and asynchronous threading are handled consistently. Add automated host, managed and hardware smoke tests, including IPL detection from real hardware vs explicit file, both regional compressed-layout families, asset inventory, texture render, stereo sound, model load and deterministic animation captures. Use synthetic/original fixtures only in the repository; never commit a Nintendo ROM or extracted proprietary asset data. Record exactly what was tested on physical hardware vs emulated/compiled/host-only; do not claim physical verification without a successful run. Finish with a comparison to GC-anim-Wii's identified resources, a gap table, and measured budgets where measurable. **Commit Phase 7.**

## Git, verification and completion

- Work exclusively on `codex/system-firmware-assets-20261105`, targeting `main`, in the **existing draft PR**. Do not spawn a competing PR.
- Commit and push after **each** completed phase, with descriptive `system: ...` messages. If a phase is incomplete, do not label a partial implementation as complete; record missing pieces and continue where possible.
- Run at minimum `dotnet run --project tests/GameCubeFormatsTests`, `python3 tests/gcft_cli_smoke.py`, relevant audio/model/native tests, compiler/AOT tests, `python3 scripts/ci_test_suite.py graphics`, and the devkitPro GameCube build where the cloud environment supports them. Follow repository CI conventions, fix failures, and report evidence. Make no blind assumptions about a green pipeline.
- Treat external ROM data and individual games as untrusted input. Input/decode limits, overflow handling, startup safety, and no indefinite hardware polling are mandatory.
- No Nintendo ROM, music, textures, copyrighted model dumps, or GPL Dolphin Emulator source in the commits. Respect upstream MIT notices when copying licensed implementation snippets.
- **Do not merge automatically**, even if CI becomes green. Keep the PR in draft until checks are green and remaining known gaps are explicit.
- Avoid asking the user to decide API names: use your judgment, `Dolphin.System`, consistent existing conventions, and comments noting **IPL firmware origin**.
