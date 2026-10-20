# Managed TEV materials

`TevProgramBuilder`, `TevMaterialDefinition` and `TevMaterial` build on the existing
`GxEffect` pipeline. These presets provide toon shading, metallic reflections and emissive surfaces.
They contain no extracted game programs or art. Supply your own textures;
no commercial disc images are needed.

## Use a preset on a mesh

Create textures and materials before `BeginFrame`, reuse them across draws, and
dispose them when finished. A material owns its effect and borrows its texture.
One definition can create independent material instances with different textures
and parameter values. Changing options after creating a definition has no effect.

```csharp
// albedo, reflectionMap and emissionMap are caller-owned Texture2D resources.
var toonDefinition = TevMaterialPresets.Toon(new ToonMaterialOptions {
    Low = 70, High = 170,
    Dark = new Color32(30, 40, 75),
    Middle = new Color32(100, 150, 180), Bright = Color32.White
});
using var toon = toonDefinition.Create(albedo);
using var metal = TevMaterialPresets.MetallicReflection(new MetallicMaterialOptions {
    ReflectionStrength = 190, ReflectionTint = new Color32(180, 220, 255)
}).Create(reflectionMap);
using var glow = TevMaterialPresets.Emissive(new EmissiveMaterialOptions {
    BaseColor = new Color32(25, 40, 60), EmissionColor = new Color32(20, 230, 255),
    EmissionStrength = 200
}).Create(emissionMap);

// Update between frames. Cache slots when updating every frame.
int emissionSlot = glow.Effect.GetUniformSlot("EmissionStrength");
glow.Effect.SetColor(emissionSlot, new Color32(160, 160, 160));
metal.Material.AlphaCutoff = 0.5f;
device.BeginFrame(Color32.Black);
renderer.Begin3D(camera);
renderer.DrawMesh(normalMesh, toon.Material, toonTransform);
renderer.DrawMesh(normalMesh, metal.Material, metalTransform);
renderer.DrawMesh(normalMesh, glow.Material, glowTransform);
renderer.End3D();
device.EndFrame();
```

The presets default to diffuse camera-headlight lighting. Use
`VertexPositionNormalColorTexture` for intended shading. Vertex color and
`Material.Color` tint the raster input before TEV; white vertex colors make the
headlight the toon intensity source. Set `Material.Lighting = LightingMode.Unlit`
to drive raster RGB explicitly with vertex colors.

| Preset | Stages | Named colors | Texture and result |
| --- | --- | --- | --- |
| Toon | 8 (7 without texture) | Thresholds, Dark, Middle, Bright | UV0 albedo times the palette selected by strict raster.r > Low/High comparisons |
| MetallicReflection | 3 | BaseColor, ReflectionTint, ReflectionStrength | mix(BaseColor × raster, reflectionMap × ReflectionTint, ReflectionStrength) |
| Emissive | 3 | BaseColor, EmissionColor, EmissionStrength | clamp(BaseColor × raster + emissionMap × EmissionColor × EmissionStrength) |

Strength colors are RGB factors: set all three bytes to the same intensity for
a scalar, or vary channels for colored modulation. Uniform alpha does not affect
RGB unless explicitly selected by a custom stage. Toon thresholds occupy R/G of
`Thresholds`; initial values must be ordered, and later uniform changes are direct
and intentionally do not sort values. Set `ToonMaterialOptions.Textured = false`
for palette-only output and call `Create()` without a texture.

By default, the metallic map is a clamped 2D matcap: normalized camera-space normals map to
`u = .5 + .5 nx`, `v = .5 - .5 ny`. The native renderer applies inverse transpose
and normalization on the CPU before submitting TEX0 to GX's identity texgen. This
handles rotation and nonuniform scale, with zero normals mapping to the center.
Use nonsingular transforms. Normals and a bound map are required; color-only or
UV-only meshes are rejected before submission. The default remains a single-texture surface reflection approximation.
The extensions below add independent albedo and world-oriented environment sampling. Emission uses a UV0 RGB mask (black off, white full color); it remains
visible when raster lighting is dark. There is no bloom or light cast on neighbors.

All preset RGB arithmetic clamps with GX's fixed-point rounding. Opacity remains
vertex alpha × Material.Color alpha × texture alpha. Use opaque alpha in reflection
and emission maps unless you want cutouts/transparency. RGB lighting does not light
alpha. Configure blend, cutoff, depth test/write and culling on `Material` as usual;
emission alone does not enable additive blending or disable depth writes.

These full material defaults apply through `Renderer.DrawMesh` and
`Renderer.DrawModelWithMaterial`. Imported-model
effect overrides and `Draw2D.SetEffect` accept the contained `Effect` for RGB only;
they do not apply the material's lighting or coordinate mode. In particular,
passing the metallic effect alone uses existing UV0 instead of reflection coordinates.

## Author a reusable program

```csharp
var program = new TevProgramBuilder();
int tint = program.AddColor("Tint", new Color32(150, 220, 255));
program.AddStage(TevInput.Zero, TevInput.Texture, TevInput.Constant, TevInput.Zero,
    colorSlot: tint);
var definition = new TevMaterialDefinition(program, LightingMode.Unlit);
using var custom = definition.Create(albedo);
```

`AddStage` evaluates `clamp((1-C)*A + C*B + D)` or, for `GreaterRed`,
`clamp(D + (A.r > B.r ? C : 0))`. Each stage selects one of four named colors,
optionally replicated from R/G/B/A. Results write Previous/Color0/Color1/Color2.
The builder rejects uninitialized register reads, invalid selectors, invalid or
duplicate names, more than four colors or sixteen stages, and a final result
outside Previous. Failed additions do not mutate the builder. Packages and
definitions are independent snapshots. Native DNEF validation runs again before
resource allocation; arbitrary native GX calls are not exposed through this API.

