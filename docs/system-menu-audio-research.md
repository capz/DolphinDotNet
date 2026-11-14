# IPL menu audio research — 2026-11-13

Ten cube/options effects are implemented for the qualified NTSC 1.2 and PAL 1.2
resource profile: `TurnSide`, `TurnCenter`, `Select`, `Back`, `OptionCancel`,
`OptionConfirm`, `OptionMove1`, `OptionMove2`, `OptionMove3` and `OptionSelect`.
They are composed effects, not raw-wave aliases. Early NTSC's different DSP-effect
setup, `PressStart`, memory-card effects and continuous ambience remain
unavailable. Physical-console output has not been verified.

The follow-up [audio locations and extraction plan](system-menu-audio-extraction-plan.md)
records a complete live ARAM archive match, all twelve wave IDs/instruments and
additional Options/Calendar event traces. The implementation and uninterrupted
output comparisons below complete the finite events 6–12 part of that plan.

## Physical ROM reader follow-up — 2026-11-14

A PAL console launched through GC Loader / CubiBoot returned ROM CRC-32
`6DAC1F2A` and decoded-region CRC-32 `8F87BDB8`, matching the early NTSC 1.0
reference below. The production immediate reader, DMA at 32/16 MHz, immediate
reads at 16 MHz and a repeated production read all agreed. The generated-data
managed cipher/catalog check passed; the decoded region contained 14 Yay0
signatures. This establishes agreement for that diagnostic, not successful
background acquisition or physical audio playback in the explorer.

The exact published SDK 335 explorer also loads startup audio and all six
textures from that raw reference in Dolphin. Its reported hardware encoding
failure therefore remains unconfirmed in the emulator. The SDK now uses
bounded continuous page DMA, matching libogc and the console-tested DMA path,
with static buffer lifetime and quarantine after timeout. This transport change
does not qualify early NTSC menu effects or relax firmware validation.

## Inputs

