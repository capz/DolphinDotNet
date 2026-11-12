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
effect overrides and `Draw2D.SetEffect` accept the contained `Effect` without material defaults;
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
definition/effect includes that resolve. Legacy v1-v3 packages remain at most 304 bytes.

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

## GX indirect water warp (DNEF v3)

DNEF v3 is opt-in and backward compatible with ordinary v1/v2 effects.
It uses two bound texture units, not three: primary TEXMAP0 supplies the
same foam/color mask at two UV scales; secondary TEXMAP1 is exclusively
a signed GX indirect displacement map. Stage flag 2 samples TEXMAP0 from
TEXCOORD1. One GX indirect stage warps the authored color-sampling stages.
A final unwarped TEX0 stage resolves base opacity. Non-warp materials
explicitly reset the indirect stage count and per-stage direct settings.

```csharp
using var water=TevMaterialPresets.WaterWarp(
    new Color32(196,228,222),
    new Color32(222,245,237),strength:76).Create(foamMask,displacementMap);
water.Material.Sampler=new SamplerState(
    TextureAddressMode.Repeat,TextureAddressMode.Repeat,TextureFilter.Linear);
water.Material.SecondarySampler=water.Material.Sampler;
// Set TextureTransform and SecondaryTextureTransform independently each frame.
renderer.DrawMesh(unchangedWaterMesh,water.Material,terrainTransform);
```

The displacement texture should contain independent horizontal/vertical
channels (GX RGB565 or RGBA8). All textures are allocated and uploaded once
outside frames. Tuning UV transforms and TEV colors is per-frame material
state only. Native validation rejects a zero/out-of-range warp gain, nonzero
reserved words, RGB samples of the indirect map, or a missing base/secondary
binding. DNEF v3 uses byte 20 (1–255) of the 32-byte header for warp gain;
all other header reserved words must remain zero.

`TevProgramBuilder.EnableIndirectWarp(byte gain)` is the generic API.
The `alternateUv:true` argument is permitted only on a primary-texture
`AddStage` after enabling warp. The managed material accepts the distortion
map as `SecondaryTexture`, and both textures must remain alive through
the frame. RGB cannot sample that second texture in an indirect effect.

The effect reproduces the original fixed-function *technique*, rather than
Nintendo's texture art, exact mip biases or ocean mesh. For distinct LOD biases
on the two sampled foam layers, another texture unit and material sampler
extension would be required. Hardware accuracy and performance need testing
on a real GameCube as well as the recording/native GX fixtures.

## Independent alpha and two-reference cutouts (DNEF v4)

`SetAlphaStage` configures GX alpha separately from RGB. Configure every authored
stage when opting in; the builder then omits the legacy opacity epilogue, leaving
all sixteen stages available. Both channels share the stage's one texture sample,
but have independent Previous/Color0/Color1/Color2 registers and konst selectors.
A stage cannot read primary RGB and secondary alpha together. Capture one sample
in a register, then combine it with the other at a later stage.

Alpha evaluates `D + ((1-C)*A + C*B)` for Add, or
`D - ((1-C)*A + C*B)` for Subtract; bias, scale and optional clamp follow.
Use Zero/AddHalf/SubtractHalf bias, One/Two/Four/Half scale, and `clamp:false`
for signed intermediate registers. Alpha constants choose Red/Green/Blue/Alpha
from one named color. `One` uses the same physical konst selector as a named
constant, so those inputs cannot share one alpha stage. RGB and alpha
read-before-write checks are independent; both final outputs must write Previous.

`SetAlphaCompare` overrides `Material.AlphaCutoff` for that effect with two exact
8-bit references and And/Or/Xor/Xnor. Ordinary material comparison is restored
by subsequent draws. No explicit alpha stages are required to use the comparison
alone; such programs preserve the legacy opacity calculation and epilogue.

For AnimalTown #47's two scrolling authored IA8 samples, the following original
preset keeps the existing RGB sum and combines both sampled alpha values:

```csharp
// reflection owns its authored mip chain. Binding it twice allocates no copy.
using var water = TevMaterialPresets.DualTextureGlints(
    new Color32(102, 150, 176), new Color32(215, 248, 244),
    lowReference: 33, highReference: 235).Create(reflection, reflection);
water.Material.Color = Color32.White; // raster opacity 255
water.Material.Blend = BlendMode.Alpha;
water.Material.DepthWrite = false;
water.Material.Sampler = authoredSampler;
water.Material.SecondarySampler = authoredSampler;
water.Material.TextureTransform = primaryScroll;
water.Material.SecondaryTextureTransform = secondaryScroll;
renderer.DrawMesh(waterMesh, water.Material, waterTransform);
```

