# IPL audio locations and extraction plan

Research continued on 2026-11-13 from `feat/system-menu-audio` at
`a8c7a2b486dbcacce825f1e15fa18b7f898482ca`. See
[the existing qualification report](system-menu-audio-research.md) for the three
implemented effects and their original-output comparisons. This investigation
maps the remaining material. The follow-up implementation now exposes finite
events 6–12 in addition to the original three effects, with bounded true-ID/base-key
metadata and per-instrument release defaults. The linked qualification report
records the later output comparisons and their timing/level limits. The plan
below preserves the original investigation and remaining work; alternate
startups, continuous ambience, memory-card actions and `PressStart` are still
research targets.

## Evidence and reproduction

The live input was the same NTSC 1.2 image recorded in that report: 2 MiB,
original-file SHA-256
`7c833f95839c226699108b93ab6acbe772788bfa3f05132b8ced5e78f5bc9a26`.
The emulator was Debian's Dolphin **2503**, JIT64, single CPU thread, OpenGL,
DSP HLE. These are new observations from that emulator, distinct from the
base branch's Dolphin 2609b comparisons. No physical-console run was performed.

The following procedure was used:

1. Run the existing assets tool's `firmware inspect INPUT --menu-audio` and
   retain its metadata privately. Use `FirmwareImage.Load` / `ReadRange` to
   inspect descrambled bytes; offsets below refer to those bytes.
2. Put the original input in an isolated Dolphin user directory at
   `GC/USA/IPL.bin`. Boot **Tools → Load GameCube Main Menu → NTSC-U**, with
   no disc. Use a separate emulator user directory, not personal saves.
3. Enable the GDB stub through `[General] GDBPort`, connect before boot
   execution, and break at the input's original event dispatcher,
   `0x81357840`. Record `r3` (event), `LR` (caller + 4), and the corresponding
   visible action. Remove the breakpoint, step its first instruction, reinstall
   it and continue. Controller commands were sent through Dolphin's existing
   Pipe controller backend, inside the emulator's container. Commands sent to
   a FIFO from the outer executor did not control this nested emulator.
4. Observe each completed menu transition before issuing the next input.
   Actions during a transition can be ignored. Options and Calendar were
   navigated with the main stick and A/B, with visible screens checked.
5. Pause through Dolphin's debugger and use **View → Memory → Export → Dump
   MRAM / Dump ARAM**. Compare entire bounded archive spans against these
   dumps, not a short sample signature. The dumps were 24 MiB and 16 MiB.

ROMs, descrambled copies, RAM/ARAM dumps, event logs and screenshots remain
outside Git and uploaded artifacts. Only derived metadata is recorded here.
Debugger pauses make this a control-flow/memory investigation, not an
uninterrupted audio recording or a latency measurement.

## Where the audio lives

| Resource | Descrambled file offset | Size | Observed cached MEM1 address |
| --- | --- | --- | --- |
| IBNK instruments | `0x0cd6e0` | `0x8e0` | `0x813ccec0` |
| WSYS descriptors and ID tables | `0x0cdfc0` | `0x670` | `0x813cd7a0` |
| `ipl_0.aw` encoded samples | `0x0ce640` | 729,322 | `0x813cde20` |
| BMS sequence, through `EOF\0` | `0x180740` | 3,117 | `0x8147ff20` |

The complete **729,322-byte archive** occurs once in each captured address
space: MEM1 physical offset `0x013cde20`, and **ARAM offset `0x00010000`**.
Both copies match the descrambled file byte for byte. The full archive SHA-256 is
`a8f45eff178cfda0ee7d0facf9b91f53a468d59a39c2079af99ad64d4d219500`.
Its ARAM interval is `[0x00010000, 0x000c20ea)`; padding after that interval is
not part of the declared archive. All twelve complete encoded wave spans also
match at their expected offsets in both dumps.

