# System firmware resource status

## Menu-audio follow-up

Ten cube/options effects are implemented for the fingerprinted NTSC
1.2 / PAL 1.2 `ipl-menu-bms-v2` event/bank/DSP profile. This adds `Back`,
`OptionCancel`, `OptionConfirm`, `OptionMove1`, `OptionMove2`, `OptionMove3` and
`OptionSelect` to `TurnSide`, `TurnCenter` and `Select`. Live original NTSC 1.2 event/instrument
traces and isolated emulated output comparisons qualify the recipes; PAL's
event handlers and identical resource content were checked separately. Generated
PCM16, FIR, resampler, lifetime, budget and forced-GC AOT tests pass. The EXI
fixture completed 30 ordinary Sound playbacks in Dolphin 2503 LLE after disposing
the source image/catalog. WSYS inventory exposes bounded bank-qualified wave IDs
and base keys; menu metadata exposes each effect's source IDs. The qualified
catalog retains 77,984 encoded menu bytes. Early NTSC's
different DSP setup, `PressStart`, memory-card effects and ambience remain unavailable. This is not
bit-identical output or physical-console qualification. See the detailed
[menu evidence matrix](system-menu-audio-research.md).
The supported startup audio described below remains a separate feature.

Phases 1–6 have supported-profile implementations. Phase 7 integration, automated
acceptance fixtures and documentation are implemented; the evidence below keeps
host, compiled, emulated and physical results separate. No Nintendo firmware or
extracted proprietary assets are included. PR #152 tracks review and validation;
merging remains a separate manual action.

| Phase | Implementation | Qualification limits |
| --- | --- | --- |
| 1 | Bounded read-only EXI; explicit file source/fallback; retail headers, revision/source metadata, descrambling and independent encoding evidence; cancellation and ownership | No physical console run |
| 2 | Bounded Yay0 inventory/extraction, J3D1 section metadata, six named textures, nine named models and compact/extended WSYS/AW | Qualified against NTSC 1.0/1.2 and PAL 1.2; unseen or ambiguous layouts fail |
| 3 | Existing caller-owned Texture2D/BTI pipeline, metadata-only caching, SystemFont facade and unchanged IplFont compatibility | Physical texture/font comparisons unavailable |
| 4 | Original adjacent stereo archive association, exact 174,067 samples/channel at 32 kHz, native memory-backed Sound/Music, no temporary files | Physical stereo output unverified |
| 5 | Original J3D1 geometry/topology, attributes, rigid matrices and hierarchy through the shared importer; separate authored MAT1 TEV/channel/projection/raster state and embedded textures | One channel/stage and at most one generator; unseen fog/NBT/multistage programs reject; original lighting unmeasured |
| 6 | Optional elapsed-time startup controller and existing GX renderer; five original models, tiles, cube/camera motion, logo/letters, colors, transparency, projection and deterministic seek/captures | Motion curves, camera framing, tile placement and headlight are reconstructed; no pixel-identical physical IPL claim |
| 7 | AOT bindings, firmware heap selection, metadata CLI, CI scheduling, managed/native/GC/ARAM tests, separate EXI/file acceptance builds and capture scripts | Physical validation and physical peak/latency measurements remain unavailable |

## Original input qualification — 2026-11-05

Online NTSC 1.0, NTSC 1.2 and PAL 1.2 research inputs were obtained outside Git at
the user's request. Length, CRC32, MD5 and SHA-1 match the libretro catalog;
[format research](system-firmware-format-research.md) records provenance and hashes.
The complete production CLI finds 16/16/33 Yay0 records respectively (14/14/31
boot-region records plus two unencrypted fonts), all six named textures,
all nine named models and the original exact stereo startup in each input.
All 27 original models pass native geometry/material import and release cleanly.
Model identities use bounded joint names, never absolute ROM addresses.