The DNEF v1 contract remains supported. DNEF v2 adds a second texture and texgen;
both formats retain one raster channel, four RGB registers, four konst colors,
sixteen total stages and no indirect stages. Each draw
resets indirect count, active TEV direct/swap/konst state and texgen source.
UV draws after reflection draws restore authored UV0. Existing default materials,
queued disposal and frame mutation restrictions follow [effects.md](effects.md).

## Audit and validation

The architecture audit found an existing bounded DNEF program/resource pool,
shared native combiner, separate mesh/model paths, and application-executor/frame
lifetime checks. This implementation extends that architecture without a second
native resource type, compiler binding, asset dependency or UI. It fixes combined
RGB/alpha headlight state and makes the shared combiner explicitly disable indirect
texturing. The mesh boundary validates reflection geometry and binding requirements.

Run `dotnet run --project tests/TevMaterialTests -c Release` for exhaustive toon
input/texture values, reflection/emission equations, snapshots and safety limits.
`GraphicsSmoke` exercises all three presets through AOT/native submission, parameter
updates, rejection of missing normals and shared frame ownership. The recording GX
model tests exercise the production combiner, lighting channel setup, indirect
reset and normal projection math, alongside existing effect lifetime/alpha tests.
CI runs these with the existing graphics checks, including sanitized native tests.
Physical GameCube precision/performance and visual acceptance remain hardware
checks; the tests do not establish pixel-identical commercial-game rendering.

## Independent base and auxiliary textures

The one-texture APIs retain their defaults. Enable `SeparateBaseTexture` for an
independent UV0 albedo map and reflection/emission map:

```csharp
using var metal = TevMaterialPresets.MetallicReflection(new MetallicMaterialOptions {
    SeparateBaseTexture = true,
    ReflectionCoordinates = TextureCoordinateMode.WorldReflection,
    ReflectionStrength = 180
}).Create(albedo, environment);
using var glow = TevMaterialPresets.Emissive(new EmissiveMaterialOptions {
    SeparateBaseTexture = true, EmissionStrength = 200
}).Create(albedo, emissionMask);
```

The textured base becomes `albedo * BaseColor * raster`. Reflection blends that
base with the independent tinted environment; emission adds the independent
colored mask. Each dual-texture preset uses five stages, including an opacity
stage. Only base texture alpha affects opacity; auxiliary alpha is ignored.
Textures are borrowed, independently checked for disposal, and retained by the
existing frame resource lifetime. Missing required slots fail before drawing.
`Material.SecondaryTexture` and `TevInput.SecondaryTexture` also work with custom
programs. One stage can sample one texture: use a register to combine two samples.
Two-texture builders reserve one of sixteen stages for a final TEX0 opacity
resolve, so at most fifteen authored RGB stages are accepted. `StageCount` on the
definition/effect includes that resolve. Packages remain at most 304 bytes.

DNEF v2 uses the texture header word as a two-bit requirement mask; stage byte 7
selects texture/coordinate slot 0 or 1. Native validation rejects absent texture
requirements, invalid slots and a final auxiliary slot. v1 keeps its original
zero-reserved-byte contract. Draw2D and RGB-only imported overrides reject
secondary-texture effects; use a full material draw instead.

## Full imported-model material overrides

```csharp
renderer.Begin3D(camera);
renderer.DrawModelWithMaterial(instance, transform, metal.Material);
renderer.DrawWithMaterial(gameObject, glow.Material);
renderer.End3D();
```

These explicitly named methods avoid ambiguity with existing null RGB-effect
overrides. One supplied material replaces all imported material state for the
submission: textures, tint, lighting, blend, cutoff, depth and culling. Authoring
UV0 and vertex colors, hierarchy transforms, animation/skinning and model
residency remain intact; imported textures and per-material tints are not used.
The underlying model and other instances are never mutated. Opaque overrides
submit in source order; transparent overrides sort all meshes back-to-front.
The path shares the mesh GX backend and uses a bounded 768-vertex scratch buffer,
with no per-frame heap allocations. All referenced textures, effect, skinned
buffers and model transforms are checked before sorting or submission. Cold
models and singular transforms fail. Previous `DrawModel` calls retain their
original imported materials and texture samplers.

## World-oriented reflections

Set `ReflectionCoordinates = TextureCoordinateMode.WorldReflection` on metallic
options, or set `Material.TextureCoordinates` directly. Supply a longitude/latitude
(equirectangular) environment image. The renderer computes a perspective eye ray
at each vertex, reflects it about the inverse-transpose normalized normal, rotates
the result from camera to world space, and converts it to longitude/latitude.
Camera translation changes the reflection and camera rotation does not rotate the
environment. Longitude wraps and latitude clamps. With two textures the base map
keeps UV0 and only the auxiliary map receives these generated coordinates.

This is per-vertex environment mapping, without scene ray tracing, parallax,
roughness filtering or Fresnel. Use sufficient tessellation for large surfaces.
Triangles spanning the longitude seam or environment poles may show interpolation
distortion; place seams away from important surfaces or split/tessellate geometry.
The extra CPU normalization and trigonometry need physical-hardware performance
validation. Zero-length normals/eye rays have a deterministic center fallback.

## Render acceptance

ShaderDemo includes fixed geometry for all three presets, independent auxiliary
maps with zero alpha, default-state restoration, world reflection sampling and a
full imported-model material override. `dolphin_shader_test.py` checks distinct
pixel colors in a software-Dolphin capture. Its negative controls remove each
patch in turn and require rejection. Native tests cover perspective eye-ray and
inverse-camera rotation math, skinned full overrides, transparent sorting,
preflight failure without submission, and malformed v2 programs. No retail assets
or interactive controls are added for these checks.
