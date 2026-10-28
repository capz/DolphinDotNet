# High-level graphics expansion

Phase 1 is implemented; see [API usage and validation](graphics-phase-one.md).
Phase 2 is implemented; see [API usage and costs](graphics-phase-two.md). Existing signatures, defaults, frame ownership,
asset formats and application-executor requirements remain supported.

## Phase 1

1. **2D camera and viewport:** translation, zoom and rotation; viewport-aware
   batches; bounded nested screen-space, axis-aligned intersecting clips.
2. **Sprite submission:** texture regions, horizontal/vertical flips and bounded
   bulk ranges with reusable staging. Preserve painter order and adjacent batching.
3. **Sampling and UVs:** independent base/secondary clamp, repeat, mirror and
   nearest/linear filters; GPU texture matrices for material UV transforms.
   Preserve indexed mesh fetch. Mip chains are outside this phase.
4. **3D cameras and visibility:** mutable perspective and orthographic projections,
   projection/unprojection, picking rays and explicit conservative bounds tests.
   Animated bounds are supplied conservatively by the application.
5. **Lighting and fog:** pass-level ambient, up to eight directional/point/spot GX
   lights and configurable fog. Existing camera-space headlight behavior remains
   the default. Unlit draws retain their fast path.
6. **Scene submission:** preallocated native mesh batches supporting distinct
   transforms, improved opaque queue grouping and model-instance queue support
   using the existing stable global model transparency sorter. Bulk submission
   reduces CPU crossings; it does not imply hardware instancing.

Validation must cover host math/contracts, AOT under GC stress, native preflight
and resource lifetime, raw/2D/3D state transitions, real GameCube compilation,
Dolphin pixels and CPU submission benchmarks. Report allocation, native memory,
GX draw/texture-bind counts and execution separately from VI/display pacing.
Hardware appearance and throughput remain unverified until measured physically.
No performance improvement is claimed from a source-only change.

## Phase 2

* Reusable bitmap text layouts with wrapping/alignment and cached glyph placement;
  recorded 2D geometry for static HUDs, menus and tile layers. Keep ownership and
  disposal explicit for every referenced texture, effect and native command list.
* Setup-time tessellated rounded rectangles, circles, gradients, basic 3D meshes
  and billboards. Bound geometry size and avoid per-frame tessellation/allocation.
* Versioned mipmapped texture packages and explicit EFB-copy scene captures for
  minimaps and simple TEV post-processing. Preserve v1 loading and expose copy,
  synchronization and memory costs; GX uses the shared EFB.

These additions were approved for implementation. Workload-based memory/bandwidth
measurements and physical hardware validation remain required. They do not promise PBR, programmable shaders,
GPU instancing or an additional rendering/background thread.
