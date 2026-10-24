# TextureStrategy: moving-camera residency prototype

A runnable GameCube experiment for Hypercube's page-versus-atlas decision. The camera moves through a 48×32-cell procedural world whose full-resolution textures would occupy 6 MiB. Only 192 KiB of detail is resident at a time.

## Run

Install the .NET 8 SDK and the existing DolphinDotNet Docker or native build prerequisites described in [the IDE guide](../../docs/ide-workflow.md).

```sh
dotnet build samples/TextureStrategy/TextureStrategy.csproj -c Release -m:1
```

Open `samples/TextureStrategy/bin/Release/net8.0/TextureStrategy.dol` in Dolphin or on GameCube. The root `TextureStrategy.slnf` remains an IDE entry point.

- **A:** switch between page and atlas caches; release the old native textures.
- **Y:** toggle the automatic sweep/reversal route; use the left stick for manual movement.
- **X:** toggle deterministic source-failure injection.
- **B:** drain the outstanding worker request while continuing to present, then exit.
- **Z:** existing diagnostics toggle.

Both layouts use the same generated texels and camera. Pages are 32×32 RGBA8 (4 KiB); atlases group 4×4 cells into 128×128 RGBA8 (64 KiB). The active cache holds at most 48 pages or three atlases, with identical 192 KiB budgets. Existing visible entries are protected from eviction, so an oversubscribed working set falls back rather than churning every frame.

## Residency and fallback

A permanently resident 48×32 coarse texture costs 6 KiB and supplies one average-color texel per world cell. Visible coarse geometry draws first. Resident detail overlays the matching cells or patches. Missing, delayed, cancelled and failed requests therefore retain coverage.

A single worker task generates the requested pixel payload into one of two reusable scratch arrays (4 KiB and 64 KiB). The application polls completion; it never waits on an incomplete task. Completed, still-relevant requests create actual GX textures between frames. An obsolete request is observed and discarded before its scratch buffer is reused. A mode generation prevents an old request publishing into the new layout. Eviction clears material bindings and disposes the native resource after the previous frame completed.

The upload scheduler replenishes 4 KiB per rendered frame, caps credit at 64 KiB, and enforces a minimum four-frame request delay. This deliberately exercises fallback and heterogeneous upload sizes. Source-failure injection fails selected first attempts, backs off twenty frames, and allows retry. These timing constraints are experiment controls, not measurements of storage bandwidth.

Runtime assertions enforce exact native ownership: the fixed scene baseline plus currently resident detail must equal the graphics allocator's byte count. Detail must never exceed 192 KiB. Scratch buffers, coarse data, geometry and telemetry are accounted for separately.

## Measurements and CI

The overlay reports real promotions, evictions, native resident bytes, uploaded/requested bytes, fallback/detail cell counts, failures, cancellations, mode switches, and p95/p99 promotion latency in rendered frames. Telemetry also records per-upload CPU microseconds, maximum upload cost, and the previous complete frame time (including presentation/vsync). These are not separate CPU/GPU timings.

Build the automated stress variant with `-p:DndTextureStreamingCI=true`. It injects source failures, follows the normal camera route, and switches modes every 240 rendered frames. `-p:DndTextureAtlasStart=true` selects the initial layout. The bottom binary strip encodes frame-local counters for CI; the regular performance overlay is disabled in this variant so it cannot obscure that strip.

`.github/workflows/texture-dolphin.yml` builds and boots Debug and Release DOLs starting in each layout. Each Dolphin software-rendering run lasts 90 seconds and must demonstrate:

- Continued capture growth and at least 360 actual guest frames.
- Real detail and coarse fallback pixels in both layouts, matching the current camera's expected world colors.
- Camera sweeps/reversals, native eviction, source-failure recovery, stale-request cancellation and an in-process mode switch.
- Detail/native ownership and upload-budget invariants, with no holes, early shutdown or PPC faults.

Artifacts include compact screenshots, JSON counter samples, emulator logs, DOL/ELF and the full capture. Run the independent checks with:

```sh
dotnet run --project tests/TextureStreamingTests -c Release
python3 -m unittest discover -s tests -p test_dolphin_streaming.py
```

## Scope and next gate

This implements real bounded MEM1/GX texture residency and asynchronous procedural pixel preparation. It does not implement disk reads, ARAM promotion, compressed pages, texture gutters, mip selection, world collision/gameplay or a storage-backed virtual texture system. Filtering is nearest-neighbor in both layouts; there is one always-available coarse level.

Use the results to compare granularity, working-set pressure and fallback behavior. Do not use emulator timings or the paced procedural source to choose a hardware streaming architecture. Next replace the source with actual storage/ARAM transfers and profile representative geometry, audio and gameplay on an original GameCube. The older `CacheTrace.cs` logical trace remains historical and is no longer used by this sample.
