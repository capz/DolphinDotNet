# Complete IPL audio qualification

The native session executes caller-supplied, qualified IPL score/instrument data.
It does not bundle firmware, extracted sounds, textures, sequence bytes,
oscillator tables, recordings or runtime dumps. Private reference work uses the
installed, unmodified Dolphin 2503 emulator with DSP LLE. Emulator results and
physical-console confirmation are recorded separately.

## Inventory and interfaces

- Twelve original WSYS sources have true wave IDs and copied metadata. Nine
  play once; three have authored repeating sections. Both the standalone
  `SystemAudioSample.Play()` and session `PlaySample(session.GetSample(i))`
  handle the intro and repeating section without caller loop configuration.
- Normal, One Controller Z and Four Controller Z select the original score's
  three startup paths. Source completion triggers automatic idle handover.
  Standalone Sound/Music paired-source factories remain available separately.
- Nineteen named score cues cover the ten existing cube/options effects,
  Press Start, Error, and seven card notifications/progress effects. Progress
  identities describe the source slot, independently of copying or moving.
- Original visible card actions establish error (13), erase/move completion
  (15), copy/format completion (16), transfer from A/B (17/18), copy-transfer
  completion (19), and move-to-B/A transfer completion (20/21). A self-authored
  disc's Game Play screen establishes Press Start (22).
- Event 14 has a valid recipe but no identified caller; it receives no invented
  public action name. The dispatcher’s three startup paths, motion wrapper
  (23), ambience enable/disable wrappers and output-mode setup were audited
  alongside table-dispatched cues. Verified cube/submenu motion and ambience
  controls have typed APIs; raw event numbers and ports are not public APIs.

Early, NTSC 1.2 and PAL 1.2 profiles retain qualified score/bank/effect/resampler
and oscillator snapshots. Clock and absolute-gate helpers are fingerprinted as
well. NTSC 1.2 and PAL 1.2 complete normalized snapshots are byte-identical;
physical region alone does not identify the profile. The reporting PAL console
returned the early reference fingerprint through GC Loader / CubiBoot.

## Native behavior and bounds

Preparation is stopped and ownership-safe, defaults to 8 MiB, and supports two
prepared caches with one streaming session. Twelve decoded sources and all
producer/queue/delay allocations are charged; the current early target reports
approximately 3.45 MB. Ordinary Sound/Music can play alongside the session.

A dedicated producer feeds four 320-frame chunks. The AESND bridge primes 576
stereo frames and subsequently requests 288, carrying a cursor across chunk
boundaries. Its callback copies bounded native PCM and posts an IRQ-safe wakeup;
it performs no managed access, allocation, I/O or score decoding. A generation
cookie rejects stale callbacks. Stop unregisters the callback and joins the
producer before clearing voices, queue and delays. Disposal releases the cache.

The VM bounds tracks, nesting and opcode work. Unsupported instructions,
malformed inputs and repeated underruns fail closed. The 64-voice mixer recycles
lower-priority released score tails and then older equal-priority score voices;
explicit raw auditions retain their owned playback. Dense generated retriggers
verify the bound. A faulted session is disposed and prepared again.

Sequenced resampling uses exact 4.12 positions and a bounded int32 four-tap MAC.
Sparse block mixing and cached unchanged pitch/pan calculations reduce render
cost without changing PCM. An excessive coefficient norm is rejected before
rendering. Source loops retain their exact partial-frame metadata boundaries.

The early dialect uses float tempo/timebase clocks, deferred tick-created
children, its original velocity/envelope laws and effects. Modern cues inherit the running root clock phase. Modern tracks share
the root tick tree: children opened between ticks participate in the following
tick, and children opened during traversal initialize at their place in that
tree. Modern absolute gates, neutral oscillator replacement, center/subtractive
routing and mono routing follow the separately qualified modern behavior.

## Validation evidence

- Generated tests pass for VM clocks/branches/registers/ports/interrupts,
  malformed and zero-time programs, gates and child creation, partial-frame
  loops, finite and filtered PCM oracles, effects/routing, dense voice recycling,
  ownership, budgets, cancellation, stale tokens and joined teardown.
- The generated producer test consumes ten minutes of continuous 288-frame
  output across 320-frame chunks. The backend test verifies priming, callback
  sizes, continuous PCM, generation cookies and teardown. Sanitizer suites pass;
  latest VM/mixer changes additionally pass address/undefined-behavior checks.
- Managed synthetic firmware tests and inventory CLI checks pass. Private
  three-profile checks establish twelve snapshots, three embedded loops,
  qualification rejection and byte-identical legacy packets.
- New cue events 13–22 pass aligned original LLE comparisons in early and
  modern profiles: spectral cosine exceeds 0.9990 and RMS ratios remain within
  0.974–1.035. Semantic names are established separately by original UI actions.
- All ten existing cube/options cues pass early and modern comparisons: minimum
  spectral cosine 0.9940, RMS ratios 0.987–1.040. Two phase-sensitive modern cues
  were additionally recorded with the original first-note clock phase; matching
  that captured phase passes both thresholds. An authored PCM-onset regression
  rejects resetting a newly opened modern cue to a fresh clock.
- The full early ambience loop passes sixteen long comparison windows:
  minimum spectral cosine 0.9997, RMS ratio 1.0007–1.0376.
- The full modern ambience loop passes all sixteen windows after matching the
  original control-input clock phase: minimum spectral cosine 0.99998, RMS
  ratio 0.9995–1.0045. The first ninety original note-clock observations match
  track order and root phase within floating-point rounding. Matching input
  timing matters because unison voices can interfere differently when their
  starts move by one DSP block; this is not a bit-identical waveform claim.
- Fixed-point optimization is byte-identical to a complete previous native
  modern render. Legacy Sound/Music defaults and factory packets are unchanged.

All-profile public-API target runs pass all nineteen cues, source looping,
ordinary-audio coexistence, mono/stereo, all three startups, three additional
stop/startup cycles, stop/restart and disposal. They report zero underruns/faults
and no surviving native session. A repeat with the latest small cue-clock/ramp
changes and the complete dense-motion target soak are in progress. Earlier pressure failures
were traced to dense motion exhausting the voice pool and exceeding the render
deadline. A subsequent pressure run had one isolated underrun near its end.
Incremental bus ramps remove the remaining per-sample divisions; mathematical
PCM oracles and full modern ambience comparisons still pass. Those earlier
pressure runs do not qualify this final renderer. Physical-console
acceptance of the newly added session remains pending.

The demo consumes the official public NuGet package after SDK publication.
Its approved category design adds Startup/Menu/Cards/Ambience/Samples and
read-only automatic-loop status. Downloadable artifacts contain executable DOL
code only, with no extracted firmware resources.
