# Model assets: proposed next phase

This document is a design, not an implemented loader or renderer API. ARAM storage/cache is implemented separately in `aram-runtime.md`.

## Developer workflow

A desktop converter imports FBX, OBJ plus MTL/texture files, or glTF/GLB and emits one self-contained `.dndmodel` package. A build target can run conversion automatically when model dependencies change. GameCube applications load only the compiled package, rather than parsing authoring formats on the console.

```sh
dnd-assets model player.glb player.dndmodel --texture-limit 256
```

Proposed runtime usage:

```csharp
using var model = await Model.LoadAsync("sd:/assets/player.dndmodel");

renderer.BeginFrame(camera);
renderer.DrawModel(model, transform);
renderer.EndFrame();
```

The model includes node transforms, multiple meshes, material assignments and textures. Drawing the model traverses its static node hierarchy and submits each submesh with its material. Applications can optionally override a material; rendering does not require reconstructing materials or manually binding textures.

## Import and build layer

Use a pinned desktop importer with fixtures for each supported source format. Assimp is a candidate because its official supported-format list includes FBX, OBJ and glTF/GLB: https://github.com/assimp/assimp/blob/master/doc/Fileformats.md. Qualify its exact version/platform packages and texture handling before adopting a .NET binding. Keep importer/native dependencies out of the console runtime.

The tool resolves external/embedded textures, triangulates geometry, applies consistent units/axes/winding/UV convention, preserves static node transforms, builds normals when absent, splits meshes to fit the chosen index range, and emits bounds and material/submesh tables. Report missing textures and unsupported features with the source mesh/material name. Default conversion should fail rather than quietly drop a required feature; explicitly selected approximation modes can bake supported surface data.

For the first milestone, support static triangle meshes, positions/normals/UV0/optional vertex color, opaque or alpha-tested base-color textured materials, simple fixed-function lighting and unlit materials. Materials map into a defined GX-compatible subset; arbitrary FBX shaders and glTF PBR do not automatically look identical. Texture generation includes size limits, mipmaps and explicit alpha policy. Deduplicate identical textures/materials within a package. Animation, skeletal skinning, morph targets, advanced PBR and order-dependent transparency require later stages and explicit importer diagnostics in the meantime.

## Compiled package

A versioned, bounds-checked, big-endian chunk container:

| Chunk | Contents |
| --- | --- |
| Header/index | Magic/version, total length, chunk offsets/lengths, checksums and required feature flags |
| Nodes | Parent indices, local transforms and mesh references |
| Meshes | Bounds, vertex format, vertex/index payload references and submesh ranges |
| Materials | Supported shading mode, base color, texture references, wrapping/filtering and alpha/cull policy |
| Textures | Dimensions, target format, mip level offsets and GX-tiled payloads |
| Optional cold payloads | Independently compressed/chunked source data for streaming or reload |

All payload ranges are explicitly sized and 32-byte aligned where DMA/GX requires it. Reject overlaps/out-of-range references, unsupported versions/features, inconsistent vertex/index sizes and memory-budget excess before publishing the model. Encode target vertex/scalar/index endianness offline. Initial texture formats would be GX RGB5A3 and RGBA8, with exact tiling fixtures; compressed formats can follow. The package does not need a live FBX/glTF parser or original PNG/JPEG decoder on the console.

## Runtime resources and rendering

Separate `Model` (metadata/resource ownership), `Mesh` (vertex/index resource), `Material`, `Texture` and `Renderer`. Native resource handles own aligned main-RAM payloads and enforce generation/lifetime checks. Managed wrappers stay small; stream package chunks rather than allocating a whole model byte array inside the current 256 KiB managed heap. Loading is transactional: validate, reserve memory, transfer/decode chunks, initialize GPU resources, then publish. Cancellation/failure releases every partial resource.

For the general renderer, use explicit native GX submission for target-ready vertex/texture data. The current OpenGX demo is not a general model renderer and cannot be substituted for this layer. Define frame/state boundaries before combining GX model draws and diagnostic overlays; material binding must fully restore its required state. A renderer-owned resource cache deduplicates shared textures and tracks references. Release resources only after submitted rendering has finished using them.

## ARAM residency

ARAM stores compressed/cold payloads, not GPU texture pointers or managed objects. A model can have metadata in main RAM, cold payloads in ARAM, and only active rendering resources in main RAM. The initial `LoadAsync` eagerly makes required resources resident so `DrawModel` never unexpectedly stalls for DMA. Later `PrepareAsync`/prefetch operations make upcoming LODs or model groups resident ahead of drawing. Eviction applies only to recoverable payloads/resources, with explicit budgets for main RAM, ARAM and transfer work.

## Implementation order

1. Add the bounded package format and desktop converter, beginning with static models and texture/material fixtures for OBJ, GLB and FBX.
2. Add native resource ownership, texture/mesh upload, frame/camera transforms and supported GX material binding; render a packaged model as one call.
3. Add cooperative streaming/cancellation, cleanup tests and hardware rendering acceptance scenes.
4. Integrate ARAM residency/prefetch and memory/transfer statistics; add LODs and animation as separately validated feature sets.

Acceptance scenes should cover multi-material meshes, embedded/external textures, hierarchy transforms, alpha-test edges, missing/corrupt assets and load/dispose loops. Compare converter output against independent decoding and target screenshots; CI compilation alone does not establish visual correctness.
