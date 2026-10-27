# TextureStrategy: moving-camera residency prototype

A runnable GameCube experiment for Hypercube's page-versus-atlas decision. The camera moves through a 48×32-cell procedural world whose full-resolution textures would occupy 6 MiB. Only 192 KiB of detail is resident at a time.

## Run

Install the .NET 8 SDK and the existing DolphinDotNet Docker or native build prerequisites described in [the IDE guide](../../../docs/ide-workflow.md).

```sh
dotnet build tests/fixtures/TextureStrategy/TextureStrategy.csproj -c Release -m:1
```

Open `tests/fixtures/TextureStrategy/bin/Release/net8.0/TextureStrategy.dol` in Dolphin or on GameCube. The project can be opened directly in an IDE.

In Dolphin, set **Graphics → Hacks → Texture Cache → Accuracy** to **Safe** (the configuration value is `[Settings] SafeTextureCacheColorSamples = 0` in `GFX.ini`). Atlas streaming reuses native addresses with content that can differ only in RGBA8's green plane; sampled texture hashing can reuse an earlier image. CI sets full hashing explicitly.

- **A:** switch between page and atlas caches; release the old native textures.
- **Y:** toggle the automatic sweep/reversal route; use the left stick for manual movement.
- **X:** toggle deterministic source-failure injection.
- **B:** drain the outstanding worker request while continuing to present, then exit.
- **Z:** existing diagnostics toggle.
- **START:** enter the comparison benchmark described below; press START there to return to this stress test.

Both layouts use the same generated texels and camera. Pages are 32×32 RGBA8 (4 KiB); atlases group 4×4 cells into 128×128 RGBA8 (64 KiB). The active cache holds at most 48 pages or three atlases, with identical 192 KiB budgets. Existing visible entries are protected from eviction, so an oversubscribed working set falls back rather than churning every frame.

## Residency and fallback

A permanently resident 48×32 coarse texture costs 6 KiB and supplies one average-color texel per world cell. Visible coarse geometry draws first. Resident detail overlays the matching cells or patches. Missing, delayed, cancelled and failed requests therefore retain coverage.

A single worker task generates the requested pixel payload into one of two reusable scratch arrays (4 KiB and 64 KiB). The application polls completion; it never waits on an incomplete source task. A fixed one-millisecond frame delay releases the runtime execution gate so the background executor can run; this pacing is independent of source completion. Completed, still-relevant requests create actual GX textures between frames. An obsolete request is observed and discarded before its scratch buffer is reused. A mode generation prevents an old request publishing into the new layout. Eviction clears material bindings and disposes the native resource after the previous frame completed.

The upload scheduler replenishes 4 KiB per rendered frame, caps credit at 64 KiB, and enforces a minimum four-frame request delay. Uploads are atomic: a 64 KiB atlas can spend sixteen frames of accumulated credit in one upload. This deliberately exercises fallback and heterogeneous upload sizes. Source-failure injection fails selected first attempts, backs off twenty frames, and allows retry. These timing constraints are experiment controls, not measurements of storage bandwidth.

Runtime assertions enforce exact native ownership: the fixed scene baseline plus currently resident detail must equal the graphics allocator's byte count. Detail must never exceed 192 KiB. Scratch buffers, coarse data, geometry and telemetry are accounted for separately.

## Measurements and CI

Counters and latency percentiles accumulate across mode switches; a live switch is a stress test, not a reset of the measurement interval. The overlay reports real promotions, evictions, native resident bytes, uploaded/requested bytes, fallback/detail cell counts, failures, cancellations, mode switches, and p95/p99 promotion latency in rendered frames. Telemetry also records per-upload CPU microseconds, maximum upload cost, and the previous scene-present duration (including source-result validation, upload, drawing and presentation/vsync, excluding input, camera movement and the one-millisecond pacing delay). These are not separate CPU/GPU timings.

Build the automated stress variant with `-p:DndTextureStreamingCI=true`. It injects source failures, follows the normal camera route, and switches modes every 240 rendered frames. `-p:DndTextureAtlasStart=true` selects the initial layout. The bottom binary strip encodes frame-local state and cumulative counters for CI; the regular performance overlay is disabled in this variant so it cannot obscure that strip.

`.github/workflows/texture-dolphin.yml` builds and boots Debug and Release DOLs starting in each layout. Each Dolphin OpenGL run uses Mesa on the CI CPU, with full texture hashing enabled, lasts 90 seconds and must demonstrate:

- Continued capture growth and at least 360 actual guest frames.
- Real detail and coarse fallback pixels in both layouts, matching the current camera's expected world colors.
- Camera sweeps/reversals, native eviction, source-failure recovery, stale-request cancellation and an in-process mode switch.
- Detail/native ownership and upload-budget invariants, with no holes, early shutdown or PPC faults.