The native AFC decoder's original output is bit-identical to the external Wii
reference decoder: exactly 174,067 stereo frames; PCM SHA-256
`ae3e194d404f0022daf33ec1448ae71bcbd0901d76db32619f545d96ba6d490c`.
All three regional inputs produce the same compressed startup and PCM hashes.
The final encoded frame has unused capacity; it does not add samples to playback.

## Host and compiled validation

Passed locally:

- Managed firmware tests: synthetic cipher/reference vector; retail header and
  source errors; cancellation; malformed/overlapping Yay0, J3D1, TEX1, WSYS and FST;
  both record families and six texture profiles; immutable metadata ownership;
  equal-time 50/60 Hz progression, seeking, fade and restart.
- GameCubeFormatsTests (88 assertions) and GCFT desktop archive roundtrip.
- Full graphics domain: existing native converters, GX recording, ModelSmoke,
  ARAM roundtrip and forced GC, texture/effect/font and display tests.
- Rebase integration: retained main's mip-chain support, opaque grouping, drawing
  telemetry and Intel CI runners. Generated GX recording covers private authored
  instance colors in grouped draws and restores the indexed headlight across
  ordinary/authored/ordinary submissions.
- Generated J3D1/MAT1 fixture: validated triangles/hierarchy, signed TEV colors,
  independent RGB/alpha operations, projection, depth/blend/cull/channel state;
  malformed tables/opcodes/truncation, budgets, source ownership, private instance
  pose/colors and retained authored state over unload/restore.
- Expanded firmware AOT bridge: console and mounted-file sources, malformed and
  unavailable-console fallback, named model import, node/color controls, textures,
  original-length stereo Sound/Music, loops, cancellation and independent lifetimes.
  No live file/draw/model/voice/clip handles remain. Forced-GC run: 1,025 reads,
  463 collections, 2,103,816 heap bytes occupied at exit (not peak).
- Native suite in standard and O3/LTO configurations; audio containers/vectors,
  independent voices, loops and memory ownership; CI selection/workflow scheduling.
- New model/GX fixture under AddressSanitizer and UBSan. LeakSanitizer is disabled
  under tracing; explicit handle/lifetime assertions remain enabled.
- Metadata CLI schema 2, all three original inventories and malformed sources.
  Structural model metadata reports `NativeImportRequired`; it does not falsely
  certify geometry. No CLI export contains vertices, pixels, samples or ROM bytes.

