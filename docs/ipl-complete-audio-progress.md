# Complete IPL audio implementation

This work follows the approved complete-audio plan. Milestones continue until
all reachable audio behavior in the three reference profiles is accounted for.
Normal startup and ten finite menu effects are already supported by SDK 343.

## Qualification ledger

| Family | Evidence | Remaining qualification |
| --- | --- | --- |
| Twelve original samples | All three reference images: 12 owned snapshots, three embedded loops; native AOT partial-frame loop oracle; GameCube-target LLE playback/stop/disposal | Physical-console confirmation |
| Startup variants | Three paired instruments, common IBNK fingerprint across early NTSC/NTSC 1.2/PAL 1.2 | Live one-controller/four-controller Z selection and output captures |
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