The three-stage preset produces `raster.rgb + PrimaryLight*TEX0.rgb +
ReflectedLight*TEX1.rgb`, saturated, and alpha approximately
`raster.a * (TEX0.a + TEX1.a) / 2` with GX fixed-point rounding. It passes
`alpha <= 33 OR alpha >= 235`, rejecting the open middle band. Thresholds must
be ordered. Lower raster opacity also scales the alpha tested: at opacity 206,
the high branch at 235 cannot pass. Keep raster/vertex alpha opaque or explicitly
choose thresholds and blending for the intended opacity.

This is a reusable averaged-alpha glint material, not a claim of a retail game's
exact equation. Consumers may express other alpha combinations with the generic
builder. Existing authored mip levels, texture formats, UV scrolling and LOD bias
need no change. This addition introduces no EFB reflection/refraction capture.
Meshes and full imported-model material overrides accept both textures;
Draw2D/effect-only imported-model draws retain their single-binding restriction.

v1-v3 packages and material behavior stay unchanged. Opt-in v4 retains the
32-byte little-endian header and four 36-byte color slots, then interleaves
8-byte RGB and 8-byte alpha records per stage (maximum 432 bytes). Header byte 20
is optional indirect gain; byte 21 enables custom comparison; bytes 24..28 are
comparison0/reference0/operator/comparison1/reference1. Other reserved bytes
remain zero. The alpha record is A/B/C/D/op/control/konst/reserved. Control packs
explicit bit 7, clamp bit 6, scale bits 4..5, bias bits 2..3 and destination bits
0..1; an all-zero record requests legacy opacity. Native inputs are GX selectors
0..7; managed One and SecondaryTexture are normalized while packing. The existing
native resource owns a 168-byte alpha/render-state tail for v4/v5; there are no per-draw
allocations, texture copies or extra rendering passes.

Validation includes all 65,536 pairs of alpha samples, scalar konst channels,
state restoration, native malformed-package rejection, Debug/Release AOT with GC
stress, and retained GX recording tests. The existing GraphicsPhaseOne Dolphin
fixture also checks both branches of the cutout, rejected middle alpha,
independent secondary alpha and restoration to an ordinary alpha-128 material.
Physical GameCube precision/performance still needs hardware validation.

## Extended RGB arithmetic and blend factors (DNEF v5)

`AddStage` now accepts optional `bias`, `scale` and `clamp` arguments after
`alternateUv`, plus `TevOperation.Subtract`. Defaults preserve the original
program bytes and behavior. The extended arithmetic is
`(D + bias +/- mix(A,B,C)) * scale`, using GX fixed-point rounding. `GreaterRed`
retains its original zero-bias, scale-one, clamped contract.

RGB selectors also expose `PreviousAlpha`, `Color0Alpha`, `Color1Alpha`,
`Color2Alpha`, `TextureAlpha`, `SecondaryTextureAlpha`, `RasterAlpha` and `Half`.
Alpha selectors replicate a scalar into RGB. RGB and alpha initialization remain
independent: an RGB read cannot consume alpha produced by the same stage, though
both channels can read values produced by earlier stages. RGB and alpha still
share the stage's single texture sample. Mixed primary/secondary sampling fails
before serialization; secondary alpha sampling is also incompatible with using
that image as an indirect warp map.

`SetBlendFactors(TevBlendFactor.SourceAlpha, TevBlendFactor.Color)` selects
`SRCALPHA/SRCCLR`, including destination source-color modulation unavailable in
the ordinary material blend presets. GX encodes Color as factor 2: it reads
**destination** RGB when used as a source factor, and **source** RGB when used as
a destination factor. `InverseColor` is its complement. Other factors are Zero,
One, SourceAlpha, InverseSourceAlpha, DestinationAlpha and InverseDestinationAlpha.
Set `Material.Blend=BlendMode.Alpha` for transparent submission ordering. Custom
factors override the GX draw state, and following ordinary draws restore their
own factors.