The expanded EXI/model/animation fixture cross-builds with the pinned devkitPro /
libogc2 image and produces aligned DOL/ELF outputs. Firmware-reachable bundles
select an actual 8 MiB heap; ordinary programs retain 256 KiB. This is compiled
GameCube compatibility, not physical execution. Current remote checks are linked
from [PR #152](https://github.com/capz/DolphinDotNet/pull/152); do not infer their
conclusion from a successful local build.

## Emulated versus physical evidence

Dolphin successfully loaded the original PAL 1.2 ROM through its emulated
EXI device. The expanded fixture reads it using the bounded native driver,
creates its catalog and renders six textures, nine model views and seven exact
animation times. The accepted visual run contains 43 XFB captures with matching
fixed-frame pairs, distinct textures/stages and a different fade/hold image.
The final wordmarks are visible and the original logo/geometry render through GX.
Normal B exit reaches `FIRMWARE HARDWARE RESOURCES RELEASED` and `MANAGED MAIN: OK`;
guest `dnd_models` and `dnd_instances` tables are entirely zero after disposal.
Captures and raw firmware-derived artifacts remain outside Git.

Renderer qualification used commit `dad475c` and DOL SHA-256
`488d45c02997adea8ae2c1a3ee69926887ffd7d4ae7b2d7075dc1b697f899026`.
The fixture reports 1,304,336 bytes of startup native budget charges after
preparation; this includes per-model texture charges and is not a unique-memory
peak. Bare model previews preserve authored rest alpha, so the wordmark may be
invisible until the animation activates its materials.

The earlier visual run deliberately omitted audio initiation. That Dolphin version reported
unsupported AESND HLE microcode `8d527c50`; an audio attempt reached managed
resource release but stalled in SDK DSP reset. An LLE attempt did not reach the
first frame within the diagnostic deadline. Those visual captures do not prove
audio playback or DSP shutdown. The separate menu-audio LLE fixture described
above subsequently completed its playback sequence. Physical audio acceptance
remains required.

The earlier synthetic Dolphin/JIT completion proof also verifies guest-written
`SYSTEM FIRMWARE CIPHER PASS` and `MANAGED MAIN: OK` buffers at
`dnd_platform_wait_forever`. Bounded RSP reads and deterministic breakpoints are
used; boot logs alone are insufficient evidence of managed success.

No physical GameCube is connected. EXI electrical behavior/contention, real
stereo output, physical peak memory/latency, and pixel comparisons with the
physical IPL are **not verified**. These omissions are explicit acceptance gaps.

## Comparison with GC-anim-Wii

| Resource or behavior | DolphinDotNet result | Remaining difference |
| --- | --- | --- |
| Environment (`0x6140` hint) | Validated TEX1/BTI, original cube and letter models | Scene headlight reconstructed |
| Tiles (`0x1020` hint) | Original BTI with reconstructed procedural quads and fades | Placement/timing not decoded firmware tracks |
| Three logo faces (`0x16f60` hint) | Header-relative BTIs and original eight-mesh logo/TEV state | Physical lighting comparison unavailable |
| GAMECUBE/Nintendo wordmark (`0x64a0` hint) | Original 17-joint, 21-material geometry, alpha activation and joint animation | Letter timing/rotation reconstructed |
| Startup sound | Both original AFC channels, original rate and exact duration, native compressed source | Physical output unverified |
| Menu assets | Menu cube, two icons and memory cube imported and selectable | No menu UI implementation requested |
| Animation/boot | Optional reusable GX rendition, seek/update without reset or implicit audio | Physical IPL curves not extracted |

The Wii project uses ripped geometry and manually tuned motion. Its geometry is
neither imported nor embedded here. Published output sizes are profile hints,
backed by full validation, rather than an absolute-offset asset locator.

## Budgets and reproduction

ROM size: 2,097,152 bytes; at most 128 records; at most 2 MiB per decoded record,
32 MiB aggregate scan work, 2 MiB retained compressed/audio data and 4,194,304 TEX1
texels across mips. EXI transfers are at most 4 KiB, with a 20 ms deadline and a
finite poll cap. Native graphics/audio/model allocations have independent bounded
budgets and share MEM1 with the managed heap. No physical peak is claimed.

Measured original model native retention is approximately 149–367 KiB per model
before instances; startup uses five models. `Model.NativeBytes`,
`ModelInstance.NativeBytes` and `SystemStartupRenderer.NativeBytes` report actual
per-resource budget charges (shared textures can be counted multiple times),
not unique physical memory or peak usage. Host warm-cache lookup measurements are approximately 0.02–0.04 ms per
100 lookups with 3,200 allocated bytes; they are not console latency predictions.

Use [the API guide](system-firmware-resources.md),
`dnd-assets firmware inspect INPUT`, `tests/system_firmware_aot.py` and
`tests/SystemFirmwareHardwareSmoke/captures.json`. Default hardware fixture source
is EXI only; `FIRMWARE_EXPLICIT_FILE` builds require the explicitly selected
`sd:/ipl.bin`. A cycles six textures, nine models and seven exact animation times;
X enables time progression, START plays sound and B releases resources and exits.

The full remote CI run for renderer commit `dad475c` passed all nine framework
domains, host debugger compatibility, macOS/Windows build-driver checks and the
final gate: [run 42111830873](https://github.com/capz/DolphinDotNet/actions/runs/42111830873).
Complete-image catalog tests additionally cover unchanged header/font records,
font-only encoding rejection and both original font decompressions. Subsequent
head checks are reported on the PR rather than inferred from that earlier run.
