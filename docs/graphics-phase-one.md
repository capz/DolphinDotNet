# High-level graphics: phase 1

The [roadmap](graphics-roadmap.md) records approved phase 1 and proposed phase 2.
These APIs extend the existing single-device, application-executor graphics model.
Create resources and staging buffers during setup. Complete passes and frames
synchronously, and keep every borrowed texture, mesh, model and effect alive.

## 2D camera, viewport and clips

```csharp
var camera2D = new Camera2D {
    Position = new Vector2(100, 40), Origin = new Vector2(160, 120),
    Zoom = 2, RotationDegrees = 15
};
using var draw = new Draw2D(device, new Draw2DOptions {
    Width = 320, Height = 240, SpriteCapacity = 64
});
// Inside an active device frame:
draw.Begin(camera2D, new Viewport(0, 0, 640, 480));
draw.PushClip(10, 10, 300, 220);
draw.DrawSprites(sprites, 0, spriteCount);
draw.PopClip();
draw.End();
```

Camera properties are snapshotted at Begin; rotation is clockwise in Y-down
logical screen space. `WorldToScreen` and `ScreenToWorld` convert logical points.
The physical viewport scales the logical coordinate system. Clips are logical
screen rectangles **after** camera transformation, intersect the preceding clip,
and use a stack of at most 16 levels. Empty intersections submit no geometry.
They do not rotate with the camera. Begin resets the stack and sampler/effect
state. Existing parameterless Begin retains full-framebuffer drawing.

`TextureRegion` borrows a texture and stores a source rectangle in texels.
`SpriteDraw` combines a region, destination, tint, rotation, destination-relative
origin, and `SpriteFlip.Horizontal`/`Vertical`. `DrawSprite` works without bulk
staging. `DrawSprites` preserves painter order and native adjacent batching.
Reserve `SpriteCapacity` (1–128) during construction; its default zero avoids
bulk buffers for existing callers. A range cannot exceed that capacity. Native
validates the whole block before submission; disposed resources and malformed
ranges reject. No drawing method allocates staging arrays per call.

## Sampling and texture coordinates

```csharp
material.Sampler = new SamplerState(
    TextureAddressMode.Repeat, TextureAddressMode.Mirror, TextureFilter.Linear);
material.TextureTransform = new UvTransform(
    new Vector2(.25f, 0), new Vector2(2, 2), rotationDegrees: 0);
material.SecondarySampler = new SamplerState(
    TextureAddressMode.Clamp, TextureAddressMode.Clamp, TextureFilter.Nearest);
// Or change a 2D batch's state, flushing pending work first:
draw.SetSampler(material.Sampler, material.TextureTransform);
```

Default sampling preserves the texture's existing filter and address behavior.
Materials provide independent `SecondaryTextureTransform` and sampler settings.
GPU texture matrices preserve indexed mesh fetch and also transform generated
reflection coordinates. Full imported-model material overrides use these states;
ordinary imported materials retain authored texture sampling. This phase supports
mip-0 nearest/linear sampling, not new mip-chain or palette package formats.

## Camera projection and visibility

`Camera.SetPerspective` updates FOV, aspect and near/far planes.
`SetOrthographic(width, height, near, far)` selects an orthographic projection;
both require finite positive near and far greater than near. LookAt retains the
selected projection. Project/Unproject take a physical `Viewport`, use Y-down
pixel coordinates, and map depth 0 to near and 1 to far. Picking rays originate
on the near plane. A point with no finite homogeneous projection rejects.

```csharp
camera.SetOrthographic(16, 9, .1f, 200);
Ray ray = camera.CreatePickingRay(cursorPixels, viewport);
bool visible = camera.Intersects(new BoundingSphere(worldCenter, worldRadius));
```

BoundingBox and BoundingSphere tests use world-space conservative bounds. Culling
is explicit; the renderer does not infer bounds for animation or deforming meshes.
Camera matrix products/inverses are cached until camera settings change. Projection
queries allocate no per-call arrays and belong to the application's graphics thread.

## Scene lights and fog

```csharp
var scene = new RenderSettings {
    Ambient = new Color32(24, 24, 32), LightCount = 1,
    Fog = new FogSettings(FogMode.Linear, new Color32(60, 70, 90), 20, 150)
};
scene.Lights[0] = new SceneLight(LightKind.Directional, Color32.White,
    Vector3.Zero, new Vector3(0, -1, -1));
renderer.Begin3D(camera, scene);
// Draw meshes, imported models or full material overrides.
renderer.End3D();
```

RenderSettings is snapshotted at pass entry. Up to eight GX directional, point or
spot lights are supported. Positions/directions are world-space; direction denotes
travel from the light. Point/spot ReferenceDistance is the distance for half
intensity under GX attenuation, and spot cutoff is in (0,90] degrees. Diffuse is
per vertex; unlit materials retain their unlit path. Zero lights means ambient
only. Fog can be linear, exponential or exponential-squared. Alpha ownership is
unchanged. Existing Begin3D/BeginFrame overloads retain the camera-space headlight
and no fog. BeginFrame(camera, settings) is the whole-frame convenience overload.

## Mesh and model submission

```csharp
var renderer = new Renderer(device, meshBatchCapacity: 64);
// Inside its 3D pass; one shared material, distinct transforms:
renderer.DrawMeshes(meshes, transforms, material, 0, 64);
var queue = new RenderQueue(capacity: 512, modelCapacity: 128);
queue.Submit(mesh, material, transform);
queue.Submit(modelInstance, otherTransform);
queue.Execute(renderer);
```

Reserve mesh bulk capacity (1–128) at construction; ordinary Renderer constructors
reserve none. Matrix arrays are borrowed only during the synchronous native call;
GPU commands retain no managed pointers. Each native block preflights tokens,
matrices and material requirements before submitting. Uniform unlit meshes retain
GX indexed fetch. Different transforms still issue separate GPU geometry draws.

RenderQueue uses a stable bounded O(n log n) material sort for opaque meshes.
Groups sharing a world-transform object can still use the existing merged stream;
other groups use indexed bulk submission. Model capacity defaults to zero, so
mesh-only queues avoid model matrix staging. Model submissions form a separate
cohort using the existing global opaque/transparent model sorter. Mesh transparency
retains input order and follows that cohort. Intersecting transparency across mesh
and model cohorts requires explicitly ordered passes. Execute does not promise
atomic rollback across multiple native blocks. Queue statistics count native
submission calls; use GpuProfiler for backend draws, texture binds and triangles.

## Validation

`python3 scripts/ci_graphics_phase_one.py` exercises Debug/Release AOT under GC
stress. Native tests cover lights/fog/sampler matrices, preflight failure without
submission, viewport mapping, empty clips and deferred lifetime. The standalone
GraphicsPhaseOne fixture and `dolphin_graphics_phase_one.py` check rendered patches
and compare 64 individual/bulk mesh and sprite submissions over 240 samples after
60 warmup frames, alternating order. The benchmark geometry is outside the view to isolate CPU submission. It does
not measure fill rate. Submission timings exclude presentation,
frame-start work and explicit yield, and are not complete GPU/CPU-cycle timings.
Physical-hardware appearance and performance remain unverified.

One validated Dolphin run produced these means for 64 submissions (microseconds):

| Work | Individual calls | Bulk call |
| --- | ---: | ---: |
| Distinct-transform meshes | 272 | 214 |
| Sprites | 369 | 329 |

The [capture proof](measurements/graphics-phase-one.json) includes the DOL hash,
workload, sample count and pixel checks. These figures compare the two APIs in
one workload; they do not establish a universal frame-time gain.
