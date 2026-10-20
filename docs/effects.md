# Managed TEV effects and the material compiler

`GxEffect` is a GameCube RGB pixel-combiner program, shared by `Material`,
`Draw2D` and imported-model draw overrides. It uses TEV fixed-point arithmetic. It is not GLSL/HLSL and does not
provide programmable vertex shaders. Alpha retains the normal pipeline behavior:
vertex/material alpha multiplied by texture alpha when a texture is bound.
Lighting and material tint feed the raster RGB input before the effect.

## Presets

For reusable mesh materials, a managed stage builder, textured toon bands,
normal-based metallic reflection maps and emissive masks, see
[Managed TEV materials](tev-materials.md).

Create effects between frames and reuse them. Uniform updates allocate no native
storage and must happen between frames. Cache a slot for repeated updates.

```csharp
using var ramp = GxEffect.ColorRamp(new Color32(12,18,58), new Color32(60,180,240));
using var toon = GxEffect.ToonBands(65,165,
    new Color32(12,24,64), new Color32(30,90,158), new Color32(70,220,248));
var material = new Material { Effect = toon };
int brightSlot = ramp.GetUniformSlot("Bright");
ramp.SetColor(brightSlot, new Color32(100,210,240)); // before BeginFrame
```

The ramp evaluates `mix(Dark, Bright, raster.rgb)` in two stages. Toon uses
seven stages and selects Dark/Middle/Bright using strict `raster.r > threshold`
comparisons. Initial thresholds must be ordered. Parameters are `Dark`, `Bright`
for the ramp and `Thresholds`, `Dark`, `Middle`, `Bright` for toon. Thresholds are
packed into the R and G bytes of the Thresholds color. Updating this generic
parameter can deliberately change their ordering; values are not silently sorted.
Palette colors need not be monotonic.

```csharp
device.BeginFrame(Color32.Black);
renderer.Begin3D(camera);
renderer.DrawMesh(mesh, material, transform);
renderer.End3D();
draw.Begin();
draw.SetEffect(ramp);
draw.DrawTexture(texture, destination, Color32.White);
draw.SetEffect(null); // flush effect vertices and restore the default pipeline
// Draw normal UI here.
draw.End();
device.EndFrame();
```

Effects are applied inside the native renderer after standard state setup. A
change to the Draw2D effect flushes queued geometry; rebinding the same effect
does not. Begin resets the batch to its default effect. Default materials,
subsequent passes, raw drawing and the diagnostic overlay do not inherit custom
TEV stages. Raw callers still own their state during their own pass.

`Dispose` follows the texture resource rules: native state referenced by queued
vertices remains alive through frame completion, while new uses of a disposed
managed effect throw. Uniform mutation during a frame is rejected. Effect
creation and parameter updates run on the application executor. A texture-reading
program requires a bound texture; missing/disposed textures are rejected before
submission. The program does not own its input texture.

## Imported models: per-draw overrides

Pass an existing effect to `Renderer.DrawModel(instance, transform, effectOverride)`
with either a `Transform` or a 16-float world matrix, or to
`Renderer.Draw(gameObject, effectOverride)`. Existing overloads and an explicit
`null` retain imported rendering. An empty GameObject submits nothing and does
not validate an unused effect.

```csharp
using var toon = GxEffect.ToonBands(65, 165,
    new Color32(12, 24, 64), new Color32(30, 90, 158), new Color32(70, 220, 248));
// On the application executor, using an existing camera, instance and transform:
renderer.BeginFrame(camera);
renderer.DrawModel(instance, transform, toon);
renderer.DrawModel(otherInstance, otherTransform, null);
renderer.EndFrame();
```

The override replaces RGB combination on every submitted mesh for that call.
It uses the original texture/UV0 and raster color: vertex color times imported
tint, including the model renderer's diffuse shade unless unlit. It does not
mutate the shared Model or its instances. Each later draw chooses its own effect.
No per-draw material copies or vertex buffers are allocated.

Texture alpha, alpha testing, blend factors, transparency sorting, depth writes,
culling, skinning, filters, wrapping and mipmaps retain their imported behavior.
`RequiresTexture == false` still keeps any imported texture bound for alpha.
ColorRamp and ToonBands replace texture RGB with raster-driven color; combine
texture RGB explicitly in a compiled graph if that is desired.

A texture-requiring effect with any submitted untextured mesh throws
`ArgumentException` identifying `effectOverride`, before sorting, triangle
accounting or GX submission. Unreferenced materials do not count. A disposed
managed effect throws `ObjectDisposedException`. Uniform updates remain forbidden
inside a device frame; disposal after submission defers reclamation until frame
completion and rejects later uses. Overrides are independent of ARAM snapshots:
cold models reject draws, and `PrepareAsync` restores normal/default or effect
drawing with the caller's chosen live effect.

Per-material overrides, normal/position shader inputs, material editing and
wireframe modes are outside this API.

