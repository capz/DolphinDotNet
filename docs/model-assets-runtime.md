# Model assets and animation

The desktop `DolphinDotNet.Assets` .NET tool compiles FBX, OBJ/MTL, DAE, 3DS, STL, PLY, glTF, GLB and PSO GJ/XJ model geometry to self-contained `.dndmodel` files. The GameCube loads compiled packages or directly imports bounded original PSO GJ/GVM/NMDM, FBX, OBJ/MTL, embedded GLB and F-Zero GX GMA/GCMF/TPL assets through `Model.LoadPsoAsync`, `Model.LoadFbxAsync`, `Model.LoadObjAsync`, `Model.LoadGlbAsync` and `Model.LoadFZeroGxAsync`. Sunshine J3D BMD/BDL/BTI/BCK/BCA and GameCube RE4 BIN/TPL/FK-FCV have on-device entry points `Model.LoadJ3dAsync` and `Model.LoadResidentEvil4Async`; see [format profiles](gamecube-model-formats.md). See the [on-device source loading guide](model-source-import.md) for exact supported profiles and memory limits. An already compiled package may also be loaded from bytes with `Model.FromPackage`. See [live loading and GameCube format matrix](live-model-formats.md). Model, animation and transform APIs live in `Dolphin.Graphics` in the GameCube assembly.

## Conversion

```sh
dotnet pack tools/DolphinDotNet.Assets -c Release -o artifacts/tools
dotnet tool install DolphinDotNet.Assets --add-source artifacts/tools --tool-path artifacts/bin
artifacts/bin/dnd-assets model player.glb player.dndmodel --texture-limit 256
```

PSO GJ/XJ geometry conversion is implemented directly in .NET and requires no Blender or external importer. Textures and NJM animations are not yet decoded: textured PSO files require explicit `--allow-material-approximation` and become flat-color geometry.\n\nGLB/glTF import is managed and preserves STEP, LINEAR and CUBICSPLINE tracks. Desktop conversion accepts glTF triangle strips/fans, sparse accessors (including accessors without a base buffer view), UV1 selection and baked base-color KHR_texture_transform UV coordinates. FBX, OBJ, DAE, 3DS, STL and PLY require the Assimp 5.3 CLI (`--assimp /path/to/assimp`); Ubuntu 24.04's 5.3.1 package is exercised in CI. Native importer dependencies stay off the console. The adapter checks source bone influences before export so weights are not silently truncated to four, and checks clip retention. It preserves imported clip durations. FBX interpolation, pivots, constraints, axes and units depend on Assimp's evaluation; complex authoring scenes should be baked and compared with the authoring application. This is not an Autodesk SDK implementation or a guarantee of reproducing every FBX feature.

The supported shading subset is unlit or simple diffuse lighting, vertex colors, opaque/alpha-tested/alpha-blended base-color textures and two-sided surfaces. PBR/normal/occlusion/emissive features require explicit `--allow-material-approximation` or authoring-tool baking; the opt-in discards unsupported surface contributions. Blended materials use native GX source-alpha blending with per-model submesh depth sorting; global cross-model or per-triangle sorting is not automatic. Morph targets, extra UV sets/influence sets, compressed accessors and required unsupported extensions fail conversion. Sparse accessors are expanded during desktop conversion. OBJ contains no skeletal animation. Export models in a consistent right-handed Y-up coordinate system and meter convention; OBJ has no unit metadata, and FBX unit conversion must be verified against the source.

Textures are decoded offline, resized to bounded power-of-two dimensions, mipmapped down to 4 pixels on the shorter dimension, and encoded as GX-tiled RGBA8. Wrapping and nearest/linear magnification are preserved; minification uses the generated mip chain. Identical texture records and material records are deduplicated in the package. A native immutable texture cache additionally shares identical pixel payloads across loaded models.

Each conversion writes a `.deps.json` manifest with SHA-256 hashes of source files, buffers, MTLs and external textures. The included MSBuild target runs conversion before builds (including dependency changes):

```xml
<Import Project="path/to/DolphinDotNet.Assets.targets" />
<ItemGroup>
  <DndModel Include="Assets/player.glb">
    <DndOutput>$(OutputPath)assets/player.dndmodel</DndOutput>
    <Options>--texture-limit 256</Options>
  </DndModel>
</ItemGroup>
```

Override `DndAssetsCommand` if the tool is not on PATH. Conversion currently runs on every build; the manifest is available for external content-hash caching. Output is published only after conversion succeeds. Packages are deterministic for the same source/options/importer.

## Load, animate and draw

