# Phase 2: imported GX materials and batch transparency

## Import contract

`--compile-gx-materials` (`ImportOptions.CompileGxMaterials`) explicitly selects
an 8-bit GX fixed-function mapping. It is separate from
`--allow-material-approximation`: normal maps and metallic/roughness contributions
still fail unless their omission is explicitly accepted with that existing flag.
The implementation is not a full glTF PBR renderer or physically based bake.

- Base texture/color is combined with vertex color and existing diffuse light.
- Occlusion uses source red and strength, producing `1-strength+strength*R`.
  It multiplies the lit base as a separate map when emission is absent. This is
  a GX mapping, not glTF's physically distinct ambient-only occlusion term.
- Emission is factor-scaled into its own map and added after base lighting. It
  is not accidentally attenuated by diffuse lighting or base tint. Constant
  emission uses a constant secondary map. Alpha always comes from raster alpha
  times the original base map, never the emission/occlusion map.
- When all three source maps are present, compatible base/AO are resolved offline
  and emission remains separate at runtime. Different base/AO samplers reject
  that offline combination. Independent samplers are retained for runtime maps.
- Source maps must select the same UV set and KHR_texture_transform; that UV
  stream is transformed once. Different UV layouts reject rather than dropping a
  contribution. UV0/UV1 selection is supported; only one target stream is stored.
- KHR_materials_unlit retains its base-only semantics; emission and occlusion
  do not alter an unlit source material.
- Texture dimensions/mips follow the existing converter limits; factor scaling
  and TEV combination use bounded, saturating 8-bit GX arithmetic, not HDR or
  linear-light PBR. Source-dependent energy conservation is not promised.

## Backward-compatible package extension

DNMD v2 keeps the 160-byte header and six section descriptors/hashes. Only the
material record extends from 32 to 40 big-endian bytes:

| Offset | Field |
| --- | --- |
| 0 | Four float color components |
| 16 | Signed base texture index (-1 for none) |
| 20 | Existing render flags |
| 24 | Alpha cutoff float |
| 28 | Double-sided flag |
| 32 | Signed secondary texture index (-1 for none) |
| 36 | Combiner: 0 original, 1 multiply, 2 add |

Combiner zero requires secondary -1; nonzero requires a valid secondary index.
Unknown versions/combiners, bad references, inconsistent records and truncated
sections fail before model publication. No managed resource tokens are stored in
packages. Native source importers and ordinary offline conversion retain v1.
ARAM unload/restore preserves the versioned records through the existing model
package path. Old SDKs intentionally reject v2; use a current runtime with the
new converter flag.

Imported rendering binds authored texture/sampler state and reuses the shared
TEV binder with a bounded three-stage program. A per-draw RGB effect supersedes
the imported combiner; geometry, skinning and alpha ownership remain unchanged.

## Global transparency

Allocate token and flattened matrix scratch once, outside the render loop:

```csharp
var tokens = new int[objects.Length];
var matrices = new float[objects.Length * 16];
// In an active Renderer frame/pass:
renderer.DrawModels(objects, tokens, matrices);
```

The batch accepts up to 128 objects and 4096 aggregate meshes. It preflights
handles, residency, matrices, normal transforms and skinned buffers before any
submission or triangle accounting. All opaque/cutout meshes are drawn first,
followed by one global stable far-to-near translucent submesh pass. Depth uses
the active camera and transformed mesh center, including rigid node transforms.
A heap sort uses 64 KiB of fixed native scratch; there is no per-frame allocation
or persistent model mutation. Empty GameObjects submit nothing. Equal depths
preserve object/mesh input order. Per-triangle ordering and intersecting/cyclic
transparent surfaces remain outside this API.

## Validation and remaining work

Desktop fixtures check v1/v2 output, material references, factor-scaled texture
pixels, AO red/strength mapping, alpha retention, combined contributions and
invalid UV/texture/factor data. Native tests validate v2 parsing and rejection;
AOT smoke exercises v2 ARAM restore and the batch binding. The recording GX test
checks second-texture stage selection, final base-alpha selection, per-draw
override precedence, opaque-first/global submesh order, stable ties, zero draw-time
allocation and whole-batch rejection before submission.

Faithful PBR baking remains a separate decision: it requires a chosen lighting
or HDR environment and a policy for view-dependent reflections. No authoring
lighting is guessed. Physical-console and independent visual-reference acceptance
remain necessary; command recording and host smoke tests do not prove pixels.
