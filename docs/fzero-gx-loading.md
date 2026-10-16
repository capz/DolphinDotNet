# F-Zero GX on-device loading

```csharp
using var model = await Model.LoadFZeroGxAsync(
    "sd:/assets/vehicle.gma.lz",
    textureArchivePath: "sd:/assets/vehicle.tpl.lz",
    cancellationToken: cancellationToken);
using var instance = model.CreateInstance();
renderer.BeginFrame(camera);
renderer.DrawModel(instance, worldMatrix);
renderer.EndFrame();
```

Mount the source volume first. Import uses the existing background executor,
native memory budgets, `Model`, instances, texture cache, renderer and ARAM
residency. No desktop conversion is required. Default `await` resumes on its
caller's executor; rendering requires the application executor.

## Containers and selection

- Raw **GMA** archives and individual **GCMF** files.
- F-Zero **LZSS** streams, including both stored-size conventions, zero-initialized
  dictionary references, overlapping references and bounded nested compression.
- **U8/ARC** directories, including an LZ-compressed outer archive and compressed
  GMA/TPL members. A source with one geometry member selects it automatically.
- `"sd:/vehicle.arc#assets/vehicle.gma"` selects an exact, case-sensitive member
  path. The texture path accepts the same syntax. Without an explicit texture
  path, one unambiguous TPL member in that ARC is used. Multiple candidates require
  explicit member paths; no arbitrary first-file selection occurs.
- `modelIndex: -1` loads every nonempty model in the selected GMA. A nonnegative
  index selects the original GMA table entry, preserving holes in the archive.
  An individual GCMF accepts `-1` or `0`. Model names become native node names.

Paths remain mounted-volume paths. Archive members are decoded in memory, never
extracted to disk. File references, names, directory depths, counts, sizes and
output expansion are bounded and validated. Source names must be printable ASCII;
node names are limited to the renderer's 63-byte limit.

## Geometry

Supported GCMF geometry includes float VAT0 and signed fixed-point VAT1 vertices,
positions, normals, NBT normals, vertex colors and texture coordinates. GX triangle
lists, triangle strips, triangle fans and quads become validated native triangles.
Front/back display-list groups preserve winding; extra display-list groups and
per-shape matrix-slot overrides are supported for direct geometry. Missing normals
are generated and all normals are normalized.

Stitching/rigid matrix-bound geometry supports identity slot zero and the eight
mapped matrix slots. Coordinates retain source units; no meter conversion is inferred.
Source affine matrices are baked into positions; inverse
transpose matrices transform normals. This preserves affine transforms without
forcing sheared matrices into an approximate TRS decomposition. It does not add
skeletal animation.

`loadDeformedMeshesAsStatic: true` explicitly permits **indexed skin/effect vertex
pool snapshots**. These use stored positions/normals/UVs and strip indices. They
contain no inferred bones, weights, physics simulation or motion. Without this
option those GCMF flags fail. Extra indexed display-list variants remain unsupported.
Use `modelIndex` to select rigid models from an archive containing other variants.

Unknown GPU commands and unsupported primitives fail. Source display lists are
parsed as data and are never executed or submitted directly to GX.

## Textures

Amusement Vision **TPL** supports I4, I8, IA4, IA8, RGB565, RGB5A3, RGBA8 and CMPR.
The AV header is distinct from Nintendo's usual TPL header. Empty/unreferenced TPL
slots are permitted; a referenced missing texture fails.

Power-of-two images from 1 through 1024 pixels are decoded to GX-tiled RGBA8.
Dimensions below four are nearest-resampled to four for native renderer support.
Authored mip levels are retained down to a minimum of 4x4, bounded by the
descriptor maximum-LOD field. Its special value 15 limits the smallest mip to
16x16; a zero maximum LOD retains only the base level.
All declared source levels are range-checked, including levels below that limit.
Wrap, mirror and nearest/linear magnification settings are retained. Sampler variants
of a texture get separate records while immutable equal pixels share the cache.

The reader consumes F-Zero's documented CMPR mip strides, including its 4-pixel
alignment bug. If a retained level lacks the bytes needed for a complete GX tile,
strict import fails. `allowMaterialApproximation: true` permits zero-filling those
missing encoded bytes; it cannot recover missing source texels. Indexed palettes
are unsupported because AV TPL provides no verified palette table in this profile.

## Materials and transparency

