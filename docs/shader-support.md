# Shader support on GameCube

Managed presets and the first constrained material compiler are now implemented;
see [managed effects](effects.md) for current APIs, graph schema and limits. The
plans below retain the broader direction; multi-texture graphs, multipass effects
and another backend are still future work.

## What works today

GameCube's Flipper GPU has fixed vertex processing and a configurable **TEV
pixel combiner**. Dolphin emulates that hardware. Its ability to use host GPU
shaders internally does not let a GameCube executable submit GLSL, HLSL or SPIR-V.
DolphinDotNet currently exposes TEV through `Dolphin.Graphics.Raw.GX`; managed
`Material`/`Draw2D` support validated `GxEffect` programs as well as their default pipelines.

[ShaderDemo](../samples/ShaderDemo/README.md) demonstrates actual per-fragment
TEV operations: a grayscale reference, an animated color ramp, and two comparison
stages that quantize the same input into three toon bands. Geometry and uniforms
come from the CPU. The ramp and comparisons execute in the emulated GPU.

Useful hardware limits for an API design are 16 TEV stages, 8 texture maps,
8 texture coordinate generators, 4 indirect stages, 4 constant color registers,
and the previous result plus 3 general TEV color/alpha registers. TEV arithmetic
is fixed-point and has clamping/precision rules. These are hardware budgets,
not an instruction count for arbitrary modern shader programs.

## Angles and implementation plans

| Approach | Best fit | Main constraint | Suggested order |
| --- | --- | --- | --- |
| Managed effect presets | Tint, ramps, toon bands, dissolve, two-texture mixes | A finite set of documented pipelines | First |
| Restricted material graph / shader DSL → TEV | Developer-authored combinations of supported operations | Must fit TEV arithmetic and register/stage budgets | Second |
| CPU vertex effects and generated textures | Waves, vertex deformation, procedural content | CPU time, upload bandwidth and MEM1 | Alongside presets |
| Multipass and indirect-texture effects | Distortion, reflection-like effects, small bloom/blur | Copies, extra draws, limited precision and EFB resources | After effect ownership |
| Modern GPU shaders on another backend | General GLSL/HLSL-style materials | Requires a different graphics backend; GameCube needs fallback materials | Separate roadmap |

### 1. Managed effect presets — implemented

`GxEffect.ColorRamp` and `GxEffect.ToonBands` bind through `Material.Effect` and
`Draw2D.SetEffect`, and imported models accept a per-draw effect through
`Renderer.DrawModel`/`Renderer.Draw(GameObject, effectOverride)`. Programs are immutable, named color parameters update between
frames, batch changes flush queued work, and native validation/lifetime rules
protect resource use. ShaderDemo and rendered-pixel checks exercise the presets,
a compiled graph, and restoration of the normal HUD pipeline. See
[the API and lifecycle contract](effects.md).

Next preset work can add texture-driven ramps, dissolve and extra material
examples. Physical GameCube profiling remains a separate acceptance check.

### 2. Restricted material graph / shader DSL compiled to TEV — implemented

`DolphinDotNet.Effects` compiles a bounded RGB DAG at build time into DNEF v1.
The SDK `DndEffect` item can embed the result directly into generated C# source.
The first version supports raster/texture input, four named color parameters,
channel replication, zero/one, add, multiply, mix and strict R8 greater-than.
It shares expressions, folds exact identities, allocates four result registers
and reports node-specific errors for stage/register exhaustion. The current toon
graph uses nine stages and three live result registers.

[The graph schema](effects.md) documents precision, package loading and resource
limits. Current compiler and native limits are deliberately narrower than the
full GPU: one texture with existing UV0 coordinates, four color parameters,
RGB expressions and no loops, derivatives or general source language. More
texture inputs, coordinate expressions, richer folding and improved scheduling
can follow without changing resource ownership. A GLSL front end should wait
until the supported subset is mature; syntax cannot remove hardware limits.

### 3. CPU effects / software-generated content

Use `DynamicMesh.SetVertices` for deformations and CPU-generated textures for
small procedural effects. Current `Texture2D.Create` performs a one-time upload;
streaming content needs a safe `Texture2D.UpdateRgba` or staging API first.

1. Add bounded texture updates between frames with dimension checks, tile packing,
   cache maintenance and explicit GPU synchronization. Avoid dispose/recreate loops.
2. Preallocate vertex and pixel buffers. Offer a displacement example and a small
   low-resolution procedural texture, updated less frequently than presentation.
3. Measure CPU generation, upload time and memory separately in the statistics
   overlay. Compare frame-time distributions with the effect disabled.
4. Establish budgets on real GameCube hardware before increasing resolution or
   update rate. Keep expensive updates off frames that already exceed the budget.

Deliverable: flexible vertex effects and small procedural textures. Full-screen
CPU fragment shading is a poor default on a 485 MHz CPU with limited main RAM.

### 4. Multipass / indirect texture effects

Use TEV indirect texturing for UV distortion where possible. Use extra draws and
EFB copies for effects that need a previous scene image. `CopyToTexture` currently
copies a same-size RGBA8 destination between passes; it is not an arbitrary render
target, downsampling or programmable postprocessing API.

1. Add effect-owned texture bindings and indirect coordinate setup with validated
   formats, matrices and stage counts. Start with one scrolling distortion map.
2. Specify explicit capture regions, copy formats and scaling before adding a
   bloom example. Preserve color/depth needed by the following pass; prohibit
   reading a texture while it is the copy destination.
3. Add a two-pass low-resolution effect with reusable textures. Track copies,
   draw calls, estimated bytes moved and capture memory in diagnostics.
4. Test pass ordering and state restoration, then profile fill rate and copy costs
   on hardware. Provide a lower-cost preset or an off switch for constrained scenes.

Deliverable: richer effects with explicit render costs, rather than pretending
that a single TEV program can sample arbitrary framebuffer data.

### 5. Another backend for general programmable shaders

If general vertex/fragment programs become a goal, introduce a backend-neutral
material description and add a desktop renderer (or a future console backend
with programmable GPUs). Keep the GameCube TEV backend as a constrained target.

1. Separate resource ownership and material semantics from raw GX state.
2. Define a capabilities query and explicit fallback material selection. Unsupported
   effects must fail clearly or choose a developer-specified fallback.
3. Implement shader compilation, parameter binding and a reference desktop preview.
4. Compare the supported subset across backends; keep target-specific effects
   visibly target-specific. Do not label a desktop-only effect as GameCube support.

Deliverable: modern shaders for the new backend, with the additional maintenance
cost of a renderer, toolchain and cross-backend acceptance suite. An emulator-only
host extension would also require a patched Dolphin and would not run on a console.

## Recommended sequence

Managed presets and the first material compiler are implemented. Extend their
pixel and hardware acceptance coverage before expanding the operation set. Add
texture/coordinate inputs and indirect effects according to developer demand.
Keep the separate programmable-backend proposal independent so it does not delay
effects that can run on GameCube.