The compact `texture-proof-<configuration>-<initial-layout>` artifact includes the runnable DOL, ELF, screenshot, JSON counter samples and emulator log. The larger `texture-dolphin-…` artifact also includes the full capture. Run the independent checks with:

```sh
dotnet run --project tests/TextureStreamingTests -c Release
python3 -m unittest discover -s tests -p test_dolphin_streaming.py
```

## Comparison mode in the same DOL

Press **START** from the original stress test. The comparison mode runs nine cases: pages, 2×2-cell atlases and 4×4-cell atlases, each at **192, 256 and 384 KiB**. Every case starts with an empty detail cache, observes a stationary camera for two seconds, then follows the same six-second, wall-clock-driven sweep/reversal and row jump. A short result hold separates cases. The complete suite takes approximately 77 seconds, then leaves its results table on screen.

Visible intersections rank ahead of the one-cell prefetch border. Larger on-screen contributions rank first, with center distance as a tie-breaker. Resources outside the selected working set can be replaced even if they are in the prefetch border. This policy is confined to the comparison mode; the original center-out, protected-window stress test is retained.

Comparison controls:

- **A:** clear all results and rerun the complete suite.
- **X:** select the next layout at the current budget and run that individual case.
- **Y:** select the next budget with the current layout and run that individual case.
- **D-pad left/right:** inspect the detailed result for another case.
- **R:** show the results table, including which cases are still pending.
- **START:** drain pending preparation, dispose comparison resources, and return to the original stress test.
- **B:** drain and exit the application.

The table shows `LAYOUT`, budget in `KIB`, `STATIC` detail coverage at the end of the initial two seconds, `MOVE` time-weighted mean detail coverage during movement, `F99MS` frame-interval p99, and total `UPKIB` installed. Coverage columns are percentages. `PAGE`, `A2X2` and `A4X4` identify the three layouts. Read coverage together with frame cost: a faster result with more coarse fallback is delivering less detail.

The detailed view also reports p95/max frame intervals, native resident/useful bytes, requested and uploaded bytes, eviction count, `FILL95 MS` (time to 95% during the cold stationary interval; **-1 means it was not reached**), `LOAD95` successful request-to-promotion p95 in milliseconds, maximum upload cost in microseconds, maximum scene-submission cost, and the longest continuous time coverage stayed below 95%. Completed rows show peak resident bytes; the running row shows current resident and useful bytes. The UI refreshes once per second.

Coverage is computed from the intersection of resident cells with the entire projected world viewport, including the world behind the diagnostic text. It excludes prefetch-only area and accounts for partially clipped cells. Useful KiB is the corresponding visible texel footprint; resident bytes outside that footprint include border allocations and clipped portions of atlases. CI independently checks the allocation ceiling and rendered world pixels. Frame intervals include the fixed one-millisecond executor handoff and presentation/vsync. Submission cost includes beginning the frame and submitting world draws; it is not an isolated GPU measurement. Percentiles use bounded histograms (250-microsecond frame buckets up to 500 ms and one-millisecond promotion buckets up to two seconds); the maximum frame time is retained separately.

This mode has **no injected failures, four-frame minimum delay, or 4-KiB-per-frame upload-credit limiter**. It retains one asynchronous procedural preparation request and at most one promotion per frame. These are measurements of this pipeline with generated pixels, not storage/ARAM bandwidth. Statistics reset for each case and remain available across the nine-case suite. START switches between test modes by disposing/recreating their scenes, so a new stress session starts its counters afresh.

CI retains the four original stress runs and adds Debug/Release round-trip runs using `-p:DndTextureStreamingCI=true -p:DndTextureBenchmarkCI=true`. Those builds start in stress mode, automatically enter the full comparison, then return to stress mode. Their six-minute wall-clock captures allow for slower emulation; the suite itself uses the guest's monotonic clock. The checker requires all nine cases, actual pixels from each layout, stable native ownership across cases and return, valid coverage ceilings and budget limits. Their `texture-proof-*-benchmark` artifacts contain the same runnable DOL, a `benchmark-summary.png` table, and measurements. Other builds use START to enter the comparison manually.

## Scope and next gate

This implements real bounded MEM1/GX texture residency and asynchronous procedural pixel preparation. It does not implement disk reads, ARAM promotion, compressed pages, texture gutters, mip selection, world collision/gameplay or a storage-backed virtual texture system. Filtering is nearest-neighbor in both layouts; there is one always-available coarse level.

Use the results to compare granularity, working-set pressure and fallback behavior. Do not use emulator timings or the paced procedural source to choose a hardware streaming architecture. Next replace the source with actual storage/ARAM transfers and profile representative geometry, audio and gameplay on an original GameCube. The older `CacheTrace.cs` logical trace remains historical and is no longer used by this sample.
