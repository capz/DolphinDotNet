# Texture pipeline throughput

This work integrates texture timing PR #124 with wait/read PR #127, then profiles the same 48×32 world, camera route, RGBA8 texels and 192/256/384 KiB fine-texture budgets. Every successful run boots the real GameCube DOL in Dolphin, runs stress → nine benchmark cases → stress, checks independent rendered pixels and allocation ceilings, and checks native ownership on return. The managed heap remains 256 KiB; the scratch pool remains 84 KiB. There is still one shared background executor and no extra worker thread.

## SDK changes

* `Draw2D.FillRectangle` constructs its six vertices directly in the existing native canvas allocation. This removes managed scratch-array writes, general rotated-quad arithmetic and the second vertex conversion/copy. Vertex order, colors, batching, clipping and effect restrictions are preserved. `GpuProfiler.UseNative2DRectangles=false` retains the validation control.
* `GpuProfiler.OverlayGlyphs` selects the existing pixel geometry, horizontal runs or a non-overlapping rectangle cover of exactly the same bitmap font. Rectangles are the default. There is no font atlas, extra texture, permanent cache or added GPU allocation. Independent raster tests include endpoint rounding; representative text falls from 1,180 to 394 quads with the same pixels.
* `Texture2D.ReplaceRgba` reuses the allocation and the existing RGBA8 conversion, with a conservative prior-GPU-read wait and texture-cache invalidation. The checked array-range `Texture2D.Create` overload lets requests own separate slices of a fixed scratch pool.
* `StreamingTexture.AllocatedBytes` charges both front and back allocations. Existing `NativeBytes` retains its one-payload meaning.
* `GpuProfiler.ReuseFrameClear` reuses the completed same-color EFB clear. First frames, changed colors, raw/external GX and aborted frames require a clear. End-frame completion and depth/color clear remain. It is opt-in: copy-count telemetry proves elision, but this test did not establish a mean-frame benefit from it.
* Begin-frame copy and managed-gate reacquisition timings are separate probes. Timer completion checks, application timer polling and aligned 16/32/64 KiB reads from #127 are retained. This texture source performs no disk reads; use Hypercube's separate protocol-v8 profile for storage/audio validation.

## Pipeline experiments

The fixture keeps visible-first priority, a permanent coarse fallback, deterministic movement and stale-result rejection. Each outstanding job owns a scratch slice until its task completes and its exception is observed. A fixed pool avoids allocation churn and fragmentation at case transitions. The first per-case allocation prototype exhausted the unchanged heap; it was rejected rather than increasing the heap.

Small images compare depths 1/2/4/5. A 64 KiB image has depth 1 because the original 84 KiB pool cannot hold two such outputs. Publication is bounded by count, 64 KiB per frame and a 1,500 µs foreground limit, checked between uploads. A 750 µs comparison tests a tighter limit; it cannot interrupt an individual native upload.

`BenchmarkSource.FillRows` generates the three distinct rows of each procedural cell and uses checked `Array.Copy` for the remaining rows. Every world ID at all three group sizes is byte-identical to the original generator, including shared-scratch offsets and guard bytes. This is a source optimization, separate from the SDK-only geometry comparison; it is not storage throughput.

The matrices first isolate productive waits, clear reuse, buffering, batching and allocation reuse, then horizontal/rectangular font covers, then native rectangle construction. All modes have the same rendered world and texture payload budget. See [measurements](measurements/texture-pipeline.md), [CSV](measurements/texture-pipeline.csv) and [JSON/DOL hashes](measurements/texture-pipeline.json).

## Measurement protocol

The original 32-field strip remains unchanged. Completed cases rotate four 16-field diagnostic pages at 350 ms, held for four seconds from actual completion. The final case uses the same elapsed-time condition as other cases. Eight 2 Hz samples cover all pages for every sampler phase. Earlier captures that missed a frozen page or the post-benchmark stress return are rejected and excluded from the result tables.

Pages export the correlated worst frame, pipeline depths/publications/allocation counts and complete enqueue/preparation/ready delays, exclusive stage means and actual begin-frame copy counts, and the correlated exact P99 frame. Eight largest complete frame snapshots cover P99 up to 800 samples; the host checks the exact rank against the histogram and checks phase accounting. GPU/VI are subsets of END, and preparation overlaps other work; neither is added twice. Elapsed preparation includes preemption and is not CPU execution time. Histogram P99 has a 250 µs bucket; exact P99 is separately reported.

Reporting strips are rotated only after the measurement window. The old DOL and fixed-pool control are retained to check probe/pool overhead. The larger wins come from reducing SDK foreground work, not removing HUD information or accepting fewer world pixels.

## Reproduction

Build `tests/fixtures/TextureStrategy/TextureStrategy.csproj` in Release with `DndBuildBackend=Docker`, `DndTextureStreamingCI=true`, `DndTextureBenchmarkCI=true` and `DndTextureOptimization` from this table. The CI workflow runs all comparison lanes and preserves DOLs, JSON, screenshots and logs.

| Build value | Workload | Checker arguments |
| --- | --- | --- |
| 0 | Original scheduling/source and legacy SDK 2D controls; all nine layouts/budgets | `--benchmark --pipeline` |
| 12 / 13 | Scheduling matrix, 2×2 at 256 / 384 KiB | `--benchmark --pipeline --matrix-budget 256` / `384` |
| 14 | Font geometry matrix, 2×2 at 384 KiB | `--benchmark --pipeline --matrix-budget 384 --matrix-glyphs` |
| 15 | Native rectangle / queue / budget matrix, 2×2 at 384 KiB | `--benchmark --pipeline --matrix-budget 384 --matrix-rectangles` |
| 32 | SDK-only geometry improvements, original source and depth/publication limits; all nine cases | `--benchmark --pipeline` |
| 34 | SDK geometry plus bounded batching/reuse and row preparation; all nine cases | `--benchmark --pipeline` |

Run `scripts/dolphin_texture_test.py PATH.dol --mode pages --streaming --backend OGL --seconds 180 --logs FRESH_DIRECTORY` plus the table's checker arguments. CI allows 360 seconds for slower hosts. Local comparisons use the same Debian trixie software-OpenGL image, CPUThread=false and full texture-cache hashing. They are emulator results, not measured GameCube hardware speedups.

Do not retire validation switches or the legacy read mode until repeated performance and hardware validation establish the preferred path. Keep historical protocol/results intact. More background threads were not added: on the single CPU they would not parallelize managed work, would consume stack/queue memory, and are not supported by evidence from this procedural workload.
