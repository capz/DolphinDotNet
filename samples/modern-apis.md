# Samples using the current APIs

All samples include the small helpers in [Common](Common). The shared generated
3×5 bitmap atlas is drawn through `Draw2D.DrawText`; number slots cache formatted
values. The helpers are examples compiled into each sample, not a new SDK layer.
Z continues toggling the native statistics/debug overlay in every sample.

| Sample | Improvements |
| --- | --- |
| Kart | Elapsed-time network deadlines and race clock, seeded pickups, engine audio/cues/rumble, hotplug-aware best-time save |
| HttpTexture | Live progress, cancellation, retry, bounded streaming PNG decode/display |
| GoogleLogoAram | Stream to ARAM, bounded MEM1 decode readback, actual image display |
| DiscBrowser | Sorted scrolling GUI, folders, banner, refresh and mount cleanup |
| Fox | Original animated model with fallback, remappable fire, routed input, best score, rumble |
| GreenMen | Remappable fire, routed input, best score, rumble |
| Pong | Routed input, per-player persistent best win streaks, rumble |
| Snake | Routed input, shared text, run-end rumble; save/RNG compatibility retained |
| AudioDemo | Async loading, WAV/ADX switching, volume/pitch and explicit ADX loop controls |
| HardwareDiagnostics | Named inventory, live controller/card changes, repeatable scan |
| ShaderDemo | Interactive thresholds/color and graph/preset switching with cached uniforms |
| ManagedGameCube | FrameClock-based rotation |
| HelloGameCube | Supported standard numeric formatting |
| AsyncNetworking | Stage/status diagnostics, elapsed timing and cancellation watchdog |

Each sample's README describes controls and setup. `SampleRecord` checks both
native card slots every two seconds, merges valid checksummed records, retains
pending results without a card, retries writes and flushes on exit. Writes run
between frames and can briefly stall a frame; this is not a background save queue.
General math helpers replace duplicate game clamps/absolute-value implementations.

Validation combines managed rules/ownership tests, AOT lowering, native compilation,
real socket fixtures, and Dolphin boot/render/ISO acceptance. Host download tests
use real loopback HTTP but substitute graphics and ARAM; they do not establish
physical BBA or DMA behavior. Physical hotplug, rumble/audio quality and two-console
LAN play remain hardware acceptance. Model generation is reproducible from the
original glTF source via `scripts/generate_sample_model.py --check`.
