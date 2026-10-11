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

The model includes node transforms, multiple meshes, material assignments and textures. Drawing the model traverses its node hierarchy and submits each submesh with its material. Applications can optionally override a material; rendering does not require reconstructing materials or manually binding textures.

## Import and build layer

Use a pinned desktop importer with fixtures for each supported source format. Assimp is a candidate because its official supported-format list includes FBX, OBJ and glTF/GLB: https://github.com/assimp/assimp/blob/master/doc/Fileformats.md. Qualify its exact version/platform packages and texture handling before adopting a .NET binding. Keep importer/native dependencies out of the console runtime.

The tool resolves external/embedded textures, triangulates geometry, applies consistent units/axes/winding/UV convention, preserves static node transforms, builds normals when absent, splits meshes to fit the chosen index range, and emits bounds and material/submesh tables. Report missing textures and unsupported features with the source mesh/material name. Default conversion should fail rather than quietly drop a required feature; explicitly selected approximation modes can bake supported surface data.

For the first milestone, support static triangle meshes, positions/normals/UV0/optional vertex color, opaque or alpha-tested base-color textured materials, simple fixed-function lighting and unlit materials. Materials map into a defined GX-compatible subset; arbitrary FBX shaders and glTF PBR do not automatically look identical. Texture generation includes size limits, mipmaps and explicit alpha policy. Deduplicate identical textures/materials within a package. Skeletal skinning and animation clips are part of the planned supported model feature set for FBX and glTF/GLB sources that contain them. OBJ supplies static geometry only. Morph targets, advanced PBR and order-dependent transparency remain later features with explicit importer diagnostics.

## Compiled package

A versioned, bounds-checked, big-endian chunk container:

| Chunk | Contents |
| --- | --- |
| Header/index | Magic/version, total length, chunk offsets/lengths, checksums and required feature flags |
| Nodes | Parent indices, local transforms and mesh references |
| Meshes | Bounds, vertex format, vertex/index payload references and submesh ranges |
| Skeletons/skins | Joint-to-node mappings, parent hierarchy, inverse bind matrices and mesh-to-skin references |
| Skin weights | Per-vertex joint indices and normalized influence weights |
| Animation clips | Names, durations, target node/property, interpolation mode, key times and values/tangents |
| Materials | Supported shading mode, base color, texture references, wrapping/filtering and alpha/cull policy |
| Textures | Dimensions, target format, mip level offsets and GX-tiled payloads |
| Optional cold payloads | Independently compressed/chunked source data for streaming or reload |

All payload ranges are explicitly sized and 32-byte aligned where DMA/GX requires it. Reject overlaps/out-of-range references, unsupported versions/features, inconsistent vertex/index sizes and memory-budget excess before publishing the model. Encode target vertex/scalar/index endianness offline. Initial texture formats would be GX RGB5A3 and RGBA8, with exact tiling fixtures; compressed formats can follow. The package does not need a live FBX/glTF parser or original PNG/JPEG decoder on the console.

## Runtime resources and rendering

Separate `Model` (metadata/resource ownership), `Mesh` (vertex/index resource), `Material`, `Texture` and `Renderer`. Native resource handles own aligned main-RAM payloads and enforce generation/lifetime checks. Managed wrappers stay small; stream package chunks rather than allocating a whole model byte array inside the current 256 KiB managed heap. Loading is transactional: validate, reserve memory, transfer/decode chunks, initialize GPU resources, then publish. Cancellation/failure releases every partial resource.

For the general renderer, use explicit native GX submission for target-ready vertex/texture data. The current OpenGX demo is not a general model renderer and cannot be substituted for this layer. Define frame/state boundaries before combining GX model draws and diagnostic overlays; material binding must fully restore its required state. A renderer-owned resource cache deduplicates shared textures and tracks references. Release resources only after submitted rendering has finished using them.

## Skeletons and animation

Preserve animated nodes as well as bones: clips can animate translation, quaternion rotation and scale on ordinary nodes or skeleton joints. Preserve the full ancestor hierarchy needed by each skin, bind poses, inverse bind matrices and separate skins sharing a node hierarchy. Apply coordinate/unit conversion consistently to vertices, node transforms, bind matrices and animation tracks. Do not flatten animated transforms into mesh data.

The converter supports named clips, skin weights and skeletal/node animation from FBX and glTF/GLB. Qualify importer behavior using independently verified fixtures; format support alone does not establish correct animation import. Preserve glTF STEP, LINEAR and CUBICSPLINE semantics, including quaternion normalization and cubic tangents. Bake FBX constraints, pivots and interpolation into sampled local transforms at a configurable rate with a reported error tolerance. Reject unsupported deformation rather than silently discarding it. Set a target budget of four influences per vertex; exceeding it fails unless the developer explicitly enables weight reduction with an error report. Split skinned meshes by a bounded joint palette and report package/runtime memory costs.