Supported surfaces include unlit/simple diffuse shading, base color, vertex color,
one selected diffuse texture, two-sided flags, alpha-zero cutouts, alpha blending,
additive blending and the eight GX source/destination blend factors. Lighting uses
the existing renderer's fixed simple diffuse model rather than the game's complete
lighting system. Custom ambient/specular/TEV contributions are not reproduced.

`allowMaterialApproximation: true` permits selecting the first ordinary diffuse
layer and discarding additional TEV layers, reflections, material ambient terms,
texture swapping and unsupported surface contributions. Strict mode rejects those
features. This flag does not permit unknown geometry or implicit deformation
approximation. Unverified nonidentity texture matrices remain unsupported.

Opaque meshes draw first; transparent meshes are sorted back to front by transformed
bounding-box center **within each model draw** and do not write depth. Sorting uses
budgeted instance buffers, with no per-frame allocation. Intersecting transparent
triangles and transparency spanning separate model draw calls can require application
ordering; this is not a global per-triangle sorter.

The existing DNMD material mode word now assigns bit 0 to cutout, bit 1 to unlit,
bit 2 to blending, bit 3 to additive blending and bit 4 to explicit GX factors.
With bit 4 set, bits 5..7 and 8..10 hold source/destination factors; otherwise those
bits must be zero. Cutout and blending are mutually exclusive. Existing mode values
0..3 retain their meaning. Package validation rejects invalid flag combinations.

## Limits and execution

The default native budget is 8 MiB, configurable to at most 16 MiB. Source and
expanded F-Zero files are limited to 16 MiB; fitting that limit does not guarantee
that parsing, image decoding, package construction and publication overlap fit the
budget or available GameCube RAM. All of those allocations are charged. Other
source loaders retain their existing 2 MiB file limits.

GMA archives support up to 1024 entries/nodes. The renderer supports up to 4096
meshes and textures and 4097 materials. Each decoded display-list group contains at
most 65,536 vertices. U8 archives are limited to 4096 entries and 32 directory levels.
F-Zero decompression permits at most three compression layers at each unwrap stage.

Parsing/decoding releases the managed execution gate, so application rendering and
GC can continue. Source imports share one service executor. Storage operations and
final package publication reacquire the gate and can delay a frame. Cancellation
is checked before loading and after native parsing; canceled imports clean up the
native result before completing. See [source-loader execution](model-source-import.md#shared-execution-and-memory-contract).

## Animation and course support boundary

**SKL, MTA, MAL, COLI scene loading, procedural course generation, camera tracks,
lip animation and runtime texture/material animation are not implemented.** The
public descriptions examined still mark important skeletal fields, scene layouts
and interpolation meanings as uncertain. This importer exposes an empty animation
collection; it does not manufacture clips or silently treat a scene as one model.
A representative source corpus and verified layouts are needed to extend those
profiles accurately. GameCube GX and AX variants are not assumed interchangeable.

## Verification and references

Original generated fixtures exercise GMA/GCMF selection, matrix slots, normals,
primitive winding, extra lists, indexed snapshots, all eight GX texture encodings,
mips, small textures, CMPR stride behavior, alpha/additive/custom blending, draw
ordering, compression dictionary references, nested U8 paths, malformed inputs,
allocation budgets, compression-header collisions and transactional cleanup. Managed tests use the public async
API through AOT, background scheduling, forced GC, rendering and ARAM restoration.

Tests and cross-builds do not establish retail-asset visual fidelity or physical
hardware loading/frame times. Those require legally supplied representative assets
and emulator/hardware inspection.

References used to verify binary layouts:

- [F-Zero GX documentation: GMA/GCMF](https://gitlab.com/RaphaelTetreault/fzgx_documentation/-/blob/master/asset/gma.md)
- [GX vertex formats](https://gitlab.com/RaphaelTetreault/fzgx_documentation/-/blob/master/asset/gamecube-gx-vertex-attribute-table.md)
- [Known research gaps](https://gitlab.com/RaphaelTetreault/fzgx_documentation/-/blob/master/asset/unknowns-list.md)
- [Related AV GMA reader](https://github.com/magcius/noclip.website/blob/main/src/SuperMonkeyBall/Gma.ts)
- [Related AV TPL reader](https://github.com/magcius/noclip.website/blob/main/src/SuperMonkeyBall/AVTpl.ts)
- [GxUtils format descriptions and compatibility behavior](https://github.com/bobjrsenior/GxUtils)

The adapter is independently implemented; no external reader implementation is
vendored or linked for F-Zero formats. Shared texture decoding is the existing
independent GX implementation. Third-party source-parser licenses remain listed in
[model-import-dependencies.md](model-import-dependencies.md).