Mount the desired device before loading. Async loads run on the runtime background executor; use `await` without an application pump. Source parsing releases the managed execution gate; storage and final publication remain serialized. See [async source loading](model-source-import.md#shared-execution-and-memory-contract) for cancellation and scheduling details. Large package, texture, pose and skinned-vertex allocations are native, outside the 256 KiB managed heap.

```csharp
using Dolphin.Graphics;
using Dolphin.Memory;
using Dolphin.Storage;

Storage.Mount(StorageDevice.SdSerialPort2);
Aram.Initialize(); // before audio or another ARAM owner
using var model = await Model.LoadAsync("sd:/assets/player.dndmodel");
foreach (var clip in model.Animations)
    Console.WriteLine(clip.Name);

using var player = new GameObject(model.CreateInstance());
player.Transform.Position = new Vector3(0, 0, 0);
player.Transform.LocalScale = Vector3.One;
if (model.TryGetAnimation("Walk", out var walk))
    player.Model!.Animator.Play(walk.Name, loop: true);

var camera = new Camera(fieldOfViewDegrees: 60, aspectRatio: 4f / 3f);
camera.LookAt(new Vector3(0, 2, 5), Vector3.Zero, new Vector3(0, 1, 0));
var renderer = new Renderer();
// Each application frame:
player.Transform.Rotate(0, 30 * deltaSeconds, 0);
player.Update(deltaSeconds);
renderer.BeginFrame(camera);
renderer.Draw(player);
renderer.EndFrame();
```

`Model.Animations` is read-only metadata (`Name`, `DurationSeconds`), including while the model is cold. Names are case-sensitive and unknown names fail clearly. Static models expose an empty collection. Converter-generated names for unnamed/duplicate clips are reported. `ModelInstance` owns playback state independently of the shared `Model`. `GameObject` owns its optional supplied instance and disposes it; the shared model remains separately owned. `new GameObject()` creates an empty transform holder, useful as a parent. Its nullable `Model` property is null, `Update` is a no-op and `Renderer.Draw` submits no geometry. Disposed objects reject update/draw; repeated disposal is safe. Disposal rejected during an active frame leaves the object usable so it can be retried after `EndFrame`.

Playback supports `Play`, `CrossFade`, `Pause`, `Resume`, `Stop`, `Seek` and `SetSpeed`. Speeds and time deltas must be nonnegative and finite. Crossfades blend two clips; starting a third while a fade is active fails. Missing channels use the rest pose; quaternion LINEAR tracks use shortest-path slerp, cubic tracks use the glTF Hermite/tangent rule followed by quaternion normalization. Root motion stays in the pose. `GetNodeName` and `ModelInstance.GetNodeTransform(index, float[16])` support bone/socket attachments; matrices are column-major, in model space. Multiply by the object's world matrix to obtain world coordinates. Stop restores the rest pose.

CPU skinning supports four influences per vertex, up to 256 joints per skin and independently animated node ancestors. It computes skinned positions and inverse-transpose weighted normals into reusable aligned native buffers. Active pose evaluation and geometry stay in main RAM. Rendering uses native GX immediate triangle submission, RGB/alpha modulation, a fixed camera-facing diffuse direction plus ambient light, and restores the GX state required by the existing diagnostic overlay at frame end. F-Zero imports also support alpha/additive/custom GX blending with per-model mesh depth sorting; see [F-Zero loading](fzero-gx-loading.md). Hardware rendering/performance acceptance remains necessary; CI builds do not prove visual correctness. Use the shared GraphicsDevice and explicit pass boundaries when mixing model, mesh, raw GX and Draw2D submissions; see [Graphics APIs](graphics.md).

## Transforms and lifetime

`Transform.LocalPosition`, `LocalRotation` (quaternion), `LocalScale`, world `Position`, `Rotate(x,y,z)` (local XYZ Euler degrees), `Translate(offset, worldSpace: false)` and `Parent` supply a small scene hierarchy. Reparenting preserves local coordinates. Parent cycles and hierarchies exceeding 128 levels are rejected. `CopyWorldMatrix` copies to a caller-owned 16-float buffer. Clean transforms return a cached matrix in constant time; mutations dirty affected descendants, and matrix calculation occurs on next use. Reassigning identical local position, rotation or scale skips invalidation. No per-draw transform allocations occur. This provides object placement and parenting, without Unity's component, physics or scene lifecycle systems. Rendering and skinning cost depends on vertex count and submitted passes; FPS claims require hardware measurements.

The lower-level `Renderer.BeginFrame(float[16] view, float[16] projection)` and `DrawModel(instance, float[16] world)` remain available. Raw projection matrices must follow GX's right-handed depth convention (near -1, far 0); the Camera helper builds this convention. `DrawModel(instance, Transform)` also accepts cached transforms.

Update, playback mutation, unloading and instance disposal are rejected while a model frame is active. `EndFrame` waits for GX completion before buffers can be reused/freed. Dispose instances before disposing the shared model; existing instances retain native resources if the model wrapper is disposed early, but metadata lookup and new instance creation then fail. Rendering requires the application thread. Shared managed/model operations use the runtime execution gate; native source parsing runs outside it on the service thread. Native handles use monotonic tokens, never recycled identities.

## ARAM unload and reload

```csharp
model.UnloadToAramAsync().Wait(); // copy complete package, then release main-RAM payloads
// Keep model, player instance, animation metadata and current playback state.
model.PrepareAsync().Wait();      // restore from ARAM; no SD/disc reread
renderer.BeginFrame(camera);
renderer.Draw(player);
renderer.EndFrame();
```

`UnloadToAramAsync` preserves the old resident resources until the whole ARAM upload succeeds; cancellation/failure leaves the original residency intact. `PrepareAsync` validates the ARAM snapshot and rebuilds texture/skinning buffers transactionally; failure leaves the model cold. Existing instances resume at their preserved times. `PrepareAnimationAsync(name)` validates the clip and prepares its whole model. Selective clip-only eviction, compression and automatic LOD streaming are future optimizations.

Drawing/updating a cold model fails rather than hiding DMA. Await `PrepareAsync` ahead of need while the application continues its frame loop. Operations on an instance fail during its model's residency transition. Model disposal during a transition drains active DMA and cleans up without publishing the result. Transitions are serialized per model; DMA is globally serialized by the existing ARAM queue.

Backing packages remain in ARAM after restoration, so later unloads reuse the snapshot without another upload. These allocations are pinned until model disposal, not subject to priority asset-cache eviction. `IsResident`, `NativeBytes` and `AramBytes` report residency and per-model memory charges (shared texture payloads are charged to each model's budget). `ModelInstance.NativeBytes` reports its pose, palette and output-buffer charge. Small model metadata, node transforms and instance playback/pose state remain in main RAM while cold; textures, package bytes, skin palettes and skinned outputs are freed. Keeping an ARAM backup consumes both memories while resident, but avoids disk reads after eviction.

Default native budgets are 8 MiB per model and 4 MiB per instance; each can be configured up to 16 MiB. Packages are limited to 16 MiB; model/instance slots are 64/128. These are upper bounds, not guaranteed available memory. Reserve sufficient main RAM for the runtime, graphics FIFO/framebuffers, networking/audio and other assets. Texture-cache payloads are globally capped at 16 MiB. ARAM capacity still follows the platform/audio reservation in `aram-runtime.md`.

## Validation

CI exercises deterministic conversion, embedded/external textures, GLB and glTF, OBJ triangulation/materials, animated FBX, GX RGBA8 tiling, malformed sources/packages, interpolation, weighted positions/normals, ancestor animation, looping/fades, independent instances, ownership/budgets, cancellation and ARAM restore after the source file has been deleted. Native and managed AOT tests run under sanitizers and on 32-bit hosts with GC stress; the PowerPC ABI and full DOL build are compiled separately. Test fixtures are generated, including an explicitly corrected FBX timestamp fixture for an Assimp 5.3 exporter quirk. Real-console DMA/audio coexistence and visual/frame-budget acceptance must be measured on hardware.

Pass a `GxEffect` as the third argument to either `DrawModel` overload, or the
second argument to `Draw(GameObject, effectOverride)`, to override RGB per draw.
The effect is caller-owned and independent of model/ARAM residency. See
[imported-model effects](effects.md#imported-models-per-draw-overrides) for alpha,
texture compatibility and lifecycle rules.

## Compatibility implementation phases

Phase 1 extends the desktop importer with triangle strips/fans, sparse accessor expansion, base-color UV1 and KHR_texture_transform baking, and additional Assimp-backed format adapters (DAE, 3DS, STL and PLY). These formats still need representative real-world fixture qualification; Assimp accepting an extension does not guarantee feature-complete scene conversion. All native on-console source loaders and current DNMD schema remain unchanged. Phase 2 adds GLB/FBX/OBJ source and offline glTF BLEND/opacity retention through the existing GX per-model sorted alpha path. In explicit `--allow-material-approximation` mode, compatible base/emissive/occlusion maps with matching UV sets, transforms and sampler settings are baked into a single GX RGBA8 texture; base-color tint/alpha is included in the baked pixels. The existing imported material TEV path multiplies this texture with vertex lighting. Since emissive contributions are included in diffuse, this is an approximation rather than source-faithful PBR or dynamic multistage TEV. Normal maps, metallic/roughness shading and differing UV layouts remain unsupported except for explicitly acknowledged approximation. A new `Renderer.DrawModelsBackToFront(camera, objects, orderScratch)` helper sorts separate model instances by camera-space object origin without per-frame allocation. Depth ordering is not per-triangle or globally automatic; overlapping objects may still require manual draw ordering. Later phases cover morph/complex-skin baking, compressed glTF decoding, native texture formats and hardware visual/performance acceptance. Unsupported contributions must continue to fail explicitly unless approximation is requested.