## Compile a material graph at build time

The `DolphinDotNet.Effects` tool compiles a bounded RGB expression DAG to DNEF v1.
Use the [toon graph](../samples/ShaderDemo/toon.dndfx.json) as a full example.

```sh
dotnet run --project tools/DolphinDotNet.Effects -c Release -- \
  samples/ShaderDemo/toon.dndfx.json /tmp/toon.dndfx /tmp/CompiledEffect.g.cs
```

The third argument optionally emits embedded C# bytes (`CompiledEffect.Package`).
A fourth argument chooses the generated class name. Binary packages can instead
be loaded through `GxEffect.FromPackage(bytes)` or `await GxEffect.LoadAsync(path)`;
the latter reads the full small package before creating native state.

SDK projects can generate both artifacts before C# compilation:

```xml
<ItemGroup>
  <DndEffect Include="toon.dndfx.json"
             GeneratedSource="CompiledEffect.g.cs" ClassName="CompiledEffect" />
</ItemGroup>
```

Use a distinct class and source path per item. Add the item before importing
Sdk.targets. The binary lands under `obj/.../dnd-effects`; the generated source
embeds it, so this mode needs no device filesystem. Generated source may be
checked in for IDE/design-time browsing; regular builds regenerate it. The build
reports stage, uniform and register counts. No shader compilation occurs in the
render loop.

The JSON object has `uniforms`, `nodes`, and `output`. Uniforms have a unique
printable ASCII `name` (1–31 bytes) and four RGBA bytes. Nodes have unique IDs:

| Operation | Properties | RGB result |
|---|---|---|
| `raster` | none | Interpolated, tinted/lit vertex RGB |
| `texture` | none | Texture 0 sampled with the existing UV0 coordinates |
| `zero`, `one` | none | RGB 0 or 1 |
| `uniform` | `name`, optional `channel` | Parameter RGB, or replicated `r`/`g`/`b`/`a` |
| `add` | `a`, `b` node IDs | Saturated addition |
| `multiply` | `a`, `b` | Componentwise product |
| `mix` | `a`, `b`, `t` | Componentwise interpolation |
| `greater` | `a`, `b` | RGB 1 if a.r > b.r, otherwise 0 |

All arithmetic clamps after each graph operation. TEV interpolates with an
8-bit factor expanded to a 0–256 range; it is not unrestricted floating point.
Uniform RGB is unaffected by its alpha byte, except an explicit `channel: "a"`.
One texture input and its existing UV0 generator are supported in this version.
Arbitrary literals can be named uniform defaults; this version does not offer
arbitrary constant expressions, texture-coordinate expressions or multi-texture
sampling. Alpha expressions, loops, derivatives, sin/cos and general shader
source languages are unsupported and are not silently approximated.

The compiler traverses reachable nodes, detects cycles/unknown references,
shares identical expressions and folds exact constants/identities (`x*1`, `x+0`,
`x*0`, saturated `x+1`, `x>x`, and constant-endpoint mixes). It allocates four result registers and emits up to 16 stages.
At most four named color parameters are accepted; use channels to pack scalars.
Graphs are bounded to 64 KiB, 128 nodes and 64 expression levels. Unsupported
operations, excessive stages and excessive live register values produce errors
identifying the node/resource. The scheduler rejects graphs it cannot place;
it does not promise to find every possible hand-optimized TEV schedule.

DNEF packages are 176 bytes plus eight bytes per stage (maximum 304 bytes).
The runtime independently validates version, length, parameter names, selectors,
register initialization, texture requirements and final output. It copies the
program into the existing bounded graphics resource pool; editing or freeing the
source array cannot mutate live native state. Parameter names are retained by the
managed wrapper and values live in the native effect.

## Validation and next limits

ShaderDemo uses preset effects through Material, imported-model draw overrides,
and the compiled toon graph through Draw2D. Its original two-material glTF fixture
is converted by `scripts/generate_model_effect_fixture.py`; no retail assets are
needed. The capture requires a default/effect/default frame cycle with preserved
texture cutouts and ordinary mesh/UI output. The existing software-Dolphin capture validates actual output;
compiler tests compare all 256 intensities across threshold boundaries and test
quantization, sharing, folding, deterministic output and resource diagnostics.
Graphics AOT tests exercise deferred disposal, parameter mutation rejection,
texture requirements, default restoration and native package validation under
GC stress and sanitizers. A recording GX API also compiles the production model
submission path and shared combiner, asserting binding order, preflight failures,
skinning, samplers, mirrored culling, sorting and pending/stale resource handling. Physical GameCube performance/precision remains a
hardware acceptance check.

The next extensions can add additional texture inputs/coordinate generators,
better scheduling and more graph operations without changing the ownership model.
Multipass captures and indirect effects remain separate work, as described in
[the shader roadmap](shader-support.md).
