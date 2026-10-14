# Model assets and animation

The desktop `DolphinDotNet.Assets` .NET tool compiles FBX, OBJ/MTL, glTF and GLB to self-contained `.dndmodel` files. The GameCube loads the compiled package only. Model, animation and transform APIs live in `Dolphin.Graphics` in the GameCube assembly.

## Conversion

```sh
dotnet pack tools/DolphinDotNet.Assets -c Release -o artifacts/tools
dotnet tool install DolphinDotNet.Assets --add-source artifacts/tools --tool-path artifacts/bin
artifacts/bin/dnd-assets model player.glb player.dndmodel --texture-limit 256
```

GLB/glTF import is managed and preserves STEP, LINEAR and CUBICSPLINE tracks. FBX and OBJ require the Assimp 5.3 CLI (`--assimp /path/to/assimp`); Ubuntu 24.04's 5.3.1 package is exercised in CI. Native importer dependencies stay off the console. The adapter checks source bone influences before export so weights are not silently truncated to four, and checks clip retention. It preserves imported clip durations. FBX interpolation, pivots, constraints, axes and units depend on Assimp's evaluation; complex authoring scenes should be baked and compared with the authoring application. This is not an Autodesk SDK implementation or a guarantee of reproducing every FBX feature.

The supported shading subset is unlit or simple diffuse lighting, vertex colors, opaque/alpha-tested base-color textures and two-sided surfaces. PBR/normal/occlusion/emissive features require explicit `--allow-material-approximation` or authoring-tool baking; the opt-in discards unsupported surface contributions. Blended transparency, morph targets, extra UV sets/influence sets, sparse/compressed accessors and required unsupported extensions fail conversion. OBJ contains no skeletal animation. Export models in a consistent right-handed Y-up coordinate system and meter convention; OBJ has no unit metadata, and FBX unit conversion must be verified against the source.

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

Mount the desired device before loading. Async operations use Dolphin's cooperative task scheduler; pump it on the application thread. Large package, texture, pose and skinned-vertex allocations are native, outside the 256 KiB managed heap.

```csharp
using Dolphin.Graphics;
using Dolphin.Memory;
using Dolphin.Storage;
using Scheduler = Dolphin.Threading.Tasks.Scheduler;

Storage.Mount(StorageDevice.SdSerialPort2);
Aram.Initialize(); // before audio or another ARAM owner
var loading = Model.LoadAsync("sd:/assets/player.dndmodel");
// Pump Scheduler.Pump() each iteration while loading.
using var model = loading.Result; // Result also pumps while waiting
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

CPU skinning supports four influences per vertex, up to 256 joints per skin and independently animated node ancestors. It computes skinned positions and inverse-transpose weighted normals into reusable aligned native buffers. Active pose evaluation and geometry stay in main RAM. Rendering uses native GX immediate triangle submission, RGB/alpha modulation, a fixed camera-facing diffuse direction plus ambient light, and restores the GX state required by the existing diagnostic overlay at frame end. Hardware rendering/performance acceptance remains necessary; CI builds do not prove visual correctness. Use the shared GraphicsDevice and explicit pass boundaries when mixing model, mesh, raw GX and Draw2D submissions; see [Graphics APIs](graphics.md).

## Transforms and lifetime

`Transform.LocalPosition`, `LocalRotation` (quaternion), `LocalScale`, world `Position`, `Rotate(x,y,z)` (local XYZ Euler degrees), `Translate(offset, worldSpace: false)` and `Parent` supply a small scene hierarchy. Reparenting preserves local coordinates. Parent cycles and hierarchies exceeding 128 levels are rejected. `CopyWorldMatrix` copies to a caller-owned 16-float buffer. Clean transforms return a cached matrix in constant time; mutations dirty affected descendants, and matrix calculation occurs on next use. Reassigning identical local position, rotation or scale skips invalidation. No per-draw transform allocations occur. This provides object placement and parenting, without Unity's component, physics or scene lifecycle systems. Rendering and skinning cost depends on vertex count and submitted passes; FPS claims require hardware measurements.

The lower-level `Renderer.BeginFrame(float[16] view, float[16] projection)` and `DrawModel(instance, float[16] world)` remain available. Raw projection matrices must follow GX's right-handed depth convention (near -1, far 0); the Camera helper builds this convention. `DrawModel(instance, Transform)` also accepts cached transforms.

Update, playback mutation, unloading and instance disposal are rejected while a model frame is active. `EndFrame` waits for GX completion before buffers can be reused/freed. Dispose instances before disposing the shared model; existing instances retain native resources if the model wrapper is disposed early, but metadata lookup and new instance creation then fail. Operations are single-threaded/application-thread only. Native handles use monotonic tokens, never recycled identities.

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

Drawing/updating a cold model fails rather than hiding DMA. Call `PrepareAsync` ahead of need and pump the scheduler over intervening frames. Operations on an instance fail during its model's residency transition. Model disposal during a transition drains active DMA and cleans up without publishing the result. Transitions are serialized per model; DMA is globally serialized by the existing ARAM queue.

Backing packages remain in ARAM after restoration, so later unloads reuse the snapshot without another upload. These allocations are pinned until model disposal, not subject to priority asset-cache eviction. `IsResident`, `NativeBytes` and `AramBytes` report residency and per-model memory charges (shared texture payloads are charged to each model's budget). `ModelInstance.NativeBytes` reports its pose, palette and output-buffer charge. Small model metadata, node transforms and instance playback/pose state remain in main RAM while cold; textures, package bytes, skin palettes and skinned outputs are freed. Keeping an ARAM backup consumes both memories while resident, but avoids disk reads after eviction.

Default native budgets are 8 MiB per model and 4 MiB per instance; each can be configured up to 16 MiB. Packages are limited to 16 MiB; model/instance slots are 64/128. These are upper bounds, not guaranteed available memory. Reserve sufficient main RAM for the runtime, graphics FIFO/framebuffers, networking/audio and other assets. Texture-cache payloads are globally capped at 16 MiB. ARAM capacity still follows the platform/audio reservation in `aram-runtime.md`.

## Validation

CI exercises deterministic conversion, embedded/external textures, GLB and glTF, OBJ triangulation/materials, animated FBX, GX RGBA8 tiling, malformed sources/packages, interpolation, weighted positions/normals, ancestor animation, looping/fades, independent instances, ownership/budgets, cancellation and ARAM restore after the source file has been deleted. Native and managed AOT tests run under sanitizers and on 32-bit hosts with GC stress; the PowerPC ABI and full DOL build are compiled separately. Test fixtures are generated, including an explicitly corrected FBX timestamp fixture for an Assimp 5.3 exporter quirk. Real-console DMA/audio coexistence and visual/frame-budget acceptance must be measured on hardware.