Separate immutable, shared `Model` resources from mutable `ModelInstance` pose and playback state. Each instance owns its animation clock, local/global pose and skinning workspace; two characters can share a model while playing different clips. Proposed usage:

```csharp
using var model = await Model.LoadAsync("sd:/assets/player.dndmodel");
// Inspect available clips before creating or playing an instance:
foreach (var clip in model.Animations)
    Console.WriteLine($"{clip.Name}: {clip.DurationSeconds}s");

using var player = model.CreateInstance();
player.Animator.Play("Walk", loop: true);

// Once per simulation update:
player.Update(deltaSeconds);

// During rendering:
renderer.DrawModel(player, transform);

// Optional transition:
player.Animator.CrossFade("Idle", durationSeconds: 0.2f);
```

`Model.Animations` is a read-only collection of `AnimationClipInfo` metadata with `Name` and `DurationSeconds`, available immediately after loading without sampling or prefetching clip payloads. Static models expose an empty collection. The converter assigns deterministic unique names to unnamed/duplicate source clips and reports the mapping. `model.TryGetAnimation(name, out AnimationClipInfo clip)` uses ordinal, case-sensitive names; `Play` and `CrossFade` use the same lookup and throw a clear argument error for unknown names. Applications can enumerate clips to build selection logic without guessing names. Metadata belongs to the shared model; playback state belongs to each instance.

Playback includes clip enumeration, play/pause/stop, seek, speed, looping and a two-clip crossfade. Evaluate missing channels from the rest pose, interpolate rotations with appropriate quaternion handling, propagate parent transforms, then construct mesh-local skin matrices from joint transforms and inverse bind matrices. Define matrix multiplication conventions in the package specification and test bind-pose reconstruction before implementing rendering. Initially root motion stays in the animated pose; extracting it into gameplay movement must be an explicit later API to avoid applying it twice. Bone/socket attachment queries expose the current instance pose with a documented coordinate space. Layered/additive blending, retargeting and animation events are follow-up work.

Use native CPU skinning into reusable, aligned main-RAM vertex buffers for the first renderer. This handles weighted multi-bone vertices with predictable semantics; do not assume GX fixed-function vertex transforms provide arbitrary weighted skinning. Skin positions and normals with documented nonuniform-scale handling, keep source vertices immutable, and fence or rotate output buffers until prior GPU draws finish. Allocate pose/skinning storage outside the small managed heap and enforce per-instance budgets. Update once per frame and reuse the result for multiple draws. Validate performance on hardware and expose timing/vertex/joint counts; later palette-based optimizations must preserve the reference output.

Active pose, sampled tracks and skinned output remain in main RAM. Unused clips can live in ARAM, with explicit `PrepareAnimationAsync` prefetch before playback; `Play` cannot trigger hidden DMA or silently use a nonresident clip. The initial eager loader makes all clips resident within declared budgets and fails clearly if they do not fit. Cancellation and instance disposal follow the same transactional ownership rules as static resources.

## ARAM residency

ARAM stores compressed/cold payloads, not GPU texture pointers or managed objects. A model can have metadata in main RAM, cold payloads in ARAM, and only active rendering resources in main RAM. The initial `LoadAsync` eagerly makes required resources resident so `DrawModel` never unexpectedly stalls for DMA. Later `PrepareAsync`/prefetch operations make upcoming LODs or model groups resident ahead of drawing. Eviction applies only to recoverable payloads/resources, with explicit budgets for main RAM, ARAM and transfer work.

## Implementation order

1. Add the bounded package format and desktop converter, beginning with static models and texture/material fixtures for OBJ, GLB and FBX.
2. Add native resource ownership, texture/mesh upload, frame/camera transforms and supported GX material binding; render a packaged model as one call.
3. Add skeleton/clip conversion, per-instance playback and crossfades, native CPU skinning and bone attachment queries; validate bind poses and animated FBX/GLB fixtures.
4. Add cooperative streaming/cancellation, cleanup tests and hardware rendering acceptance scenes, including animated instances.
5. Integrate ARAM residency/prefetch for model payloads and unused clips, memory/transfer statistics, and later LOD support.

Acceptance scenes should cover multi-material meshes, embedded/external textures, hierarchy transforms, alpha-test edges, missing/corrupt assets and load/dispose loops. Compare converter output against independent decoding and target screenshots; CI compilation alone does not establish visual correctness.

Animation acceptance covers bind-pose identity, multiple skins, animated non-bone ancestors, STEP/LINEAR/CUBICSPLINE tracks, quaternion continuity, missing channels, clip boundaries, looping/seeking/crossfades, two independently animated instances, weighted normal deformation, corrupt joint/track references and disposal while GPU work is pending. Compare sampled transforms and skinned vertices with an independent reference, then verify animated target screenshots and frame budgets on hardware.
