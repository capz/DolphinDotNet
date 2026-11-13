# IPL menu audio research — 2026-11-13

This is infrastructure and research work, **not completed named menu playback**.
No production semantic profile is enabled. `TurnSide`, `TurnCenter`, `Select`
and every secondary effect return unavailable on every revision. A validated AW
sample is not proof of a complete menu event. Do not merge this work as the
finished menu-sound feature.

## Inputs and reproducibility

At the user's explicit request, the three research inputs were downloaded from
[RetroBIOS](https://github.com/Abdess/retrobios/tree/a9772dd6c7f6824c43c4c0117cbee56c70da9369/bios/Nintendo/GameCube/GC)
at commit `a9772dd6c7f6824c43c4c0117cbee56c70da9369`. Firmware and derived
files remain outside the checkout. All inputs are exactly 2,097,152 bytes.
Acquisition is online research, not a physical-console dump or output capture.

| Input | Acquisition path | SHA-256 of input file, before descrambling |
| --- | --- | --- |
| NTSC 1.0 | `JAP/IPL.bin` | `5b2f2617cc63f1861a1f4ae44bb135caf10e202b5b9b79271daaf60f8caaf37d` |
| NTSC 1.2 | `USA/IPL.bin` | `7c833f95839c226699108b93ab6acbe772788bfa3f05132b8ced5e78f5bc9a26` |
| PAL 1.2 | `EUR/IPL.bin` | `85ffcbc834238eb24c87fce27ba7b438cc53a77db013a0a7c59c78e0af08cb84` |

The existing loader independently detects scrambled encoding for all three.
The first has the publisher/copyright header without a revision suffix; the
others explicitly report NTSC/PAL Revision 1.2. Folder names do not establish
region/language or event semantics.

The metadata command used was:

```sh
dotnet run --project tools/DolphinDotNet.Assets -c Release -- firmware inspect /private/ipl.bin --menu-audio
```

| Input | WSYS offset / bounded bytes | Descriptor bytes | Associated AW offset / bytes | Result |
| --- | --- | --- | --- | --- |
| NTSC 1.0 | 698048 / 976 | 36, compact | 699040 / 729322 | Structurally resolved |
| NTSC 1.2 | 843712 / 1648 | 44, extended | 845376 / 729322 | Structurally resolved |
| PAL 1.2 | 1022912 / 1648 | 44, extended | 1024576 / 729322 | Structurally resolved |

All associate one `ipl_0.aw` bank containing twelve AFC waves through the
numbered wrapper and packed extents. Offsets above are observations, never
production locators. Compact stale wrapper sizes are retained and do not set
the archive start. Inter-wave padding may be nonzero.

The explicit audition command subsequently canonicalized all twelve candidates
from each input. A private native harness using `source/audio.c` loaded and
decoded all 36 selected sources through the copied-memory JAudio route, after
destroying the temporary managed-style input arrays. It checked exact source
frame counts, channels, rates, loop intervals and end of one-shot decoding.
SHA-256 of the concatenated mono big-endian signed PCM16 output, in flattened
wave order 0 through 11 with no separators or container headers, is identical
for all three inputs:
`102de93cbb189e0cbebfa69181a6ddf938c2c9fc09a6e247029ac1d689ebf48f`.
This upgrades the **raw sources** to host-decoder verified; no named effect is
upgraded. No independent retail decoder comparison or original-event comparison
was performed in this follow-up. Source/output data and the private harness
remain outside Git and published artifacts.

The NTSC 1.2 inventory includes the following **flattened candidate positions**;
these are not sound-event IDs or a published semantic map:

| Positions | Source rate | Source frames | Loop interval |
| --- | --- | --- | --- |
| 0, 1 | 32000 | 174067 each | None; existing startup channels |
| 2, 3 | 32000 | 187092 each | None |
| 4 | 32000 | 52049 | None |
| 5 | 44100 | 31214 | None |
| 6 | 32000 | 17821 | None |
| 7 | 32000 | 31551 | [11892, 31415) |
| 8 | 32000 | 28544 | [14080, 28137) |
| 9, 10 | 32000 | 166745 each | None |
| 11 | 44100 | 79249 | [31967, 36865) |

The descrambled NTSC 1.2 image also contains an IBNK tag at 0xcd6e0,
`BANK` at 0xcd700, nine `INST` tags at 0xcdd80 through 0xcdf80, and
`C-DF`/`SCNE` tables inside WSYS. These are leads only: instrument notes,
ID tables, envelope/tuning behavior and event dispatch have not been resolved.
In particular, a looping source cannot safely be treated as a complete one-shot
effect without tracing its release/tail behavior.

[ogamespec/gc-ipl](https://github.com/ogamespec/gc-ipl/tree/0d8ec091b89f223b088b1debfbab3d0e83938f09/Jaudio)
was inspected as an independent reverse-engineering lead. Its wave/bank reader
describes pointer conversion and sound-handle lookup, not a verified map of the
requested cube actions. No external implementation or payload was copied into
the SDK. A second lead, [mstan/gcnlle](https://github.com/mstan/gcnlle), emulates
the IPL's audio path; it was not used as mapping evidence.

## Semantic evidence matrix

These results apply individually to each of NTSC 1.0, NTSC 1.2 and PAL 1.2.
No event trace, uninterrupted original-action PCM comparison, emulator sound
acceptance or physical-output recording was completed for menu effects.

| Identity / original action to investigate | Source / parameters / profile | Evidence level |
| --- | --- | --- |
| `TurnSide`: central cube toward a side; check each direction | Unresolved event/wave, pitch, gain, pan, envelope, layering and complete duration; no profile | Unsupported |
| `TurnCenter`: return to central position; establish distinctness | Unresolved; no shared-source equivalence assumed | Unsupported |
| `Select`: confirm and enter an actual menu | Unresolved; do not substitute an option-selection sound | Unsupported |
| `Back`: return action | Unresolved | Unsupported |
| `OptionCancel`: cancel option change | Unresolved | Unsupported |
| `OptionConfirm`: confirm option change | Unresolved | Unsupported |
| `OptionMove1`: first proposed option movement identity | Original action and recipe unresolved | Unsupported |
| `OptionMove2`: second proposed option movement identity | Original action and recipe unresolved | Unsupported |
| `OptionMove3`: third proposed option movement identity | Original action and recipe unresolved | Unsupported |
| `OptionSelect`: select an option | Unresolved | Unsupported |
| `PressStart`: start prompt action | Unresolved | Unsupported |

Before enabling a profile, trace original input → event dispatch → event/note
ID → bank instrument → true wave ID → archive span and all voice transforms.
Correlate uninterrupted output with that same original action. Fingerprint a
documented encoding stage of bounded event/bank content, then check hashing on
the target AOT runtime. A whole-image hash, filename, wave count, rate or duration
alone must never qualify a mapping. Recheck event relationships on each revision.
No sequence interpreter or PCM renderer has been added without that evidence.

## Implemented structural and ownership work

`IncludeMenuAudio` opts into structural association and future verified-source
retention. Current unknown profiles retain **zero additional encoded sample
bytes**. The default path does not perform menu discovery or retention, and the
encoding-evidence scan remains independent of menu profiles. Metadata queries
allocate no native audio or decoded PCM. Creation fails before native allocation
on unavailable effects, with a diagnostic reason.

The internal selected-wave canonicalizer is exercised by the explicit audition
tool and generated tests, independently of semantic resolution. It preserves
mono codec, sample rate, exact frame count, early loop end and descriptor fields
from either source layout. It rebases the selected wave to zero in an extended
256-byte WSYS header supported by the existing native reader. Only frames needed
for actual samples are copied, including partial final frames. This capability
does not make any named effect available.

Generated vectors cover all four WSYS codecs, both layouts and early/no loops.
Every native decoded sample is compared with an independently specified PCM16
oracle. Tests destroy the input arrays before native playback, exercise 16 clip
slots and 32 independent voices, reject insufficient budgets, and drain all slots.
The AOT firmware fixture additionally clears/disposes image, catalog and snapshot
before playing an isolated generated wave under collection-on-allocation stress.
Existing startup stereo Sound/Music remain covered in the same run.

Measured generated-vector counters: selected-wave payload is 265 bytes for the
16-frame AFC fixture, and at most 294 bytes for the 19-frame codec vectors
(256-byte header plus at most 38 bytes). Native single-clip retained usage peaks
at 694 bytes in these vectors; this is a test measurement, not a retail-effect
budget. Across the 36 retail raw-source auditions, the largest single native
clip was 105,902 bytes (source positions 2 and 3), with a 105,502-byte selected
header/body snapshot. No complete named effect has a measured budget yet.
The forced-GC AOT bridge reported 572 collections and 4,208,224 occupied
heap bytes at exit on its first expanded run; exit occupancy is **not peak**.
Full preparation peak and physical latency have not been measured. Peak must
include the live 2 MiB image, normal catalog, snapshots, parser temporaries,
native sources and validation scratch; the native clip counter alone is
insufficient. The existing 2 MiB aggregate catalog limit is unchanged.

## Private audition and acceptance

```sh
# Explicitly exports ONE validated raw candidate. Choose a private directory
# outside Git; never add outputs to CI artifacts. Existing outputs are protected.
dotnet run --project tools/DolphinDotNet.Assets -c Release -- firmware audition /private/ipl.bin 0 7 /private/audition-7
```

Load `selected.wsys` with `AudioFormat.JAudioWaveBank`, paired with
`selected.aw`, entry zero, using the existing Sound API. Auditioning this raw
source does not reproduce any instrument tuning, release, layering or event
sequence. The normal inspection command exports no audio.

`tests/SystemMenuAudioHardwareSmoke` requests only the three primary effects on
the preparation executor, cleans partial initialization, disposes image/catalog,
then uses normal application-executor Sound voices with completion markers and
gaps. Its default build reads EXI only. `FIRMWARE_EXPLICIT_FILE` reads only
`sd:/ipl.bin` after an explicit mount. It deliberately fails acceptance while
mappings are unavailable. A compiled DOL or an unavailable log is not evidence
of authentic audible playback. No physical run has been performed.

## Local validation results

Passed on 2026-11-13 using the local Linux development container:

```sh
dotnet run --project tests/SystemFirmwareTests -c Release -p:DndBuildNative=false
python3 tests/system_menu_audio.py
python3 tests/system_firmware_aot.py
python3 tests/system_firmware_inventory.py
python3 scripts/ci_test_suite.py assets
python3 scripts/ci_test_suite.py audio
python3 scripts/ci_test_suite.py compiler
python3 scripts/ci_test_suite.py gamecube
python3 -m unittest discover -s tests -p test_ci_changes.py
python3 -m unittest discover -s tests -p test_ci_workflow.py
```

The final focused tests were rerun after tightening malformed numbered-WSYS
handling and preserving the default inventory JSON shape. The selector tests
cover the new test paths, and the audio suite actually executes the generated
canonicalizer/native oracle test. The GameCube suite AOT-compiles the new
acceptance fixture and compiles its generated C as a host object.

Separately, both console-only and `FIRMWARE_EXPLICIT_FILE` acceptance variants
were bundled and **cross-linked with devkitPPC GCC 16.1.0 / libogc2**, including
the repository's EXI/video overlay and O3/LTO. Both ELF-to-DOL conversions and
the DOL section-alignment validator passed. The generated Makefiles retain the
existing 8 MiB firmware managed heap. Console-only ELF/DOL sizes are
5,871,484 / 911,616 bytes; explicit-file sizes are 6,181,396 / 1,055,040 bytes.
These binaries contain SDK code, not firmware or sampled audio, and are retained
locally for acceptance. Successful cross-linking is not an emulated or physical
audio-output test. Neither variant has passed named-effect playback acceptance.