The BMS bytes match their original file span exactly. IBNK and WSYS do not:
the running IPL rewrites pointers and runtime state in place. For example,
WSYS's WINF/WBCT offsets become `0x813cdaa0` / `0x813cde00`. Do not feed a
runtime WSYS dump into the file-relative reader or mistake those modifications
for a different firmware profile.

For this image, BS2 file offset `0x820` is loaded at `0x81300000`.
The file/MEM1/ARAM addresses above are research observations, **not extraction
locators**. Production acquisition must continue to use bounded resource
discovery and validated archive association. No emulator memory dump is needed
to extract the original encoded material.

## All twelve visible waves

Every entry uses AFC HQ (9 bytes per 16 samples). Each is mono; stereo startup
material is stored as two separate entries. Flat index means WINF enumeration
order, while ID means the original WBCT → SCNE → C-DF lookup identity.

| Flat index | True ID | Instrument program | Rate | Frames | Loop, half-open | AW-relative offset | ARAM offset |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 0 | 7 | 0, low key | 32000 | 174067 | none | `0x00000` | `0x10000` |
| 1 | 8 | 0, high key | 32000 | 174067 | none | `0x17e80` | `0x27e80` |
| 2 | 0 | 1, low key | 32000 | 187092 | none | `0x2fd00` | `0x3fd00` |
| 3 | 1 | 1, high key | 32000 | 187092 | none | `0x49820` | `0x59820` |
| 4 | 4 | 8 | 32000 | 52049 | none | `0x63340` | `0x73340` |
| 5 | 5 | 5 | 44100 | 31214 | none | `0x6a5c0` | `0x7a5c0` |
| 6 | 6 | 3 | 32000 | 17821 | none | `0x6ea60` | `0x7ea60` |
| 7 | 9 | 7 | 32000 | 31551 | `[11892,31415)` | `0x711a0` | `0x811a0` |
| 8 | 10 | 6 | 32000 | 28544 | `[14080,28137)` | `0x75700` | `0x85700` |
| 9 | 2 | 2, low key | 32000 | 166745 | none | `0x795c0` | `0x895c0` |
| 10 | 3 | 2, high key | 32000 | 166745 | none | `0x90440` | `0xa0440` |
| 11 | 11 | 9 | 44100 | 79249 | `[31967,36865)` | `0xa72c0` | `0xb72c0` |

The existing inspector reports flat indices; they must not be substituted for
these IDs. Wave 9 in an instrument means **flat 7**, not flat 9.

Programs 0, 1 and 2 each have two key regions, ending at keys 64 and 127.
The low region has tuning 1.0; the high region has tuning 0.5. All gains are
1.0 and each region has one velocity region ending at 127. Both regions must
be understood before identifying the two notes as a stereo pair: key and
instrument tuning are part of playback, not additional sample rates.
The normal startup pair is already qualified as flat 0/1. Flat 2/3 and 9/10
are two further paired instruments; their alternate-boot action/variant names
require their own live qualification.

Programs 3, 5, 6, 7, 8 and 9 each use one key/velocity region with gain and
tuning 1.0. Programs 0/1/2/8 use source base key 60; programs 3/5/6/7/9 use
48. There is no program 4 in the inspected bank. Looped wave length is not the
length of a menu effect, and the 44.1 kHz source rates are not output rates.

## Actions observed in the original IPL

The following events were observed at the dispatcher, with their callers
checked against the original PPC branches. Entry addresses are offsets within
the BMS, not file or RAM addresses. Program/source assignments come from
following those sequence entries and the original bank, rather than a timbre
guess. Only events 3–5 already have output-qualified SDK implementations.

