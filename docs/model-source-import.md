# On-device source model loading

All loaders below parse original source files on GameCube and return the existing
`Model`. Rendering, `ModelInstance`, `Animator`, native texture caching and ARAM
residency use the same DNMD representation. No desktop process, CLR, SkiaSharp,
System.Text.Json or asset conversion service is required at runtime.

```csharp
using var model = await Model.LoadFbxAsync("sd:/assets/character.fbx", cancellationToken);
using var instance = model.CreateInstance();
instance.Animator.Play("Walk", loop: true);
instance.Update(deltaSeconds);
renderer.BeginFrame(camera);
renderer.DrawModel(instance, worldMatrix);
renderer.EndFrame();
```

Mount the source volume first. Other source entry points are:

```csharp
Model.LoadFZeroGxAsync("sd:/assets/vehicle.gma.lz", "sd:/assets/vehicle.tpl.lz");
Model.LoadJ3dAsync("sd:/assets/actor.arc#model.bmd", "sd:/assets/actor.arc#walk.bck");
Model.LoadResidentEvil4Async("sd:/assets/body.bin", "sd:/assets/body.tpl");
Model.LoadObjAsync("sd:/assets/prop.obj");
Model.LoadGlbAsync("sd:/assets/character.glb");
Model.LoadPsoAsync("sd:/assets/player.gj", "sd:/assets/player.gvm",
    "sd:/assets/walk.njm", motionFramesPerSecond: 30, eulerKeyBytes: 16);
```

The PSO frame rate above is an example: use the verified rate of the source motion.
The imported PSO, J3D and RE4 clips are named `Motion`. FBX/GLB retain source clip names.
See [Sunshine and GameCube RE4 format support and limits](gamecube-model-formats.md).

## Shared execution and memory contract

`LoadAsync`, `LoadObjAsync`, `LoadFbxAsync`, `LoadGlbAsync`, `LoadPsoAsync` and
`LoadFZeroGxAsync`, `LoadJ3dAsync` and `LoadResidentEvil4Async`
return `Task<Model>` and queue loading on the runtime service thread. Use ordinary
`await`; no application pump is required. A pending default await captures its
executor, so application code resumes on the application thread for rendering.
`ConfigureAwait(false)` follows the runtime's normal background continuation rules.
`Result` and `Wait()` still block the caller's logical flow.

Source parsing, animation baking, image decoding and package construction release
the managed execution gate. They use native-owned data and native copies of source
paths, so the application can render and allocate/collect while parsing is active.
Source imports are serialized on the one service executor; another source import
queues behind the current one. Other service-thread tasks, timers and network work
also wait until that native parse returns. This is not a thread pool.
On GameCube the service thread inherits the application's timesliced LWP priority
(capped below time-critical priority), allowing both to advance on the single CPU.

Storage-handle operations and final model validation/texture-cache publication
reacquire the execution gate. Prebuilt package loading uses bounded background
steps under the gate. Blocking storage calls and final publication can still delay
an application frame; this API does not guarantee uninterrupted frame delivery.
Loading may overlap active rendering; model/instance lifetime and frame rules still
apply to published models. `FromPackage` remains synchronous.

Cancellation is checked before loading and before publishing the managed result.
It cannot interrupt a native library parse. Cancellation during parsing waits for
native cleanup, discards the resulting native model and completes the task as
canceled. Await outstanding loads before application shutdown; shutdown joins an
active parser before releasing managed roots.

The default native budget is 8 MiB; the maximum is 16 MiB. Parsing, temporary
buffers, decoded images, package construction and publication overlap all count
towards that budget. Inputs and external image/MTL files for PSO/FBX/OBJ/GLB are limited to 2 MiB each.
F-Zero, J3D and RE4 source and expanded files are limited to 16 MiB within the same native budget.
An input below that size can still exceed the memory budget after decoding or
baking. Model/instance budgets, slot limits and ARAM ownership otherwise follow
[model assets and animation](model-assets-runtime.md).

Texture allocations are native and 32-byte aligned. Models use the existing
immutable texture cache and GX completion/lifetime rules. Source images become
GX-tiled RGBA8 base levels; PSO/FBX/OBJ/GLB source import currently generates no mipmaps.
F-Zero TPL imports retain authored mipmaps down to 4x4. This
uses more memory than retaining native CMPR and other compact encodings.

FBX/OBJ sidecars are opened through DolphinDotNet storage handles, with strict
UTF-8 decoding and normal mounted-volume path normalization. References may stay
within the source volume; switching volumes or using network/host URIs is rejected.
External streams are closed on success and failure. ufbx's default filesystem and
malloc backends are disabled; custom allocators charge the model budget.

## F-Zero GX GMA/GCMF, TPL, LZ and ARC

`LoadFZeroGxAsync` supports verified rigid GMA/GCMF geometry, AV TPL textures,
LZSS compression, U8/ARC member selection, native translucency and explicitly
requested static snapshots of indexed skin/effect meshes. Full pilot and course
animation formats remain unsupported. See [F-Zero GX loading](fzero-gx-loading.md)
for the API, exact profile, material approximation and test coverage.

## PSO GameCube GJ/GVM/NMDM

Supported geometry includes a validated GJCM node hierarchy, GJTL texture-name
mapping, independent 8-/16-bit attribute indices, colors/UVs, triangle lists
(0x90), strips (0x98) and fans (0xA0). Node evaluation flags and XYZ/ZYX order are
retained. Geometry pointers are checked byte offsets, never executable GPU lists.

