# Opaque model batching and submission counters

`Renderer.DrawModels` and the model portion of `RenderQueue` now flatten and
group opaque submeshes by shared model, material and mesh. Limits remain 128
objects and 4096 total meshes. The bounded sort uses 64 KiB of extra static
scratch and allocates nothing per frame. Global stable back-to-front
translucency remains a separate pass; it is not reordered by material.

Within one native batch, compatible repeated materials reuse GX depth/blend,
alpha, TEV, sampler and texture-object state. Object/normal matrices, mirrored
culling and register tint remain per draw. The default headlight is loaded once
per batch. Indexed vertex cache is invalidated when the mesh/evaluated skin
source changes, after a direct fallback, and at the first indexed draw of a new
batch. A repeated geometry run can retain it. Ordinary indexed meshes also track
source/layout changes and are marked dirty across imported models. No state
pointers are trusted across native model batch boundaries.

Unbatched calls and unsupported indexed inputs retain their previous paths.
This is state/submission reuse, not hardware instancing or polygon reduction.
Do not substitute `DrawMeshBatch` merely to reduce native calls: its current
shared-transform implementation expands into direct staging chunks.

Native diagnostic `dnd_model_state_cache(-1)` queries the default-on mode. Zero
and one switch it outside device/model frames; other values are rejected. It is
independent of the indexed-model diagnostic and does not change resources.

## Counters

`GpuProfiler.Snapshot().DrawSubmissions` includes imported direct/indexed
primitive submissions and hardware-size splits in the SDK mesh paths.
`TextureBindings` counts issued texture-object loads, including both maps of
compiled materials. They do not observe arbitrary raw GX or user display lists.

Two additional counters reset at device BeginFrame:

- `GpuProfiler.DrawingApiCalls`: native drawing entry attempts for canvas shapes,
  sprites/retained geometry, meshes/batches and models/batches. Excludes getters,
  state setters, allocation/upload and raw GX.
- `GpuProfiler.MaterialChanges`: full material pipeline setups in either native
  renderer. Object matrices and tint alone are not additional setups.

`Draw2D.DrawTexturedQuads` accepts ordered XYUV float arrays and texture references
for custom text/layout engines, bounded by SpriteCapacity. It reuses native sprite
validation and avoids managed SpriteDraw conversion; normalized UVs may repeat.
Camera transforms use reserved staging; ordinary canvases borrow coordinates
without another managed copy. Tint-only changes in the ordinary backend reuse
material state; direct vertex colors and indexed register tint are still applied
per draw.

The former imported-model counts were missing; historical snapshots cannot be
compared as complete draw totals. Recording fixtures compare geometry, alpha,
TEV, ordering, no-allocation behavior and actual GX loads between modes,
including multiply/emission materials and indexed fallback. Split-mesh tests
check two primitive submissions with one texture load; reset and diagnostic
boundaries are also covered.
