# Manual testing TODO — entire repository

Reviewed: **2026-10-23**. Scope: `main` at `5f8f47c60647fb1d068e61484cb919a521764903`, plus the phase-1/2 follow-up through `6389e78563d8a6b8736da4cf57840ff61d458d55` ([PR #112](https://github.com/capz/DolphinDotNet/pull/112)). This covers the framework, compiler, native backends, tools, packaged SDK and their integration boundaries, not only model import. Other unmerged branches are excluded.

Every item below is **open acceptance or requalification work**, not a claim that the feature is broken or has never been tested. Historical evidence is useful but does not verify this exact revision, device, media or package. Creating this document does not execute any manual test.

## How to use this checklist

- **P0:** required before calling the applicable GameCube feature/release hardware-qualified; begin with build/boot and data-integrity cases.
- **P1:** representative compatibility, visual, performance and desktop workflow qualification; required for the corresponding advertised support.
- **P2:** experimental-platform or optional integration qualification; no implied promotion to supported status.
- Environment tags: **Host** = real desktop/toolchain; **IDE** = interactive editor; **Dolphin** = actual PowerPC DOL execution in the emulator; **GC** = physical GameCube; **Release** = intentional distribution event. A host C executable is not a Dolphin/GC run.
- Check an item only after its required environments pass and the evidence is recorded. A failed or blocked attempt stays unchecked. Missing hardware/assets/probes are **Blocked**, not Passed or Not applicable. Use Not applicable only for a deliberately excluded release configuration, with a reason.
- Samples and IDE plugins moved to [DolphinDotNet.Samples](https://github.com/capz/DolphinDotNet.Samples) and [DolphinDotNet.Debuggers](https://github.com/capz/DolphinDotNet.Debuggers). Record their exact revisions/package versions. Validate this repository's integration; do not treat those external implementations as source owned here. Several older docs still link to removed `samples/` paths.
- Prefer existing smoke projects and harnesses listed below. Where the probe cannot yet produce the required observations, record the missing probe/asset as a prerequisite; merely building it does not complete the item.

### Current automated baseline and what it does not establish

The reviewed follow-up passed [framework CI run 1509](https://github.com/capz/DolphinDotNet/actions/runs/39947357238) and [package verification run 30](https://github.com/capz/DolphinDotNet/actions/runs/39947357323). Inspect [the workflow](../.github/workflows/build.yml), [domain runner](../scripts/ci_test_suite.py) and [model acceptance runner](../scripts/ci_model_tests.py) before reusing that claim for a newer commit.

Current CI runs on macOS/Windows. It executes selected host/runtime tests, numerical/string AOT contracts, desktop converters, native model/GX/model AOT acceptance and package consumers. Several domain smoke projects are **built**, and the `gamecube` domain generates C and compiles it with the **host** compiler. It does not currently cross-link or boot GameCube DOLs, run real Dolphin scenarios, or reproduce the earlier Linux 32-bit/ASan/UBSan matrix. Storage/network/audio/async/timing smoke project presence is not evidence that each runs on the target. Historical README/doc CI claims can be broader than today's workflow.

Before a release, rerun the relevant omitted acceptance suites on a suitable host and execute the target/manual cases below. Normal PR runs do not prove NuGet publication, interactive IDE behavior or physical peripherals.

### Test kit and evidence

Use a clean external consumer, the .NET/toolchain/image pins from the tested revision, Dolphin with an isolated user directory, and a physical GameCube where required. Record console region/model, bootloader/ODE firmware, video mode/output chain, controller/adapters, BBA/USB Gecko, SD adapter/card/filesystem and memory-card identity. Test each claimed combination separately; do not infer one device's behavior from another.

Use backed-up **disposable test media and disposable saves** for writes, repair, removal-during-write and power-interruption cases. Run explicit interruption tests only as a planned fault-injection exercise on that media. Use legally supplied assets and keep private/retail assets out of public test artifacts.

For each case, append a result entry using this template. Attach logs/screenshots/audio, checksums, completion markers, timing/memory captures and issue links as appropriate. Keep environment-specific results when a case needs both Dolphin and GC.

```text
Case ID / priority:
Status: Not run | Blocked | Failed | Passed | Not applicable (reason)
Tester / date:
Framework commit / package version / external sample + debugger revisions:
Host OS + architecture / .NET / native backend + image/toolchain version:
Dolphin version + configuration OR console/peripheral/media/firmware details:
Probe + source asset hashes / reproduction steps:
Expected result / observed result:
Frame-time distribution, MEM1/managed heap/stack/native/ARAM peaks where relevant:
Evidence location / defect or blocker issue / retest reference:
```

## 1. Clean install, SDK, templates and native builds

**P0 · Host + Dolphin; P1 · IDE.** References: [SDK](nuget-sdk.md), [acceptance record](sdk-validation.md), [build setup](ide-workflow.md), [standalone builds](standalone-native-builds.md), [packaging](../eng/pack.py), [consumer probe](../tests/sdk_consumer.py).

- [ ] **SDK-01 — Fresh consumer installation.** On Windows and macOS, use an empty package cache and an external directory containing spaces. Install the candidate templates and create `gamecube`, `gamecube-game` and `gamecube-lib`; restore without a framework checkout. Expect matching SDK/Core/GameCube/template versions and actionable dependency errors; the library must perform no native build.
- [ ] **SDK-02 — Real Docker toolchain build.** Build Debug and Release applications to nonempty ELF/DOL with the pinned Linux-container backend. Test the claimed x64/ARM host combinations, including ARM emulation where used. Expect consistent artifacts and clean diagnostics for missing/stopped Docker, wrong container mode or inaccessible mounts; verify no stale successful artifact is launched after failure.
- [ ] **SDK-03 — Native backend qualification.** On each claimed Linux/macOS native-toolchain setup, build with the pinned devkitPPC/libogc2 libraries. Expect correct linker/library selection and executable DOLs. Check the documented rejection/limitation for whitespace in Native Make paths rather than assuming Docker path behavior applies.
- [ ] **SDK-04 — Consumer dependencies and relocation.** Relocate the installed feed/project; consume a library through a transitive package and another through a project reference, including Debug dependencies without PDBs. Expect correct AOT resolution and clear missing/duplicate/unsupported-API diagnostics. Repeat clean rebuild, configuration changes and `--no-build` launch; record exact output selected.
- [ ] **SDK-05 — Upgrade and tooling integration.** Upgrade a previously created external project with matching package pins, install/run Assets and SaveImage tools, and verify effects/model build inputs rebuild on dependency changes. Expect no hidden repository paths, missing bundled adapter/compiler files or native work during IDE design-time evaluation.

## 2. DOL format, boot, shutdown and platform boundaries

**P0 · Dolphin + GC.** References: [binary audit](dol-binary-audit-plan.md), [ELF/DOL validator](../scripts/verify_dol_elf.py), [smoke harness](../scripts/dolphin_smoke.py), [HelloGameCube](../tests/HelloGameCube), [platform limits](standalone-native-builds.md).

- [ ] **BOOT-01 — Cross-link and inspect actual output.** Build the current acceptance DOLs with the real PowerPC toolchain; run the ELF/DOL validator and retain map/readelf/disassembly output. Expect matching entry points, valid MEM1 ranges/alignment, preserved initialized payloads and correct BSS. Host C compilation alone cannot complete this case.
- [ ] **BOOT-02 — Cold and repeat boot.** Boot the same Debug/Release artifacts in Dolphin and each intended physical launch route (for example the selected homebrew loader/ODE). Confirm managed entry, rendered frames, input and completion/exit markers; repeat at least ten launches. Expect no loader/PPC exceptions, intermittent startup stalls or dependence on previous process state.
- [ ] **BOOT-03 — Exit and exception teardown.** Exit normally, return a nonzero result, cancel pending work and raise an unhandled managed exception with active I/O/audio. Expect the documented diagnostic/result, service-thread shutdown and resource cleanup without a hang. Verify orderly teardown on GC, not only host handle counts.
- [ ] **BOOT-04 — Region and video startup.** Exercise each advertised console region and supported interlaced/progressive video mode/output chain. Expect a stable visible frame, correctly sized viewport/UI and working input; record unsupported mode combinations explicitly.
- [ ] **BOOT-05 — Experimental platforms (P2).** Separately build/boot the supported minimal Wii path in Dolphin/physical Wii and the minimal Wii U path on its appropriate target. Expect the documented subset only; Wii U requests requiring missing platform adapters must fail generation clearly. Do not label a GameCube run Wii/Wii U acceptance.

## 3. Managed runtime, AOT, BCL and memory

**P0 · Dolphin + GC.** References: [supported .NET subset](dotnet-standard-status.md), [runtime gates](phase7-runtime.md), [collections](collections-arrays.md), [numbers](numerics.md), [strings](string-runtime.md). Probes: `tests/AotSmoke`, `ControlFlowSmoke`, `GenericSharingSmoke`, `LibrarySmoke`, `NumericSmoke`, `ExactNumberSmoke`, `StringSmoke`, `WishlistSmoke` and their host/target entry fixtures.

- [ ] **RT-01 — Execute compiled language contracts on PowerPC.** Run current probes for object/static initialization, virtual/interface dispatch, generics, boxing, arrays/byrefs, delegates and nested catch/finally/rethrow. Expect the probe success markers and reference results in Debug and optimized Release; retain target output, not merely DOL build logs.
- [ ] **RT-02 — Constrained-heap and root stress.** Force collections while holding deep/cyclic graphs, pending tasks/exceptions and native-operation owners. Exercise OOM and dispose/reload loops. Expect live references to survive, dead memory to be reusable and failure cleanup to leave a usable runtime; capture heap/stack/native peaks and allocation failures.
- [ ] **RT-03 — Numeric and text parity.** Compare fixed reference vectors for 32/64-bit integers, floats/doubles/subnormals, decimal/BigInteger, formatting/parsing, UTF-16, encodings and collections on the desktop reference and PowerPC. Expect the documented ordinal/invariant subset and matching overflow/null/range errors; do not require unsupported culture/normalization APIs.
- [ ] **RT-04 — Long-running memory stability.** Repeat at least 100 load/create/dispose cycles, then run a representative combined scene for at least 30 minutes. Expect native handles, managed occupancy, MEM1 and ARAM use to return to bounded baselines and stack/heap peaks to stay within a declared budget. Record frame-time percentiles; average FPS alone is insufficient.
- [ ] **RT-05 — Compatibility boundaries (P1).** Compile representative consumer/library calls at the exact documented overloads and deliberately request unsupported APIs. Expect actionable compiler diagnostics or documented runtime rejection, not silent desktop emulation. Iterator-block support remains unverified; qualify it explicitly before changing its compatibility status.

## 4. Async execution, cancellation and timing

**P0 · Dolphin + GC.** References: [async](async-runtime.md), [persistent worker](persistent-async-worker.md), [timing](timing.md). Probes: `AutomaticAsyncSmoke`, `AutomaticTaskSmoke`, `AsyncSmoke`, `TimingSmoke`, `ModelAsyncSmoke`.

- [ ] **ASYNC-01 — Automatic progress and affinity.** Run async Main, default awaits, `Task.Run`, `Yield`, `ConfigureAwait(false)` and `WhenAll` while a synchronous frame/input loop continues, without manual pumping. Expect documented application/background continuation placement and no graphics/input calls from the background executor.
- [ ] **ASYNC-02 — Cancellation, faults and lifetime.** Cancel queued and active file/network/model work, dispose its owner and propagate a background exception across an await under forced GC. Expect documented partial-transfer semantics, correct exception/cancellation, bounded handles and no late callback into disposed state.
- [ ] **ASYNC-03 — Timer accuracy under load.** Compare monotonic time/Stopwatch/delays/callback timers against an independent clock during rendering, network and DMA load. Test zero/infinite delays, cancellation, pause/resume and frame clamping. Set acceptable jitter/frame budgets before measurement; expect no early deadline, runaway callbacks or timer delivery after disposal/shutdown.
- [ ] **ASYNC-04 — Service shutdown and contention.** End Main while tasks/DNS/storage operations are pending and repeat startup/exit. Expect the documented drain/cancel behavior and joined service thread. Measure contention and responsiveness without assuming true parallel managed execution or enabling the unfinished general multithreaded GC integration.

## 5. Graphics, textures, effects and display output

**P0 visual correctness; P1 performance · Dolphin + GC.** References: [graphics](graphics.md), [effects](effects.md), [TEV materials](tev-materials.md), [shader scope](shader-support.md). Probes: `GraphicsSmoke`, `TevMaterialTests`, [shader capture harness](../scripts/dolphin_shader_test.py); external ShaderDemo where applicable.

- [ ] **GX-01 — Baseline pixels.** Render indexed/textured meshes, camera/transform hierarchies, normals under nonuniform/negative scale, culling, depth, cutout and alpha blending. Capture known geometry/colors in Dolphin and GC. Expect correct winding, placement, depth and color/alpha at each supported video mode; keep reference images and tolerances.
- [ ] **GX-02 — State transitions.** Alternate model/mesh passes, raw GX, Draw2D, font/UI, scissor/viewport and overlay in one frame. Expect no leaked texture/TEV/depth/blend/light state, correct frame/pass rejection and unchanged later draws. Reproduce in both Debug and Release.
- [ ] **GX-03 — Textures and samplers.** Inspect all implemented native formats (I4/I8/IA4/IA8/RGB565/RGB5A3/RGBA8/CMPR), palettes on applicable import paths, mip levels and nearest/linear/wrap/mirror/clamp behavior. Expect correct tile/channel/alpha ordering, no stale-cache updates and no unexpected seams at boundaries; retain close-up images.
- [ ] **GX-04 — Effects and material overrides.** Exercise compiled effects and toon/metallic/environment presets, auxiliary maps with zero alpha, imported/skinned overrides and default-state restoration. Compare independent expected colors and run capture negative controls. Expect secondary RGB contributions without accidental secondary-alpha ownership; per-vertex reflection seams remain a documented limitation.
- [ ] **GX-05 — Display and resource lifetime.** Inspect overlay/fonts/UI at 480- and 576-line layouts and on real output hardware; repeatedly create/replace/dispose meshes, textures, fonts and effects. Expect readable, unclipped output and no GPU use-after-free, flicker or unbounded native memory growth.

## 6. Model compatibility and phase-1/2 acceptance

**P1 · Host + Dolphin + GC.** References: [model runtime](model-assets-runtime.md), [phase 2](phase2-materials.md), [studio bake](neutral-studio-baking.md), [source import](model-source-import.md). Probes: `ModelAssetTests`, `ModelSmoke`, `ModelAsyncSmoke`, `model_test.c`, `model_effect_test.c` under `tests/`.

- [ ] **MODEL-01 — Representative desktop formats.** Qualify real FBX/OBJ/DAE/3DS/STL/PLY exports against the exact supported Assimp version on each claimed host. Include external/embedded textures, multi-material geometry, hierarchy, axes/units and animation where supported. Expect source-comparable output or explicit documented rejection; record the known STL/PLY-to-GLB exporter failures instead of treating adapter routing as qualification.
- [ ] **MODEL-02 — glTF compatibility in pixels.** Render indexed/nonindexed strips/fans with degenerates, sparse accessor replacements with/without base data, UV1 and rotated/scaled/offset texture transforms. Compare to an independent glTF renderer. Expect correct geometry, winding and selected UV stream; malformed/unsupported data must fail without publishing a partial model.
- [ ] **MODEL-03 — Secondary-only regression.** Use emissive-only, AO-only and combined maps without a base texture, including transformed UV1, missing selected UVs and constant emission without UVs. Expect correct baked RGB/alpha and transform, explicit missing-UV rejection, and successful UV-free constant emission. Compare actual rendered output as well as converter bytes.
- [ ] **MODEL-04 — Alpha and global ordering.** Compare glTF BLEND/MASK, OBJ MTL opacity and FBX raw/classic transparency against references. Put opaque/cutout and translucent submeshes in multiple transformed/skinned objects; test depth ties and camera movement. Expect `DrawModels` opaque-first/global stable submesh order, preserved alpha and no frame-time allocations. Record the older origin-sort helper's limits; intersecting/per-triangle transparency is not promised.
- [ ] **MODEL-05 — DNMD v1/v2 and TEV fidelity.** Load ordinary v1 and opt-in v2 packages on the current runtime; inspect base/AO/emission, unlit, sampler behavior and per-draw override precedence. Expect documented alpha ownership, runtime response and version errors. Unload/restore through ARAM and repeat; the restored image and material choices must match.
- [ ] **MODEL-06 — Animation and cancellation.** Inspect bind pose, STEP/LINEAR/CUBICSPLINE, ancestor animation, crossfade, two independent instances, joint attachments and weighted normals. Cancel cold loads/restores during frame rendering and forced GC. Expect source-reference poses, no corrupt first/restored frame and complete resource cleanup.
- [ ] **MODEL-07 — Neutral-studio reference acceptance.** Prepare legal textured spheres and production assets with valid static atlases. Reproduce the exact documented rig/view/BRDF/exposure/display curve in an independent renderer; agree image/seam tolerances before comparison. Check normal maps, metallic/roughness, AO-on-ambient, independent emission, unlit transfer and alpha. Fixed-view baked highlights must not be described as dynamic PBR.
- [ ] **MODEL-08 — GX display and budgets.** On GC, inspect studio and responsive GX packages for gamma/color, alpha edges, mip/UV seams and front/back appearance. Measure converter/package size, MEM1/ARAM and frame times across representative vertex/joint/material counts. Expect declared budgets and documented limits, not just successful loading.

## 7. Native GameCube asset families

**P1 · Dolphin + GC; legal reference assets required.** References: [format matrix](live-model-formats.md), [F-Zero GX](fzero-gx-loading.md), [J3D/RE4](gamecube-model-formats.md), [RenderWare/Melee](renderware-melee-loading.md), [PSO Episode III](pso-episode3-loading.md).

- [ ] **ASSET-01 — PSO/Ninja profiles.** Inspect supported GJ/GVM/NMDM and Episode III PRS/BML/REL/Ninja content, explicit entry selections, textures, hierarchy and supported motions against independent decoding. Expect only the documented profiles; distinguish offline GJ/XJ geometry-only limits from native textured/animated loaders.
- [ ] **ASSET-02 — F-Zero GX profiles.** Compare GMA/GCMF geometry, TPL palettes/formats, blend modes, compressed/archive selection and bounds against an independent viewer. Expect correct textures/placement and bounded load/ARAM restore. SKL/MTA/MAL/COLI animation and unqualified related-game variants are not passing requirements for this subset.
- [ ] **ASSET-03 — J3D and RE4 profiles.** Inspect supported BMD/BDL/BTI/BCK/BCA, envelope skinning, archives/compression and RE4 BIN/TPL/FCV/DAT/DRS selection with the correct GameCube release assets. Expect matching supported poses/materials and explicit refusal of unsupported channels/layouts; identical file extensions across ports do not establish support.
- [ ] **ASSET-04 — RenderWare and HAL profiles.** Compare documented DFF/TXD and Melee/HSD structures, strips/palettes/animation where implemented, with representative assets. Expect correct profile selection and clear unsupported-pipeline errors. Do not infer full Sonic Heroes, Burnout 1/2 or every HSD game's compatibility from synthetic fixtures.
- [ ] **ASSET-05 — Failure cleanup and asset provenance.** On target, load missing/truncated/over-budget/mismatched companion files and cancel archive loads; then load a valid asset. Expect bounded failure without leaked file/model/native handles. Retain asset hashes and private reference locations so visual claims remain reproducible without distributing game data.

## 8. Streaming graphics, fences, asset I/O and GPU metrics

**P1 · Dolphin + GC; throughput requires GC.** References: [streaming foundation](streaming-rendering-foundations.md). Present code includes these experimental APIs even though the document names their original feature branch.

- [ ] **STREAM-01 — Native texture publication.** Stage array/native-buffer uploads during rendering, publish on the application executor, rebind materials, and alternate both allocations rapidly. Expect old texture data to remain valid until frame completion, new pixels after publish, correct cache invalidation and no partially uploaded page or publish during invalid raw GX state.
- [ ] **STREAM-02 — Fence and retirement ownership.** Insert frame fences and retire resources while submitting subsequent frames, including queue capacity/error cases. Expect completion only after the corresponding GX frame finishes and disposal only when safe. A frame marker is not a sub-frame GPU timestamp; retained materials must not reuse retired resources.
- [ ] **STREAM-03 — AssetStream boundaries.** Read archive chunks at aligned/unaligned file offsets into valid native destinations, including EOF/short files, cancellation, one-in-flight enforcement and disposal. Expect exact returned counts/bytes, correct buffer ranges and no stream/native-buffer reuse while work owns it.
- [ ] **STREAM-04 — RenderQueue and counters.** Compare batched/unbatched scenes sharing material/transform objects, distinct transforms and translucent meshes across pass/raw/2D transitions. Expect identical images and only documented coalescing. Cross-check profiler draw/bind/triangle counts against a known scene; do not report software counters as GPU busy time or bandwidth.
- [ ] **STREAM-05 — Real storage/DMA throughput.** Profile representative streamed texture/geometry traffic from SD2SP2, original DVD and supported GC Loader with audio active. Record stalls, frame-time distribution, MEM1 double-buffer cost and ARAM contention. Set resource/frame budgets first; page tables, residency policy and a complete megatexture engine remain future implementation.

## 9. ARAM, DMA and caching

**P0 · GC; Dolphin where emulated.** References: [ARAM](aram-runtime.md), [streaming](streaming-rendering-foundations.md). Probes: `AramSmoke`, `aram_test.c`, `ModelSmoke`.

- [ ] **ARAM-01 — Physical DMA correctness.** Round-trip patterned data across allocation/chunk boundaries, zero-length operations and supported unaligned byte-array ranges; verify neighboring bytes and full checksums. Expect correct CPU/cache/DMA ordering on real hardware and no retained managed pointers across asynchronous transfers.
- [ ] **ARAM-02 — Shared request slot and lifetime.** Interleave ordinary transfers with aligned native-buffer `AramTransferQueue` requests and audio. Cancel/dispose while requests are queued/active. Expect busy retry/serialization, retained MEM1/ARAM owners until completion and no reuse/corruption of an in-flight region; measure frame/audio impact.
- [ ] **ARAM-03 — Capacity, eviction and restoration.** Fragment/coalesce allocations, exhaust slots/bytes, prefetch and evict cache entries, and restore a model after deleting its original source file. Expect documented cache replacement/residency behavior, intact poses/materials and bounded cleanup after failures.
- [ ] **ARAM-04 — Long-run coexistence.** Run recurring uploads/readbacks, cache pressure, model restores and compressed audio for at least 30 minutes. Expect stable checksums and budgets with no DMA hang or audible underrun. Capture transfer latency and stalled-request evidence; hardware faults cannot be inferred from host fake completion.

## 10. Filesystems, disc mounts and device inventory

**P0 · GC; image-backed cases also Dolphin.** References: [storage](storage-runtime.md), [device inventory](API_REFERENCE.md#dolphinhardware--non-destructive-device-inventory). Probes: `StorageSmoke`, `DiscIdentityTests`, `ConnectionSmoke`, `hardware_gamecube_test.c`, `disc_mount_test.c`; external DiscBrowser.

- [ ] **FS-01 — Claimed media matrix.** On each available slot SD adapter, SD2SP2 and supported GC Loader firmware, mount FAT32/exFAT test volumes, enumerate, read/write/seek/flush/close/unmount and verify hashes after reboot. Include empty/full/near-full/read-only/missing media. Expect documented support or explicit mount/I/O failure; never infer support from another adapter/filesystem.
- [ ] **FS-02 — Naming and file semantics.** Test long/UTF-8 names, supported non-ASCII spelling/casing, directory traversal, copy/move/delete, partial I/O and open-handle unmount rejection. Expect the documented .NET subset and filesystem-specific behavior; record normalization/case/encoding limits instead of requiring unsupported locale folding.
- [ ] **FS-03 — Original and image-backed discs.** Mount original FST discs, ISO9660 data media and supported ISO/GCM files through their distinct APIs. Check identity, banner fallback, language selection, directories and file bytes. Switch images/media after closing/unmounting. Expect no stale identity/bytes and enforced source-volume lifetime; compressed/Wii images and files beyond the supported size are rejected.
- [ ] **FS-04 — ODE and DVD arbitration.** Test raw `gcloader:/` access versus selected-disc access on each claimed firmware. Expect safe mount/switch behavior or clear unsupported/busy results. Confirm actual media paths and simultaneous-use limits; host ISO tests cannot prove GC Loader firmware behavior.
- [ ] **FS-05 — Inventory and connection snapshots.** Insert/remove cards/controllers/SD adapters between Safe/Extended inventory and connection updates, including an SD device owning a card slot. Expect non-destructive probing, correct cached/Busy/Unknown states and single connection transitions, without mounting/resetting media or treating presence as proof of writability.
- [ ] **FS-06 — Other inventory classes (P1).** With available GBA links, modem and Game Boy Player hardware, inspect supported inventory entries and native IDs while ARAM/EXI owners are active. Expect only implemented read-only detection and honest Unknown/Unsupported results; Game Boy Player detection is currently Unknown, and inventory does not imply a communication/gameplay backend.

## 11. Memory-card saves and save artwork

**P0 · GC; use disposable saves/cards.** References: [save APIs and recovery](storage-runtime.md), [SaveImage](../tools/DolphinDotNet.SaveImage/README.md). Probes: `card_transaction_test.c`, `SaveImageTests`; external Snake or a dedicated save probe.

- [ ] **SAVE-01 — Slot/identity and round trip.** Create/read/update/delete a test save in slots A/B under explicit game/company identity, including small/full/unformatted/wrong-identity cards. Verify after reboot. Expect correct listing/metadata, complete payload checksums and errors without modifying unrelated saves or implicitly formatting a card.
- [ ] **SAVE-02 — Failure/recovery matrix.** On scratch cards, interrupt or remove media during planned allocation/write/close/metadata/publication stages, then reboot/remount and recover. Expect a complete documented old/new save or explicit recoverable error, with no unrelated data loss; record device-specific outcomes at each checkpoint.
- [ ] **SAVE-03 — Real save-browser output.** Prepare supported icons/banners/comments with SaveImage, write metadata and inspect the console browser. Expect correct RGB5A3/transparency, dimensions, supported animation timing, strings and fallback for absent/malformed art; a packed-pixel host test is insufficient.
- [ ] **SAVE-04 — Polling and ownership races.** Insert/remove/reinsert while a mount is cached or a save is active, then dispose/remount under the desired identity. Expect stable UI transitions, surfaced I/O failure and no stale mount owner/handle after retry.

## 12. Contiguous writes and explicit file optimization

**P0 data integrity; P1 throughput · GC · destructive fault injection only on disposable media.** References: [storage policies](storage-runtime.md); probes `contiguous_write_test.c`, `file_optimize_test.c`, `file_extents_test.c`; external StorageIntegrity if available.

- [ ] **CONTIG-01 — Extents and default policy.** On FAT32 and exFAT, compare default-off writes, scoped/opt-in writes, explicit contiguous creation and `Defrag.Analyze/Optimize`. Independently inspect extent counts and file hashes before/after. Expect reported outcomes to match physical extents, correct threshold/scope/statistics and no false contiguity claim on unsupported host volumes.
- [ ] **CONTIG-02 — Space and concurrency failures.** Exercise existing target, no contiguous space, near-full volume, source underrun, cancellation and a competing writer on the same volume. Expect explicit bounded failure, intact existing destination and recoverable temporary/journal state; verify after remount.
- [ ] **CONTIG-03 — Planned interruption recovery.** Run scratch-card create-new/repair operations with intentional power cuts/removal at documented checkpoints, then reboot and verify checksums, publication and unrelated files. Repeat across media/firmware combinations. Keep interrupted cases open until the documented recovery outcome is demonstrated.
- [ ] **CONTIG-04 — Performance qualification.** Benchmark representative 1 MiB writes and 16/64 KiB buffers on supported SD2SP2/GC Loader media, including fragmented and near-full volumes. Record throughput, write amplification, latency and card/firmware details. Expect intact bytes and truthful optimization outcomes; do not claim a whole-card defragmenter.

## 13. Audio decoding, playback and coexistence

**P0 audible correctness; P1 capacity/performance · Dolphin + GC.** References: [audio](audio.md), [format profiles](gamecube-audio-formats.md). Probes: `AudioFormatsSmoke`, `AudioAssetTests`, `audio_formats_test.c`, `audio_backend_test.c`; external AudioDemo.

- [ ] **AUDIO-01 — Listen to supported profiles.** Compare WAV/PCM and documented DSP/ADX/AFS/AFC/HPS/SSM/AST/JAudio/RWS/AWD/RE4/DTK profiles against independently decoded reference audio. Verify rates, mono/stereo ordering, bank/segment/layer/paired-file selection, duration and end-buffer draining. Expect correct audible output on GC, not only sample-vector equality.
- [ ] **AUDIO-02 — Loop boundaries.** Listen to intro/embedded/custom loops, partial ADX-frame boundaries and independent voices with different ranges. Record loop captures and sample positions. Expect the documented exclusive-end interval without clicks, repeated/missing samples or predictor drift; unsupported WAV/PCM custom ranges must reject clearly.
- [ ] **AUDIO-03 — Controls and resource pressure.** Exercise overlapping voices, per-voice/music volume and pitch, stop/restart and disposal during playback; approach clip/voice/native-memory limits. Expect independent controls, clean refusal/exhaustion, no stale-channel control and no use-after-free. Do not require unimplemented pan or pause/resume controls.
- [ ] **AUDIO-04 — Sustained combined load.** Increase compressed voices while rendering, networking and ARAM DMA are active for at least 30 minutes. Measure callback CPU cost, frame times, underruns and MEM1/ARAM use. Declare a supported voice/load budget from results; compressed resident playback must not be called disk streaming.
- [ ] **AUDIO-05 — Invalid and unsupported sources.** Try truncated files, wrong companion banks, over-budget archives and unsupported encrypted/platform codecs on target, followed by a valid clip. Expect explicit failure, no speaker garbage/hang and released resources; do not infer title-wide audio-engine/sequence support from a container reader.

## 14. Controllers, contexts and rumble

**P0 · GC; digital/analog injection also Dolphin.** References: [input](controller-input.md), [controller harness](../scripts/dolphin_controller_test.py); probes `ControllerDolphinSmoke`, `InputSmoke`, `InputContextSmoke`.

- [ ] **INPUT-01 — Four physical ports.** Test simultaneous buttons, both sticks, analog triggers, deadzones/calibration and reconnects on every claimed controller/adapter type and port. Expect independent snapshots and one-frame pressed/released/connection transitions after a single update per tick; disconnected devices must not leave stuck inputs.
- [ ] **INPUT-02 — Routing and rebinding.** Combine gameplay/UI contexts, modal capture, consume/block rules, press/release rebinding, cancellation and timeout. Expect input to reach only eligible contexts, capture to wait for the correct release, and no swallowed/stuck action when a context is removed.
- [ ] **INPUT-03 — Physical rumble timeout.** Start/replace/cancel timed rumble, then stall the managed update loop, pause, unplug or exit. Expect native alarm-driven expiry and motor stop on disconnect/shutdown even when callbacks do not run. Measure duration; emulator flags do not establish motor behavior.
- [ ] **INPUT-04 — Diagnostics and device pressure.** Show input state while hot-plugging and while media/network probing contends for hardware. Expect responsive controls and honest unknown/busy inventory without unintended transitions or rumble.

## 15. BBA networking, DNS, HTTP and bounded JSON

**P0 · GC + controlled LAN peer; Dolphin separately.** References: [network contract](network-runtime.md), [async](async-runtime.md); probes `NetworkSmoke`, `network_fixtures.py`, `network_native_test.c`, external AsyncNetworking/HttpTexture.

- [ ] **NET-01 — Initialization and link failures.** Test absent BBA, cable unplugged, DHCP success/timeout, explicit static IPv4 and configured/disabled DNS servers. Expect accurate readiness/errors and responsive application progress. Follow the documented restart requirement after native initialization failure rather than assuming Stop resets the interface.
- [ ] **NET-02 — Real LAN transports.** Exchange UDP (including empty/oversized datagrams) and framed TCP traffic with a controlled peer; test listener accept, partial reads/writes, EOF, timeouts, cancellation and socket exhaustion/reuse. Expect correct endpoints/bytes, distinct truncation/WouldBlock/EOF outcomes and cleanup within documented limits.
- [ ] **NET-03 — DNS and HTTP integration.** Use a controlled resolver/server to exercise A/CNAME/cache expiry/TCP fallback; HTTP fixed/chunked/close-delimited bodies, redirects, status/errors and streamed transfers into files/ARAM. Expect exact hashes, bounded buffers, stripped cross-authority credentials and explicit malformed/truncated-response rejection. Keep HTTPS unsupported; do not use live public services as the only oracle.
- [ ] **NET-04 — Loss and cancellation during gameplay.** Remove cable or stop the server while downloading/rendering/audio are active; cancel/dispose pending requests and retry only supported lifecycle paths. Expect surfaced failure/cancellation, no leaked sockets/tasks and maintained frame/input responsiveness.
- [ ] **NET-05 — Bounded JSON and capacity.** Parse representative UTF-8 responses and malformed/deep/oversized inputs through the tokenizer under GC pressure. Expect documented grammar/depth/string/input limits and typed conversion failures. Measure task/buffer costs; this is not reflection serialization or general web-stack conformance.

## 16. Interactive IDE debugging and native transport

**P1 · IDE + Dolphin; P0 for hardware-debugging claims · GC with each transport.** References: [debugger boundary](debugger-components.md), [Visual Studio](visual-studio-debugging.md), [Rider](rider-debugging.md), [native plan](on-device-debugging-plan-a.md); probes `DebuggerStackSmoke`, `DebuggerInspectionSmoke`, `dolphin_smoke.py` with the pinned packaged adapter.

- [ ] **DBG-01 — Install and launch workflows.** Install the matching external VSIX/Rider plugin, open a fresh packaged SDK consumer and exercise toolbar Play/Debug/attach on each claimed IDE/OS. Expect correct DOL/ELF/source selection, isolated emulator settings, actionable missing-backend/adapter errors and no unintended CLR attach.
- [ ] **DBG-02 — Source debugging.** In real Dolphin, bind/hit consecutive C# breakpoints, pause/resume, step into/over/out and inspect managed stack, fields, strings and locals through supported shapes. Expect accurate line/frame identity and refreshed values; record unsupported inspection explicitly.
- [ ] **DBG-03 — Identity and lifecycle failures.** Rebuild with stale/missing PDB/map/ELF, move source paths, detach/reconnect, stop the guest and abruptly kill/restart the adapter. Expect identity mismatch rejection, useful diagnostics, clean port/process release and no stale breakpoint/report reuse.
- [ ] **DBG-04 — BBA and USB Gecko targets.** Separately attach to a physical GameCube over each claimed transport; repeat breakpoint/step/pause/disconnect/recovery and verify packet/timeout behavior under load. Expect the correct transport capabilities; an emulator TCP session does not establish serial or native BBA behavior.
- [ ] **DBG-05 — Independent subsystem coexistence.** While debugging, exercise supported application networking, audio and storage without assuming the native GDB stub shares the application network stack. Expect documented ownership and no corruption/stall; the native BBA stub does not currently promise application UDP heartbeat.

## 17. Overlay, hardware status and live heartbeat

**P1 · Dolphin + GC.** References: [overlay](debug-overlay.md), [debugger plan](on-device-debugging-plan-a.md); probes `overlay_test.c`, `debug_heartbeat_test.c` and the pinned debugger integration.

- [ ] **OBS-01 — Visual and metric accuracy.** Toggle the overlay over real gameplay at supported resolutions and compare frame/heap/native/ARAM figures to controlled allocations and independent timing. Expect legible layout, reset/cadence behavior and no material/GX state changes; distinguish allocator/arena/free-heap metrics.
- [ ] **OBS-02 — Physical connectivity.** Connect/unplug BBA cable and contend for EXI while showing/hiding stats. Expect honest link/unknown state, preserved configured IP and no hidden network initialization, DHCP or reset caused by observation.
- [ ] **OBS-03 — Heartbeat truthfulness.** With an application-BBA route to the configured adapter, show/hide stats, pause/resume, kill the adapter without cleanup, inject stale/session-mismatched replies and reconnect. Expect checking/waiting/connected/lost/disconnected states at documented cadence/lease boundaries; hiding stops traffic and reconnect requires the new session token.
- [ ] **OBS-04 — Post-detach retained report.** Detach cleanly and inspect the ELF-bound telemetry/report slot, then rebuild/restart. Expect disconnected/fallback reporting without writes to mismatched metadata or a previous session; the report alone must not be treated as proof of a live transport.

## 18. Launching another DOL and reboot behavior

**P0 · Dolphin + GC.** References: [handoff contract](dol-launching.md), [system API](system-api.md); probes `DolLauncherSmoke`, [replacement builder](../tests/build_dol_replacement.sh).

- [ ] **DOL-01 — Independent runtime handoff.** Prepare a trusted replacement from supported SD/DVD/file and in-memory routes, request launch and return successfully from Main. Expect old audio/input/network/storage shutdown, correct replacement data/BSS and a working new libogc thread/runtime. Confirm on hardware; a host preparation test is insufficient.
- [ ] **DOL-02 — Ownership and refusal cases.** Cancel async preparation, dispose unused preparations, request a second launch, fail Main, and try malformed/overlapping/unsupported/over-budget images. Expect early explicit refusal or documented cancellation and released staging, with the current app still usable; no partial jump or leaked preparation slot.
- [ ] **DOL-03 — Repeated handoff and reset.** Chain/relaunch the trusted acceptance images repeatedly and exercise documented reboot/reset options for the target. Expect stable video/input, no stale service/DMA/resource state and correct startup after each transition. Record loader/firmware/console differences.

## 19. Release and distribution acceptance

**P1 · Host + IDE + Dolphin/GC as applicable; Release only for publishing.** References: [release process](releases.md), [actual publish workflow](../.github/workflows/nuget-preview.yml), [acceptance record](sdk-validation.md).

- [ ] **REL-01 — Documentation-to-package audit.** Follow the current README/SDK instructions from a clean machine and inspect package licenses, versions, source/tool/adapter contents and external repository references. Expect reproducible commands and accurate platform/CI claims; file defects for stale `samples/` links or historical acceptance wording rather than treating them as current proof.
- [ ] **REL-02 — Candidate release matrix.** Run SDK-01–05, BOOT-01–04 and the applicable feature cases using the exact candidate artifacts on the declared OS/device matrix. Repeat offline restore from an already populated local feed where supported. Expect the same candidate to build, launch and debug; record package hashes and exclusions before promotion.
- [ ] **REL-03 — Publishing configuration inspection.** Inspect package ownership, trusted-publishing owner/scope, `NUGET_USER`, workflow/environment names, required reviewers and main-only restrictions against the actual workflow. Expect ordinary PR/default verification to perform no publication. Configuration inspection alone does not prove token exchange or ingestion.
- [ ] **REL-04 — Authorized publication acceptance.** Only during an intentionally approved release, dispatch publication of the exact verified artifact; verify OIDC exchange, intended package/version ownership, ingestion and clean-consumer installation. Investigate partial uploads explicitly. Leave Blocked until a real release is authorized; never publish just to close this checkbox.

## Implementation gaps that manual testing cannot close

These are follow-up engineering scope, not hidden acceptance tasks. Testing should confirm clear rejection/documented limits where applicable. Create separate implementation work before adding positive acceptance claims for:

- General reflection/dynamic assembly loading, full .NET/BCL conformance, unsupported overloads/globalization, TLS/HTTPS and general multithreaded managed execution.
- Complete source-renderer/dynamic PBR parity, advanced material extensions/IBL/shadows/GI, compressed glTF decoding, morph/complex deformation and automatic charting/pose extraction; see the [studio checklist](neutral-studio-baking.md#still-to-do).
- A full sparse virtual-texture/megapage system, scheduler/residency policy and precompiled geometry pipeline; the present streaming APIs are foundations.
- Generic GameObject destruction/reconstruction from ARAM snapshots; current byte streaming/model residency is not scene serialization.
- Unsupported title/platform-specific model/audio variants, encrypted codecs, sequence/instrument engines, MP3/Ogg/AHX decoding and audio disk streaming.
- Per-triangle/intersecting transparency resolution, seamless per-vertex environment reflections and full MikkTSpace fallback/advanced sampling where not implemented.
- Wii/Wii U backend capabilities beyond their documented experimental subset, and native BBA-stub application heartbeat support.

## Closing a qualification pass

- [ ] **SIGNOFF-01.** Link environment-specific evidence for every applicable P0/P1 item; assign an owner/issue to each Failed or Blocked case and state intentional exclusions. Keep the corresponding feature's qualification open until its prerequisites and reruns pass.
- [ ] **SIGNOFF-02.** Update feature docs/compatibility/release notes with the exact tested revision, device/profile limits and remaining gaps. Preserve the distinction between host tests, actual PowerPC/Dolphin execution, visual comparison, listening tests and physical-console results.
