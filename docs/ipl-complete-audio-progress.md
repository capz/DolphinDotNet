# Complete IPL audio implementation

This work follows the approved complete-audio plan. Milestones continue until
all reachable audio behavior in the three reference profiles is accounted for.
Normal startup and ten finite menu effects are already supported by SDK 343.

## Qualification ledger

| Family | Evidence | Remaining qualification |
| --- | --- | --- |
| Twelve original samples | All three reference images: 12 owned snapshots, three embedded loops; native AOT partial-frame loop oracle; GameCube-target LLE playback/stop/disposal | Physical-console confirmation |
| Startup variants | Live early-IPL controller selection plus original DSP voice source counts; all 3 profile snapshots; native Sound/Music variant factories | Complete persistent-score startup handover and physical-console confirmation |
| Remaining finite cues | Static event 13 and 14–22 sequence paths | Original visible actions, stop/release paths and uninterrupted LLE comparisons |
| Controlled card effects | Backward branches in events 17/18 and extended parameter instructions | External termination, ownership and complete port/interrupt semantics |
| Ambience | Live programs 6/7/8, source IDs 10/9/4; persistent root traces | All control states, loop continuity and full DSP routing |
| Output/global controls | Profile-specific clock/velocity and effect setup traced | Stereo/mono transitions, startup-to-idle handover, replacement/shutdown |

No guessed public cue names, raw event aliases or fixed-duration substitutes
are qualified by this ledger. Raw sample playback is structural extraction;
it does not claim to reproduce a sequenced effect or instrument envelope.

## Implementation checkpoints

- Automatic samples: catalog retention/enumeration, ownership-safe snapshots,
  and a facade whose parameterless Play honors the source loop are implemented.
  Legacy Sound.Play and Music.Play defaults are unchanged.
- Automatic-sample validation: synthetic metadata/foreign descriptor/cancel
  checks, native-budget failure, forced-GC host AOT (8 MiB heap), metadata-only
  inventory CLI and complete legacy audio regression suite pass. The 3-profile
  private check confirms byte-identical legacy packets after shared retention.
- GameCube-target acceptance: installed Dolphin 2503, JIT64/single CPU thread,
  OpenGL/DSP LLE, early reference IPL. All twelve samples play after input
  disposal; only the three looped sources remain active beyond source duration;
  Stop works, and sample disposal stops concurrent playbacks. Final native
  counts: zero clips and zero voices. No physical-console result is claimed.
- Next: qualify startup variants and remaining finite cues, then implement the
  persistent native session and its streaming mixer.
- Demo: revised mockup and confirmation before UI changes; consume only public
  SDK packages after publication, with a downloadable verified DOL.

Firmware, decoded copies, samples, captures, screenshots and runtime dumps stay
outside Git and downloadable artifacts. Qualification levels distinguish
generated fixtures, emulation and physical-console testing.

## Paired startup sources

Live reference boots use disposable controller pipes, DSP LLE and a self-authored
test disc. One held Z selects program 2, wave IDs 2/3, 166,745 samples; four held
Zs select program 1, IDs 0/1, 187,092 samples. Normal selects program 0, IDs 7/8,
174,067 samples. Every source is mono 32 kHz; each pair is interleaved losslessly.
These numeric program IDs are not public variant enum values.

The metadata/factory milestone exposes the paired sources, with no claim that
a standalone raw clip executes the original score's gain envelopes or handover.
Unknown profiles remain unavailable. Synthetic/host AOT and 3-profile private
checks pass. A GameCube-target DOL prepares all three variants through both Sound
and Music, disposes the inputs, plays/stops them and finishes with zero native
clips/voices in installed Dolphin 2503 DSP LLE. Legacy normal packets remain
byte-identical. The full native session remains a later milestone.