The user explicitly authorized downloading these research inputs from
[RetroBIOS](https://github.com/Abdess/retrobios/tree/a9772dd6c7f6824c43c4c0117cbee56c70da9369/bios/Nintendo/GameCube/GC).
Each is 2,097,152 bytes. Firmware, extracted samples, RAM/debug traces and audio
recordings stay outside Git and uploaded artifacts.

| Input | Acquisition path | SHA-256 before descrambling |
| --- | --- | --- |
| NTSC 1.0 | JAP/IPL.bin | `5b2f2617cc63f1861a1f4ae44bb135caf10e202b5b9b79271daaf60f8caaf37d` |
| NTSC 1.2 | USA/IPL.bin | `7c833f95839c226699108b93ab6acbe772788bfa3f05132b8ced5e78f5bc9a26` |
| PAL 1.2 | EUR/IPL.bin | `85ffcbc834238eb24c87fce27ba7b438cc53a77db013a0a7c59c78e0af08cb84` |

Existing acquisition independently detects scrambled encoding. The first header
has no revision suffix; the other two explicitly name Revision 1.2. Paths and
region labels are evidence, never production profile selectors.

| Input | WSYS offset / bounded size | Descriptor bytes | Validated adjacent AW offset / size |
| --- | --- | --- | --- |
| NTSC 1.0 | 698048 / 976 | 36, compact | 699040 / 729322 |
| NTSC 1.2 | 843712 / 1648 | 44, extended | 845376 / 729322 |
| PAL 1.2 | 1022912 / 1648 | 44, extended | 1024576 / 729322 |

The numbered wrapper and packed extents associate `ipl_0.aw`. The bounded WSYS
end, including compact stale-wrapper handling, sets the archive start. No
absolute offsets above are production locators. All 36 raw-wave auditions
passed the existing native decoder. Concatenated mono BE PCM16, flat positions
0–11, has SHA-256
`102de93cbb189e0cbebfa69181a6ddf938c2c9fc09a6e247029ac1d689ebf48f`
on all three inputs; this alone does not establish named-effect semantics.

## Events, instruments and true wave IDs

Static analysis followed cube input branches into the original event dispatcher,
then the BMS event jump table, IBNK program/key/velocity maps and WSYS C-DF IDs.
The NTSC 1.2 original IPL was also driven through Dolphin 2609b's existing
controller/TAS interface with GDB event and note traces. Up/down rotation,
return and actual menu entry were observed. All four direction branches share
the same event statically; this was also checked in PAL's handler.

NTSC 1.2 dispatch is at RAM `0x81357840`; PAL dispatch is `0x8135abc0`.
These are research observations, not production addresses. BS2 file offset
`0x820` loads at RAM `0x81300000` for the inspected images.

| Public identity / original action | Event and sequence entry | Verified instrument notes |
| --- | --- | --- |
| TurnSide: center toward any side | Event 3; BMS `0x3fc` | Program 7, wave ID 9: authored keys 64/67/60, transpose -12 → 52/55/48, velocity 100 |
| TurnCenter: opposite direction or B returning to center | Event 4; BMS `0x423` | Program 7, wave ID 9: keys 60/57/53, transpose -12 → 48/45/41, velocity 100 |
| Select: A entering a cube-side menu | Event 5; BMS `0xadf`, child entries `0xaed` and `0xb72` | Program 7 / ID 9: keys 72/70 → 60/58, velocity 85; program 9 / ID 11: 15 authored notes, keys 85…69 → 73…57, authored velocities and pan |
| Back: B leaving a side menu | Event 6; BMS `0xb35` | Program 7 / ID 9 |
| OptionCancel: B closing an editor | Event 7; BMS `0x497` | Program 3 / ID 6 |
| OptionSelect: A opening an editor | Event 8; BMS `0x934` | Program 9 / ID 11 |
| OptionMove1: change an edited value | Event 9; BMS `0x926` | Program 9 / ID 11 |
| OptionMove2: move between editor fields | Event 10; BMS `0x918` | Program 9 / ID 11 |
| OptionMove3: move between option rows | Event 11; BMS `0x90a` | Program 9 / ID 11 |
| OptionConfirm: A accepting an edit | Event 12; BMS `0x942` | Program 9 / ID 11 |

Program 7's one key/velocity region references **ID 9**, resolved by C-DF to
**flat position 7**, not position 9. Program 9 references **ID 11**, flat 11.
Both have instrument/key/velocity gain and tuning 1.0. Native note traces
confirmed IDs, transposed keys, velocities, tuning and squared velocity gain.

| True ID | Flat position | Codec / rate / base key | Source frames / loop interval |
| --- | --- | --- | --- |
| 6 | 6 | AFC HQ / 32000 / 48 | 17821 / no loop |
| 9 | 7 | AFC HQ / 32000 / 48 | 31551 / [11892,31415) |
| 11 | 11 | AFC HQ / 44100 / 48 | 79249 / [31967,36865) |

The instrument rate is not the output sample rate. Original tuning divides by
32028.5 Hz. The tempo/timebase product is 300 sequence ticks per second at
that DAC reference, advanced in 80-sample DSP blocks. Track gain is linear;
velocity gain is squared. Byte parameters use signed byte ×256 /32768.
The event parent gain is 90/128; mixer master is 13107/16384 after output gain.
These values were checked against the original PPC code and live track/DSP
parameters, rather than inferred from a later JAudio implementation.

Rotation uses track gain 65/128, two four-tick pitch ramps (-685 then 0), waits
60 and 87 ticks, note-offs, and the instrument's 500-unit **squared** release.
The first Select layer uses gain 50/128, bend range 8, four bends at ticks
0/21/42/63 (-685,685,0,1370), note-offs at 91/92 ticks and ADSR
(1,10,2000,300,200). The second uses gain 40/128, starts at tick 30, follows the
firmware's finite 15-note/pan sequence and ADSR (1,10,32000,1000,100).
Envelope time units are 1/600 second at the DAC reference.

Instrument defaults are read from the actual program's oscillator/release table.
Program 9's default release is 200, while program 7's is 500. Explicit sequence
ADSR takes precedence. Program/key/velocity regions and unity gain/tuning are
validated before compiling notes; source IDs are assigned per recipe, rather
than assuming a program number is a flattened wave position.

Rotation's default parent FX weighting yields send 127/512. The two audible
feedback paths use 50 DSP blocks of delay, an 8-tap FIR and 0x6000 Q15 output
gain. The filter coefficients are extracted from firmware. Select's common
setup writes register 12 = 0, disabling the inherited send. Its panning uses
its authored curves. An additional IPL reverb bus does not route to audible
front outputs in the inspected HLE path; physical behavior is not claimed.

## Profile and bounds

`ipl-menu-bms-v2` requires the checked WSYS/AW association, bounded adjacent IBNK,
true ID tables and all the following SHA-256 fingerprints of **descrambled**
resource-relative content:

| Content | Bytes / SHA-256 |
| --- | --- |
| IBNK, starting with IBNK tag | 2272 / `50e79d0625aa0b8817509b6fa3f56af7c3f94aa81dac64a146e15e04f0ce10e1` |
| BMS at aligned AW end, through `EOF\0` | 3117 / `3432f80f4f3c7be92e1288b81ce8071db3554cd8fcc520410f5dd489f0ccdc1f` |
| DSP-effect initializer directly preceding IBNK | 128 / `c10b2ad8bc679484c74fe78f9b5bcca66e210a08cba43a51fbc5b36ad224b4fc` |
| Four-tap, 64-phase resampling table, in bounded preceding 64 KiB | 512 / `f5010bc5d7798e7a059af43623dfcdb26c34b5bf2d282ad253db5bdc32ce591b` |

Identical software/DSP copies of the complete resampling table are equivalent;
competing event/archive associations remain errors. Hashing does not replace
pointer, size, codec, loop, duplicate-ID or archive bounds checks. The small
SHA-256 implementation passes standard host vectors and single/multiblock AOT
vectors. Unknown bank/BMS/DSP semantics remain unavailable. A recognized profile
with malformed ID/source tables fails validation. Private mutation and complete
resource relocation tests passed on NTSC 1.2 and PAL 1.2.

Early NTSC's 3092-byte BMS has hash
`5e9be1050199629aa560f410effbbd5a8361ea17777fb07f580c1d75586cbd35`.
Its equivalent note recipes compile in research, but the DSP-effect initializer
differs. It is deliberately unavailable pending separate output qualification.

The compiler accepts only the finite subset used here: notes/off, bounded waits,
program/register writes, transpose, volume/pitch/pan, ADSR, route zero and bounded
calls/child tracks. Limits are 512 instructions, three tracks, four call frames,
32 notes, 64 automation changes, 2000 sequence ticks and eight output seconds.
Unsupported commands, active-note reuse, unreleased notes and invalid ranges fail.
It is not a public or general BMS/music engine.

## Output comparison and limits

An uninterrupted original NTSC 1.2 DSP dump was recorded in Dolphin 2609b HLE
with ambient root tracks muted, while the event tracks and their DSP sends were
left intact. GDB note/event tracing was disabled during that recording. Captures
are private and contain no reference-collection WAV substitution.

Host-rendered SDK PCM16 was compared with those three original actions. Capture
windows began at 12.36 s (center), 24.39 s (side), 36.69 s (select). After aligning
first-channel cross-correlation, lags were 309,454,421 samples respectively.
Magnitudes were compared using 1024-sample Hann STFTs, 128-sample hops, retaining
windows above 3% of original peak window energy:

| Effect | Aligned waveform correlation | Mean spectral cosine | SDK/original stereo RMS |
| --- | --- | --- | --- |
| TurnSide | 0.965 | 0.997 | 1.002 |
| TurnCenter | 0.993 | 0.997 | 1.002 |
| Select | 0.610 | 0.981 | 0.982 |

These establish matching notes/timbre, envelope, level and tail at the stated
comparison level, **not bit-identical DSP output**. Select's layered notes are
particularly sensitive to the running parent's fractional tick phase; a prepared
one-shot uses a deterministic zero phase. The renderer quantizes pitch to DSP
4.12, uses firmware four-tap coefficients with four initial history samples,
and uses direct sample selection at ratios >=4. It does not retain an IPL DSP
machine or the ambience's ongoing phase. Per-block mixer ramps/rounding also
prevent an exact sample-by-sample claim. Physical DAC output is unverified.

| Input / effect group | Evidence level |
| --- | --- |
| NTSC 1.2 / all three primary effects | Live event/instrument trace; host render compared with original emulated output |
| PAL 1.2 / all three primary effects | Static action/dispatcher verification; identical bank/BMS/DSP content; native decoded/prepared output verified |
| NTSC 1.0 / all primary effects | Structural/raw decoder and event-recipe research only; unsupported DSP setup |
| NTSC 1.2 / events 6–12 | Live action/dispatcher trace and uninterrupted original-output comparison described below |
| PAL 1.2 / events 6–12 | Identical qualified bank/BMS/DSP content; prepared packets match NTSC 1.2; no live PAL action capture |
| PressStart, memory-card effects and ambience | Unsupported; no guessed or startup fallback |

Output is ordinary stereo PCM16 at nominal 32000 Hz. Rotation contains 74480
frames, including a bounded 32000-frame feedback tail after the final release;
Select contains 38640. Metadata reports these exact prepared lengths. In the
rotation comparison the final SDK 100 ms was below 4 PCM16 RMS steps. The
inaudible residual of a feedback network is bounded, not claimed infinitely exact.

### Additional options comparison — Dolphin 2503

The original NTSC 1.2 IPL was driven through Sound and Calendar options in
Dolphin 2503 HLE. Before recording actions, only the two persistent background
roots (track IDs `0x21001` and `0x21003`) were sent to the sequence's existing
empty end at relative `0x21b`, followed by three guest seconds for tails to clear.
The event parent (`0x20002`), instruments, BMS bytes and DSP sends were left
intact. No debugger stops occurred during the recorded actions. Input timing
was measured in guest audio frames, avoiding wall-clock delays on a slow host.

Each original action was matched to its prepared SDK effect near the recorded
controller offset. After waveform alignment, stereo RMS and the cosine between
long-term magnitude spectra (square root of Welch power, 1024-sample Hann,
128-sample hop) were measured. This is a different spectral measure from the
earlier per-window STFT comparison above. Ranges include repeated actions:

| Effect | Waveform correlation | Long-term spectral cosine | SDK/original stereo RMS |
| --- | --- | --- | --- |
| Back | 0.944 | 0.99994 | 1.001 |
| OptionCancel | 0.515–0.717 | 0.97062–0.99533 | 0.989–1.192 |
| OptionConfirm | 0.741 | 0.99955 | 0.985 |
| OptionMove1 (value) | 0.889 | 0.99909–0.99911 | 0.829–0.830 |
| OptionMove2 (field) | 0.890 | 0.99958 | 0.987 |
| OptionMove3 (row) | 0.822 | 0.99377–0.99379 | 1.145–1.146 |
| OptionSelect (editor) | 0.802–0.812 | 0.99620–0.99656 | 0.915–0.920 |

The capture confirms the selected actions, pitches, timbres and finite recipes;
it does not establish identical levels or sample timing. The largest observed
RMS difference is about 19%, and coherent layers, ongoing parent tick phase,
block ramps and prior effect tails affect the comparison. No per-effect gain
was fitted to these captures. Applications receive deterministic prepared
one-shots, with the same playback limits described above. PAL prepared packets
match NTSC byte-for-byte; live PAL options output and physical DAC output were
not recorded.

Back, OptionCancel, OptionConfirm, OptionMove1, OptionMove2, OptionMove3 and
OptionSelect contain respectively 47,200, 78,800, 39,600, 20,080, 20,080, 20,080
and 30,000 stereo frames. Duration follows note release and bounded DSP tails,
rather than the length of a raw source or a trailing silent sequence wait.

## Ownership and memory

Opt-in capture retains only three selected encoded spans (IDs 9, 11 and 6),
small recipes/FIR metadata and one shared 512-byte resampling table:
**77984 additional bytes**, up from 65054 for the original three-effect profile.
It retains no ROM or decoded PCM. Unknown profiles retain zero. Legacy catalog
construction does not invoke menu discovery or keep these bytes.

`CreateMenuSound` detaches the selected packet, copies it to native ownership,
decodes the needed instruments through the existing AFC decoder, and renders in
bounded background slices. Its ordinary caller-owned Sound outlives all managed
inputs. Completion frees packet, decoded scratch, FIR rings and parser spans;
playback retains only aligned PCM16. Cancellation/budget/error/slot cleanup use
normal AudioDispose. No second real-time mixer, WAV fallback or PCM8 reduction.

Measured 64-bit host counters (allocator metadata excluded):

| Effect | Native retained PCM16 | Maximum per-preparation native allocation charge |
| --- | --- | --- |
| TurnSide / TurnCenter | 297920 | 397694 |
| Select | 154560 | 442518 |

The 8 MiB default is a ceiling, not an allocation target. Peak must also include
live firmware, ordinary catalog, detached managed packet and managed objects.
The native column is **not full managed/native peak**. The target keeps the
existing 8 MiB managed heap. Forced-GC AOT fixtures verify collection on allocation;
exit occupancy is not peak, and physical latency has not been measured.

## Validation and reproduction

No Nintendo samples or firmware are in generated fixtures. Automated coverage
includes all WSYS codecs/layouts, exact independent PCM16 decoding, bounded BMS
compilation, hash vectors, timed pitch/pan/ADSR, independent layers, FIR feedback
and complete tails, four-tap sampling, exact budget boundaries, malformed packets,
partial-preparation disposal, 16 clips/32 voices and native ownership after
managed-array destruction. Synthesized sample comparisons allow at most 3 PCM16
steps; observed maximum is 1. AOT covers background preparation, disposal,
forced GC, ordinary voices and unchanged startup Sound/Music.

```sh
python3 tests/system_menu_audio.py
python3 tests/system_firmware_aot.py
python3 tests/system_firmware_inventory.py
python3 scripts/ci_test_suite.py assets
python3 scripts/ci_test_suite.py audio
python3 scripts/ci_test_suite.py compiler
python3 scripts/ci_test_suite.py gamecube
```

For explicit **private** qualification, the managed test project accepts
`--private-menu INPUT PRIVATE_DIRECTORY`. It tests changed fingerprints,
conflicting IDs, allocation-free queries and relocation, then exports ten
bounded preparation packets.
It also rejects a bijective swap of IDs 9/11 against qualified source metadata.
Never commit or upload input/output. The normal
`firmware inspect INPUT --menu-audio` exports metadata only; `firmware audition`
continues to export one explicitly chosen raw wave, without semantic claims.

The existing logging-only `SystemMenuAudioHardwareSmoke` fixture prepares these
ten sounds, disposes image/catalog, plays independent voices to completion and
cleans up. Default acquisition is EXI only; `FIRMWARE_EXPLICIT_FILE` reads only
`sd:/ipl.bin`. Cross-linking and emulated/physical acceptance are distinct checks.
The final console/EXI capture build was cross-linked with devkitPPC GCC 16.1.0 /
libogc2, O3/LTO, and passed DOL section alignment. It then completed all nine
normal `Sound` playbacks in Dolphin 2609b, JIT single-core / DSP LLE, using the
qualified PAL 1.2 firmware selected for the DOL's region. Private RAM inspection
of the matching ELF's console ring observed `MENU AUDIO END 2` followed by
`MENU AUDIO COMPLETE`; all three sounds had already been prepared and image /
catalog disposed. `FIRMWARE_ACCEPTANCE_HOLD` only keeps this existing logging
fixture alive after completion for private inspection. No debugger pause was
used during the nine playback intervals. No physical-console run was performed.

The expanded ten-effect fixture was cross-linked with the same devkitPPC GCC
16.1.0 / libogc2 O3/LTO toolchain and completed **30** ordinary playbacks in
Dolphin 2503, JIT single-core / DSP LLE. Private live MEM1 inspection of the
matching ELF's console ring observed `MENU AUDIO END 9` and
`MENU AUDIO COMPLETE`; no pauses were used during playback. The full local audio
suite, metadata inventory, generated WSYS identity tests and forced-GC AOT fixture
pass. The latter performed 1,025 reads and 887 collections, with 4,212,984 heap
bytes occupied at exit (not peak). NTSC 1.2 and PAL 1.2 private mutation/relocation
runs prepare all ten; early NTSC reports unsupported with zero retained menu
bytes. No physical-console run was performed.

Research references include the independently inspected
[early JAudio bank/wave reader](https://github.com/ogamespec/gc-ipl/tree/0d8ec091b89f223b088b1debfbab3d0e83938f09/Jaudio),
[later JAS sequence documentation through source](https://github.com/projectPiki/pikmin2/tree/main/src/JSystem/JAudio/JAS),
and [Dolphin's IPL DSP implementation](https://github.com/dolphin-emu/dolphin/blob/master/Source/Core/Core/HW/DSPHLE/UCodes/Zelda.cpp).
The later oscillator/volume rules differ from this IPL and were not copied into
the SDK. No external implementation or proprietary payload was committed.