| Event | Visible action observed | BMS entry | Program → true ID (flat index) |
| --- | --- | --- | --- |
| 3 | Center toward Options or Calendar | `0x3fc` | 7 → 9 (7) |
| 4 | B from a cube side back to center | `0x423` | 7 → 9 (7) |
| 5 | A from a cube side into Options or Calendar | `0xadf` | 7 → 9 (7), plus 9 → 11 (11) |
| 6 | B/Finish from Options or Calendar back to its cube side | `0xb35` | 7 → 9 (7) |
| 7 | B cancelling an active Screen Position or Time edit | `0x497` | 3 → 6 (6) |
| 8 | A opening a Sound, Screen Position or Time editor | `0x934` | 9 → 11 (11) |
| 9 | Change Stereo/Mono, screen offset, or time value | `0x926` | 9 → 11 (11) |
| 10 | Move between Time fields while editing | `0x918` | 9 → 11 (11) |
| 11 | Move between Sound/Screen Position or Date/Time rows | `0x90a` | 9 → 11 (11) |
| 12 | A confirming a Sound edit | `0x942` | 9 → 11 (11) |

Representative call sites, as verified by `LR - 4`, are `0x81327a78` for
event 6; `0x81327ce8` for Screen Position cancel; `0x8132825c` for Time edit;
`0x813287d0` for Time field selection; `0x813279c8` for moving Options rows;
and `0x81327b74` for Sound confirmation. These addresses apply only to this
input and are not SDK constants.

Useful distinctions for the stable public identities: `Back` should be
qualified against event 6, separately from `TurnCenter`/event 4;
`OptionCancel` against 7; `OptionSelect` against 8; `OptionConfirm` against 12.
Events 9, 10 and 11 distinguish value, field and row movement and are candidates
for `OptionMove1/2/3`, respectively. These are proposed semantic qualifications,
not newly enabled API mappings.

The BMS is not a list of one sample per button. Events 8–12 launch two child
tracks and build their echoes from further authored notes, velocities, waits
and pan changes on ID 11. Event 6 instead uses two ID-9 notes, pitch ramps and
an explicit ADSR. Event 7 uses another instrument, ID 6, with a timed pitch
bend. A raw ID-11 audition cannot stand in for any of these complete effects.

## Idle background note tracing

A second original-IPL boot added a breakpoint at the instrument/velocity-map
resolver, `0x8134f0c0`. Its arguments are the instrument pointer in `r3`,
effective key in `r4`, and velocity in `r5`. Instrument pointers were matched
to the original IBNK program records, then followed through their key/velocity
regions and C-DF IDs. This supplies live note evidence in addition to the
action-dispatch evidence above.

Before the first injected controller action, the trace observed program 7
(ID 9 / flat 7) alternating keys 40/35 at velocity 40; program 6 (ID 10 /
flat 8) playing keys 24/29/36 at velocity 80; and program 8 (ID 4 / flat 4)
playing key 29 at velocity 80. After turning to the Options face, further
background notes used program 6 keys 29/34/41 and program 8 key 34 at velocity
90. The turn itself separately requested program 7 keys 52/55/48 at velocity
100, matching the already qualified effect.

Thus the background uses both looped sources and a nonlooped source, with
authored repeated notes, pitch and velocity changes. ID 10 is an observed
background instrument, not a complete background recording. ID 9 is shared
between background and menu effects. No controller input was injected during
the observed background phrases. These traces do not establish an exact loop
period, all possible background states, or a qualified standalone ambience
render; output/effects and persistent track state still need investigation.

## Material still requiring investigation

- Event 13 (`0x447`) uses program 5 / ID 5 (flat 5). Static callers include
  unsuccessful value/selection branches. A visible failure action and
  uninterrupted output capture are still needed before naming this effect.
- Events 14–21 occupy BMS entries `0x6df`, `0x743`, `0x7a1`, `0x84e`,
  `0x85c`, `0x81e`, `0x82f`, `0x838`. Their callers are in the memory-card
  flow, which was not exercised in this run. Events 17/18 contain backward
  jumps through shared code at `0x841`; do not assume all are finite one-shots.
  Parameter command `0x97` also appears in this group, outside the current
  recipe compiler's accepted subset.
