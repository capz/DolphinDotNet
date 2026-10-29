# Regression test audit — 2026-10-29

Scope: main at `2c95c7522ab12fceee0efb7d8cb548568da958bc`, concentrating on
PRs #128–#131: texture/2D throughput, indexed mesh submission, typed AOT array
access and high-level graphics phases 1 and 2. This is a targeted regression
audit, not a statement of exhaustive branch coverage or physical compatibility.

## Gaps closed

| Area | Gap | Regression protection |
| --- | --- | --- |
| Scene capture | Native capture accepted odd EFB origins and RGBA8 targets whose dimensions were not multiples of four | Reject before the GX backend; test both axes, dimensions, EFB edges, overflow-sized coordinates, frame/pass state, unsupported formats and disposed targets |
| Mip packages | Existing native test primarily exercised square RGBA8 full chains | Check every prefix of rectangular 16x8 chains in all eight supported GX formats, exact payload preservation, truncated/overlong chains and no allocation on rejection |
| Retained geometry | Existing tests did not verify immediate → retained → immediate painter order | Record backend submissions and require 6 → 3 → 6 vertices; invalid matrices/disposed borrowed textures cannot flush queued draws |
| AOT array fast paths | Existing test concentrated on u4 stores and a few floating patterns | Differential checks against checked runtime loads/stores for u1/u2/u4/r4/r8; eight strides including unaligned elements; all valid indices and invalid indices; scalar/reference mismatch and null precedence; zero, signed zero, infinities, quiet NaNs, subnormals and integer extrema |
| Native CI optimization | Native unit tests ran with default compiler flags, unlike shipped release O3/LTO | Run all native tests in both default and `-O3 -flto -fno-strict-aliasing` modes, cleaning between builds and retaining assertions; test cleanup and failure propagation |

The capture regression fails against the original runtime. The validation fix
prevents invalid inputs reaching GX; valid exact-size and half-size edge copies
remain accepted. The new native binary is part of `make -C tests test`.

## Validation

- All 32 native test binaries passed with default compiler flags and again with
  O3/LTO and strict warnings enabled on the Linux host.
- Both expanded array tests and new graphics resource tests passed AddressSanitizer
  and UndefinedBehaviorSanitizer. LeakSanitizer is unavailable under this execution
  environment's tracing; it was disabled for these runs. Resource tests separately
  require zero live native resources and zero accounted bytes at completion.
- CI-runner tests and the existing change-selection tests passed.
- The local environment has no .NET SDK or reachable shell network proxy. Managed
  compiler/AOT, asset-converter and PowerPC/Dolphin checks require repository CI;
  they were not rerun locally for this audit.

## Existing coverage and remaining gates

Existing native tests cover CRC reference/incremental identities, indexed GX
layout/index splitting/cache invalidation, overlay raster equivalence and command
cache lifetime, texture replacement, streaming I/O fault paths, scheduler progress,
GC/stop-the-world coordination and resource accounting. Graphics CI also executes
Debug/Release AOT smoke fixtures under GC stress and validates rendered Dolphin
patches and paired submission timings. These checks complement host unit tests.

At audit time, main's TextureStrategy and high-level graphics Dolphin workflows
were green. Its aggregate framework workflow was red because assets, gamecube,
audio and SDK jobs were cancelled; their missing results cannot be treated as a
pass. Require a complete green CI result for this change before merging.

Remaining acceptance includes real PowerPC optimizer/ABI behavior, GPU/VI timing,
BBA/USB Gecko physical attachment and reconnect, real cache/DMA synchronization,
and performance across representative complete scenes. Host mocks cannot establish
these properties. The debugger now lives in DolphinDotNet.Debuggers and its
physical-device gate remains separate. No finite unit suite establishes that
all possible regressions are absent.
