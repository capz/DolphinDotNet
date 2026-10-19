# TEV shader demo

A self-contained demonstration of GameCube's GPU pixel combiners. It starts
immediately with three panels, each drawing the same grayscale disk and ramp:

- **Reference:** one stage passes interpolated vertex intensity through.
- **Color ramp:** a managed preset in two stages mixes two colors using intensity; the bright color animates.
- **Toon bands:** a managed toon preset on the disk and a build-time compiled graph
  on the strip produce three discrete color bands. Their thresholds animate.

The small top row draws one original imported model three times: default,
toon override, default. It has a textured alpha-cutout material and an untextured
material. The first three frames also exercise default/effect/default over time;
then the middle panel stays toon shaded. `Renderer.DrawModel` borrows the effect
per draw without changing the shared model. Regenerate the embedded fixture with
`python3 scripts/generate_model_effect_fixture.py` (the existing glTF converter).

The CPU submits geometry and changes a few registers. TEV computes the ramp and
comparisons per fragment. This illustrates shading, not physical sphere lighting.
No GLSL/HLSL, downloaded assets, SD card, network or custom Dolphin build is needed.

Open `ShaderDemo.sln` and choose **ShaderDemo in Dolphin (play without debugger)**,
or use the included wait-for-Rider profile. **Start** pauses/resumes animation,
**B** exits and **Z** toggles statistics. Animation also runs without a controller.

```sh
dotnet build samples/ShaderDemo/ShaderDemo.sln -c Debug
```

The native build needs the same devkitPro/libogc2 setup as other samples. To check
C# compilation without it, add `-p:DndBuildNative=false`. The disks use `Material.Effect`; the right strip uses `Draw2D.SetEffect` with
`CompiledEffect.Package`, generated from `toon.dndfx.json` by the SDK before C#
compilation. The HUD uses the default pipeline after that batch, exercising state
restoration. Parameter values update between frames; no graph compiles per frame.

GameCube has no programmable vertex/fragment shader units. Its TEV hardware has
16 stages and a small fixed-point operation/register set. Managed effects expose a constrained subset of that functionality. See
[the effect API and graph schema](../../docs/effects.md). See
[shader support plans](../../docs/shader-support.md) for preset APIs, a restricted
material compiler, CPU effects, multipass effects and another-backend options.

The automated acceptance check captures software-Dolphin output and checks the
actual reference gradient, color ramp and three toon colors, plus the imported
model default/effect/default cycle and cutout alpha. It exits when enough
frames have been captured, with a 90-second failure deadline (not a fixed soak):

```sh
python3 scripts/dolphin_shader_test.py samples/ShaderDemo/bin/Debug/net8.0/ShaderDemo.dol --logs /tmp/shader-check
```

This requires Dolphin, Xvfb and FFmpeg. `frame.png` and `results.json` are retained
for inspection. Captures validate emulated output; final performance and precision
on physical GameCube hardware remain a separate check.

## API updates

The main stick adjusts the low/high toon thresholds; the C-stick adjusts the
bright color. A switches graph/preset rendering and Y resets parameters. Start
pauses animation, B exits, and Z toggles statistics. Uniform slots are cached;
changes update parameters between frames without recompiling the graph. The
original default appearance remains the automated visual reference.
