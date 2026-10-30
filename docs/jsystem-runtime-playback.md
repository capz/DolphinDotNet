# JSystem runtime playback (follow-up to GCFT format readers)

This is the runtime companion to [GCFT/JSystem interoperability](gcft-jsystem-integration.md).
The baseline format reader and desktop tooling landed in PR #136. The
opt-in renderer extensions in this branch keep ordinary `Model`,
`Renderer.DrawModel`, `Texture2D`, and native asset packages unchanged.

## JPA point/billboard particle playback

```csharp
using Dolphin.Formats;
using Dolphin.Graphics;

// Setup between frames, after mounting storage:
var archive = await JpcArchive.LoadAsync("sd:/effects/scene.jpc");
using var effect = new JpcParticleEmitter(archive, particleId: 0x0020,
    maxParticles: 128, seed: 1234);

// Per frame, update before the renderer begins:
effect.Simulation.X = 1.0f;
effect.Simulation.Y = 2.0f;
effect.Update(deltaSeconds);

// Inside an active 3D pass:
renderer.DrawModel(characterInstance, world);
effect.Draw(renderer, camera);
```

The supported profile is deliberately explicit: JPAC1-00/JPAC2-10, a point
volume in BEM1, billboard shape in BSP1, one texture selected through TDB1,
finite emission/lifetime and color. BEM1 and BSP1 must be the only behavior
chunks besides TDB1/TEX1. It rejects unsupported child emitter blocks, field
forces, material TEV stages, texture scroll, animation keys and non-billboard
geometry instead of pretending to reproduce them.

The managed `JpcParticleSimulator` uses preallocated state, a 60 Hz fixed
step, a 1-256 particle budget and deterministic pseudorandom spread.
`JpcParticleEmitter` allocates a GX `Billboard` and texture during setup,
sorts translucent particles by camera depth using reusable scratch arrays,
and draws through the existing native GX mesh path. It does not provide the
original game's complete JPA physics, emission volumes, shader pipeline,
child particles or hardware-level sprite batching.

You can test the simulation without GX by constructing
`JpcEmitterDefinition.Read(archive, particleId)` and passing that to
`JpcParticleSimulator`.

## J3D material animation: per-material render state

```csharp
using Dolphin.Formats;
using Dolphin.Graphics;

// Load a retail-derived J3D asset using the existing source loader:
using var model = await Model.LoadJ3dAsync("sd:/actor.bmd",
    allowMaterialApproximation: true);
using var instance = model.CreateInstance();

// Source MAT3 material names come from the original J3D model bytes:
var names = J3dMaterialNames.Read(await Dolphin.IO.File.ReadAllBytesAsync(
    "sd:/actor.bmd"));
var player = new J3dAnimationPlayer(model, names, fps: 30);
player.BindByName(J3dMaterialAnimation.Read(
    await Dolphin.IO.File.ReadAllBytesAsync("sd:/actor.btk")));
player.BindByName(J3dMaterialAnimation.Read(
    await Dolphin.IO.File.ReadAllBytesAsync("sd:/actor.brk")));

// During gameplay, on the application executor:
instance.Update(deltaSeconds);
player.Update(deltaSeconds);

renderer.BeginFrame(camera);
player.Draw(renderer, instance, worldTransform);
renderer.EndFrame();
```

`J3dAnimationPlayer.BindByName` matches BTK/BTP/BRK track names to source
MAT3 names and refuses unmapped/ambiguous material names. It is necessary
to supply the **actual source J3D2 BMD3/BDL4 bytes** for MAT3 names;
compiled DNMD packages do not retain the original MAT3 string table.
BVA is shape-indexed and requires
`player.BindShapeVisibility(bva, sourceShapeIndex, materialIndex)`; mapping
a shape to an entire material is only correct when all its shapes have the
same visibility.

Each material has a fixed 12-float channel record: visibility, RGBA
multipliers, affine UV transform, and source texture index override. The
new `Renderer.DrawModelAnimated` preflights the entire channel record
inside the native application-executor renderer, uses the existing
skinning/mesh/GX submission and never mutates shared model material data.
The default `Renderer.DrawModel` path remains unchanged.

BTK playback implements a **basic affine S/T profile**. Multiple texture
generators, non-affine Q transformations, complex Maya matrix modes,
advanced GX TEV stages and correct reconstruction of every game's texture
coordinate conventions require separate work. For source MAT3 materials
sharing multiple meshes, material-level overrides apply to all such meshes.
Alpha blending still follows source material blend flags, not an arbitrary
BRK override. Source animation angles and matrix orientation need
visual comparison with real game footage; synthetic tests prove parsing
and arithmetic, not visual fidelity.

## Native BTI texture path

Non-indexed standalone BTIs (I4, I8, IA4, IA8, RGB565, RGB5A3, RGBA8 and CMPR)
can be sent to `Texture2D.FromBti` as the original tiled GX payload in a
DNTX v2 package, preserving every validated mip level without an RGBA
conversion. Indexed CI4/CI8/CI14X2 images still expand their base level
to RGBA8. Original wrap modes, independent min/mag filters, LOD bias and
palette-indexed mip chains are not yet represented by `Texture2D`.

## Validation

- `dotnet run --project tests/GameCubeFormatsTests`: synthetic JPAC1/JPAC2,
  BEM1/BSP1 parameters, deterministic spawn/expiry and J3D BTK name/UV
  channels plus malformed-input cases.
- `python3 scripts/ci_test_suite.py assets` and
  `python3 scripts/ci_test_suite.py graphics` exercise the asset and
  graphics regression paths in CI.
- Native model draw functions compile under the normal host and GameCube
  checks; output needs testing on actual hardware before claiming full
  GX/memory/performance compatibility.
- No game ROM, retail textures, fonts or proprietary game data included.

## License and attribution

Format structures were derived from the publicly documented MIT-licensed
[LagoLunatic/GCFT](https://github.com/LagoLunatic/GCFT) and
[gclib](https://github.com/LagoLunatic/gclib) (gclib pin
`4e8f48000b757defc05179e341c3a2b06dfaadee`), plus DolphinDotNet's
existing independently implemented GX model render path.
Keep the respective MIT copyright notices if copying original source code.