Only these extended RGB or blend settings select v5; unmodified v1-v4 builders
retain their serialized layouts. V5 uses the v4 16-byte interleaved stage layout
and a 432-byte maximum without a matrix tail. RGB byte 5 is either the original destination 0..3,
or explicit bit 7 plus clamp bit 6, scale bits 4..5, bias bits 2..3 and destination
bits 0..1. In v5 only, header byte 22 enables custom blend factors in bytes 29/30;
byte 31 remains zero unless a custom indirect matrix is enabled. Native parsing validates these settings and both register
channels before resource allocation. Legacy packages cannot opt in accidentally.

Unclamped outputs saturate to signed 11-bit registers [-1024,1023]. A/B/C inputs
read the low byte, while D keeps its signed register value; final output also
uses the low byte before blending/alpha testing. Scaling by two/four happens
before interpolation rounding. This matters for nonlinear multi-sample effects;
floating-point arithmetic, early byte saturation or averaging produce different
results. The tests follow the integer model in Dolphin's
[hardware shader generator](https://github.com/dolphin-emu/dolphin/blob/master/Source/Core/VideoCommon/PixelShaderGen.cpp)
and [software TEV register representation](https://github.com/dolphin-emu/dolphin/blob/master/Source/Core/VideoBackends/Software/Tev.h).

These are generic fixed-function controls, not an assertion of any game's exact
material. Existing `DualTextureGlints` remains the original averaged-alpha preset.
V5 does not add scene capture, projective sampling or EFB reflection/refraction.

## Projected EFB sampling

`TextureCoordinateMode.ScreenProjection` projects the primary texture from the
active camera and each mesh's world transform. GX receives homogeneous STQ from
`Bias * Projection * View * World`, performs perspective division, and fetches
from the existing positions for both direct and indexed meshes. No CPU UV or
geometry rewrite is required. The ordinary primary texture transform is applied
homogeneously after screen projection; the secondary texture keeps authored UVs.
Normal-based reflection modes keep their previous secondary-slot behavior.

Allocate a `SceneCapture` between frames, end the desired opaque 3D pass, capture
its viewport, then begin a new 3D pass with the same camera and draw the water.
A capture is a synchronized EFB copy, not a second framebuffer or mirrored scene.
`new SceneCapture(device, 320, 240, NativeTextureFormat.Rgb565)` uses 153,600 bytes
for a half-size 640x480 copy. The existing constructor retains RGBA8 and its binary
signature. Only RGB565/RGBA8 non-mipmapped targets are accepted, with the existing
four-pixel target and even source-origin alignment rules. Release the capture
when its lifetime ends and keep the primary sampler clamped for screen sampling.

Projective matrices refresh for every object, including material-cache hits;
returning to an ordinary material restores its normal UV texgen. Resource and
AOT fixtures cover format, frame, primary-binding and flag bounds. The Dolphin
acceptance restores two overwritten scene patches from an RGB565 copy using the
same material at different world transforms, detecting stale projection state.


## Custom indirect matrix (DNEF v5)

Call `EnableIndirectWarp()` to reserve the secondary map/texgen, then configure
an explicit GX matrix instead of the normalized scalar-strength approximation:

```csharp
program.EnableIndirectWarp();
program.SetIndirectMatrix(0,0,0, 0,.001f,.015f, exponent:2,
    bias:TevIndirectBias.STU);
```

Each coefficient must be finite in [-1,1); the signed exponent is -17..46.
`TevIndirectBias` selects None/S/T/U or their ST/SU/TU/STU combinations using
GX's three-bit mask. GX quantizes coefficients on loading the matrix. The SDK
forwards all six float values and the exponent without normalizing the axes.
Custom stages use 8-bit indirect format, the requested bias mask, matrix 0,
no wrapping, no previous-coordinate accumulation, no unmodified LOD and no bump
alpha. The legacy scalar warp path and its existing stage behavior stay intact.
The primary map remains the color source; the secondary map is distortion data.

Header byte 23 enables a 28-byte tail after all interleaved stage records.
The tail contains six little-endian float32 coefficients, a signed exponent byte
and three reserved zero bytes. Header byte 31 then carries the bias mask; without
the matrix flag it remains zero. Programs with a custom matrix can be at most
460 bytes (sixteen stages plus tail). Native validation checks the exact payload
length, finite/range bounds, exponent, bias, warp bindings and reserved bytes
before allocating. Failed managed setters leave the previous program unchanged.
Serialization uses a bit-preserving numeric primitive supported by both CLR and
GameCube AOT; it does not rely on the host's float byte order.