- Event 22 (`0x64f`) uses program 9 / ID 11 and has a static caller in the
  Game Play flow. A disc and visible Start action are needed to qualify
  `PressStart`; a caller or recognizable melody alone is insufficient.
- Dispatcher values 0–2 route to a different track ID (`0x21002`) and send
  a control-port value. Their nominal entries in the event table do not
  establish complete playback recipes. Dispatcher value 23 updates a
  triangular fade/control state; it does **not** simply execute table entry
  23. Values above 23 take another routing path. A table index alone is not
  an event API.
- The sequence root starts four child tracks (`0x2f`, `0x178`, `0x28b`,
  `0x388`) and continues a port-driven loop. Ambient material and alternate
  startup selection must be traced in that persistent context. The live
  background assignments above narrow the targets, but do not qualify a
  complete named ambience clip.
- Only NTSC 1.2 was driven live in this investigation. The base branch records
  identical IBNK/BMS content for PAL 1.2, but PAL action paths and output still
  need qualification for newly exposed effects. Early NTSC's different DSP
  setup remains unsupported.

## Extraction and exposure plan

1. **Improve the metadata inventory first.** Add bounded optional WBCT/SCNE/
   C-DF ID reporting, source base key, and the IBNK key/velocity associations
   to the opt-in inspector. Preserve flat indices as explicit audition
   selectors. Validate every pointer, count, duplicate ID, scene/bank link
   and resource extent; absent or unsupported ID tables must never produce
   invented identities. Generated tests should cover reordered/non-contiguous
   IDs, compact and extended layouts, multiple banks, truncation and relocation.
2. **Extract only selected encoded spans.** Reuse `FirmwareMenuWaveSource`
   and the existing native AFC decoder. Explicit raw research auditions
   already work through `firmware audition INPUT WSYS_INDEX FLAT_INDEX DIR`.
   Use the original declared frame count and loop endpoints, excluding AW
   packing padding and extra capacity in the final AFC frame. Retain no ROM
   or decoded PCM in a catalog. ID 6 adds only one new source for events 6–12;
   IDs 9 and 11 are already captured by the qualified profile.
3. **Qualify Back first, then the five ID-11 option effects, then Cancel.**
   Compile the original finite recipes and compare each with an uninterrupted
   original-emulator recording of the matched action. Resolve instrument
   oscillator/release defaults from the actual selected program: the current
   compiler initially assumes program 7's oscillator. Program 9's bank
   oscillator differs, and accepting another entry without fixing that would
   silently give it the wrong release. Handle the selected source by validated
   ID rather than extending the current two-source ordinal shortcut. Check
   track inheritance, pan, send, note-offs, pitch and complete tails.
4. **Keep the renderer bounded.** Several option recipes finish their audible
   notes before an explicit parent wait/end. Choose and document output-end
   semantics from released voices and DSP tails, not BMS byte length or the
   raw sample's length. Reject loops/unhandled commands rather than truncating
   them into plausible one-shots. Test synthetic independent timing/envelope
   oracles, budget boundaries, cancellation and ownership after image/catalog
   disposal; extend the existing native and AOT fixtures.
5. **Investigate continuous and alternate material separately.** Trace the
   three paired startup programs under normal boot, one-controller Z and
   four-controller Z, checking the actual selected program/keys and channel
   routing. Trace idle root notes and memory-card operations before proposing
   an ambience/loop API. Persistent sequences need explicit lifetime and
   phase/loop-continuity design; the existing one-shot API does not provide it.
6. **Expand profiles only after comparisons pass.** Repeat on PAL 1.2 and
   separately qualify early NTSC's DSP initializer. Keep unsupported identities
   unavailable. Record waveform/spectral/level/tail comparisons and native
   retained/peak memory, then run the existing firmware, audio and AOT suites.
   Hardware/EXI playback remains a separate acceptance step.

The immediate implementation target is therefore metadata plus finite events
6–12. No additional sample archive or sample codec is needed for that work;
the missing pieces are semantic qualification and faithful recipe preparation.
