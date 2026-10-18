# Shader support on GameCube

## What works today

GameCube's Flipper GPU has fixed vertex processing and a configurable **TEV
pixel combiner**. Dolphin emulates that hardware. Its ability to use host GPU
shaders internally does not let a GameCube executable submit GLSL, HLSL or SPIR-V.
DolphinDotNet currently exposes TEV through `Dolphin.Graphics.Raw.GX`; managed
`Material`/`Draw2D` select the runtime's built-in pipelines.

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

### 1. Managed effect presets — recommended first slice

Add an immutable `GxEffect` description and a small parameter block, with
`Material.Effect` and `Draw2D.SetEffect` as proposed entry points. Start with
`ColorRamp` and `ToonBands`, using this demo's TEV equations and pixel tests.
These names are proposals, not existing APIs.

1. Describe each preset's stages, allowed inputs, uniforms and precision. Keep
   the default effect identical to current rendering.
2. Apply effects inside the native draw backend **after** it establishes the
   standard GX state. Calling raw GX immediately before `DrawMesh` currently
   fails to preserve custom TEV state because the backend restores its pipeline.
3. Validate texture handles, stage/register budgets and parameters before any
   GPU submission. Retain resource lifetimes using the existing token system;
   reject disposed inputs and mutation during a primitive stream.
4. Make Draw2D flush when effect or uniform bindings change. Cache unchanged
   state, with no per-frame allocation or compilation. Reestablish state at
   raw/managed pass boundaries and restore the diagnostic overlay.
5. Run software-Dolphin pixel checks for each preset and for transitions between
   two effects, default rendering and raw drawing. Check console/Dolphin hardware
   backends separately for precision and performance.

Deliverable: two useful effects available to ordinary C# sample code without raw
GX calls. This is the smallest path to a reusable shader-like API.

### 2. Restricted material graph / shader DSL compiled to TEV

Build on the same effect representation. Accept a small typed graph with texture
and raster inputs, constants, add, multiply, mix and comparisons. Compile at build
time to a validated TEV program; expose parameter updates at runtime.

1. Specify supported semantics first: fixed-point ranges, saturation, comparison
   behavior and permitted texture coordinate sources. Include a CPU reference
   evaluator with explicit quantization.
2. Create an intermediate representation, constant folding and common-expression
   elimination. Schedule stages, allocate TEV registers and texture bindings.
3. Report an actionable build error when a graph cannot fit. Include stage count,
   limiting resource and the originating node. Do not silently change the effect.
4. Package the program and uniform layout as an asset; integrate asset validation
   with the existing SDK build. Load it with bounded memory and version checks.
5. Compare CPU reference results with Dolphin captures, including clamp boundaries
   and comparisons. Add optimizer tests using equivalent graphs and deliberately
   impossible graphs.

Deliverable: portable-looking material expressions with an honest GameCube subset.
General loops, arbitrary fragment memory access, derivatives and arbitrary floating
point programs remain unsupported. A GLSL front end should wait until the subset
and compiler are proven; accepting GLSL syntax does not remove hardware limits.

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

Ship the raw TEV demo and its pixel acceptance check first. Implement two managed
presets next, then use them to establish the effect representation, uniform
ownership and state-transition tests. Add indirect effects or the material graph
compiler after that foundation, according to developer demand. Keep the separate
backend proposal independent so it does not delay effects that can ship on GameCube.