GVM archives and bare GVR textures support I4, I8, IA4, IA8, RGB565, RGB5A3, RGBA8
and CMPR. GX alpha/intensity layout, CMPR index order and GX-specific color
interpolation are decoded explicitly. GJTL names match named archives
case-insensitively; otherwise texture indices are matched directly. Missing or
ambiguous texture mappings fail. Native PSO texture dimensions must be power-of-two
from 4 through 1024. Alpha strip groups and unknown material properties are rejected.

One optional NMDM motion can supply linear translation, scale, quaternion and Euler
BAMS channels. Track slots bind to source DFS traversal with `NoAnimate` nodes
omitted; mismatched table sizes fail. Compact Euler keys require `eulerKeyBytes: 8`;
wide keys use 16. The frame rate must be explicit when a motion is supplied.
Euler channels are sampled at source-frame intervals and additional angular steps
of at most pi/32 radians per component, then stored as quaternion tracks. This is
bounded linear-motion support, not a Ninja spline evaluator.

Weighted Ninja NJ chunks, BML/PRS/REL containers, PVM/XVM textures, indexed palettes,
Ninja spline/custom interpolation and texture/material animation remain unsupported.
The presence of a PSO filename does not prove its structural variant is GJ rigid-node
geometry. Supply representative assets before expanding those profiles.

## FBX

The pinned native ufbx reader handles binary and ASCII FBX directly on the console.
Axes/units normalize to right-handed Y-up meters. Geometry transforms and special
inheritance use helper nodes. Polygon-corner attributes, material partitions,
bind matrices and up to four positive influences per vertex are retained. Only
linear skinning with up to 256 joints per skin is supported; weights are normalized
without truncation. Primitives are triangulated into at most 65,535 expanded
vertices per material partition.

Each animation stack is evaluated/baked by ufbx into translation, quaternion and
scale keys. `animationSampleRate` defaults to 60 and accepts 1..240 samples/second.
Playback duration is retained. This preserves evaluated transforms rather than
assuming FBX curves or pivots are glTF curves. Morphs, caches, constraints,
multiple deformers and extra UV sets are rejected. Unsupported animated material
properties are rejected.

Supported model images are embedded or external PNG, JPEG, TGA and BMP. Dimensions
must be at most 1024 on each axis. Images are nearest-resampled to power-of-two
sizes from 4 through 1024. Base color, vertex color, simple diffuse/unlit shading,
wrapping and two-sided flags are retained. Textured surfaces use alpha testing at
0.5; this is not sorted blended transparency. Procedural/layered textures and UV
transforms require baking and are rejected. Other surface maps are rejected by
default; `allowMaterialApproximation: true` explicitly permits dropping unsupported
surface contributions. This option never permits unsupported deformation.

## OBJ and MTL

`LoadObjAsync` now uses the same native reader for OBJ polygon triangulation,
separate/negative position/UV/normal indices, object/group hierarchy, smoothing
normals, MTL assignments and external base-color images. Concave polygons are
triangulated rather than treated as triangle fans. Source units are treated as
meters; OBJ does not contain reliable unit metadata.

MTL diffuse color and base-color texture maps are retained. Unsupported opacity,
bump, normal, specular and reflection maps require explicit approximation or fail.
The FBX image and surface restrictions above apply. OBJ and FBX UVs use the
source's bottom-left convention and are flipped vertically for top-left decoded
images. OBJ has no standard skeleton or skeletal animation and exposes no clips.

## Embedded GLB v2

The native GLB profile requires JSON followed by one embedded BIN chunk and no
external buffers/images. PNG and JPEG images are supported, with power-of-two
sizes from 4 through 1024. The selected scene is honored. Core triangle geometry,
one UV/color/influence set, up to 65,536 vertices per primitive, linear skinning and
STEP/LINEAR/CUBICSPLINE node TRS animation are supported. Missing normals are generated.

Binary accessors and inverse bind matrices are read explicitly as little-endian,
including on PowerPC. Matrix-form nodes, sparse/compressed accessors, morphs and
required unsupported extensions are rejected. Opaque/masked base-color shading is
supported. `allowMaterialApproximation` permits dropping unsupported shading
contributions; blended alpha is rejected.

## Validation

Generated original fixtures cover textured GJ and NMDM translation/rotation, all
supported GX texel formats, primitive winding, skinned/animated FBX, textured
FBX, concave OBJ/MTL, PNG/JPEG/TGA/BMP, embedded GLB skins and all three clip
interpolation modes. Native tests also check malformed inputs, budgets, cleanup,
instance lifetime and playback. Managed smoke tests call the actual source APIs
through AOT and render through the existing model backend. The background-load
smoke test pauses the real native parser and requires application-thread rendering,
allocations/GC and in-flight cancellation to progress before releasing it. It also
checks captured awaits, queued imports, failures and native handle cleanup.

Host sanitizers, GC stress, emulated 32-bit and big-endian PowerPC execution, and
GameCube DOL builds are separate checks. A successful cross-build does not prove
retail-asset visual correctness or hardware loading/frame costs. Those require a
representative source corpus, Dolphin inspection and physical hardware measurements.

Exact third-party pins and retained licenses are in
[model-import-dependencies.md](model-import-dependencies.md).
